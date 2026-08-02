export type ControlSender = (message: unknown) => void;

interface InputFrameEnvelope {
  v: 1;
  sessionId: string | null;
  playerSlot: number;
  frameId: number;
  timestampMs: number;
  type: "gamepad";
  buttons: number[];
  stick: string;
}

interface GameControllerInputOptions {
  sessionId: string | null;
  playerSlot: number;
}

/** A physical gamepad currently visible to the browser and its local couch-co-op binding, if any. */
export interface ConnectedGamepadInfo {
  index: number;
  id: string;
  /** Player slot (1-4) this gamepad is currently bound to, or null if unbound/idle. */
  slot: number | null;
}

const GAMEPAD_DEADZONE = 0.35;
const GAMEPAD_POLL_BUTTON_COUNT = 16;
const MIN_PLAYER_SLOT = 1;
const MAX_PLAYER_SLOT = 4;

function clampSlot(slot: number): number {
  return Math.min(MAX_PLAYER_SLOT, Math.max(MIN_PLAYER_SLOT, Math.trunc(slot || MIN_PLAYER_SLOT)));
}

/**
 * Controller-only in-game input bridge: forwards Gamepad API state to the
 * CloudMorph/Xenia bridge over WebRTC data channel. Keyboard/mouse forwarding
 * is intentionally disabled for stream security.
 *
 * Supports local couch co-op: additional physical gamepads connected to this
 * machine can be bound to extra player slots (2-4) via bindGamepad(), and
 * their input is sent as separate slot-tagged frames over the SAME data
 * channel as the primary controller. The very first detected gamepad is
 * auto-bound to the network-assigned playerSlot so single-controller usage
 * behaves exactly as before with no explicit binding required.
 *
 * Known limitation: the CloudMorph/xenia_bridge.go input bridge currently
 * composites all active player slots' button/stick state into a single
 * synthesized keyboard input stream (see pumpInput/composeSlots server-side).
 * This means simultaneous distinct analog control per couch player is not
 * yet possible - true per-slot isolation requires a virtual multi-controller
 * injector (e.g. ViGEmBus) on the bridge side, which is out of scope here.
 */
export class GameControllerInput {
  private rafHandle = 0;
  private active = false;
  private enabled = true;

  /** gamepadIndex -> playerSlot for gamepads explicitly (or auto-) bound to a slot. */
  private readonly bindings = new Map<number, number>();
  private readonly slotFrameIds = new Map<number, number>();
  private primaryAutoBoundIndex: number | null = null;
  private onControllersChanged: (() => void) | null = null;

  public constructor(
    private readonly target: HTMLElement,
    private readonly send: ControlSender,
    private readonly options: GameControllerInputOptions,
  ) {}

  public start(): void {
    this.enabled = true;
    this.begin();
  }

  public stop(): void {
    this.enabled = false;
    this.end();
  }

  public setEnabled(value: boolean): void {
    if (this.enabled === value) {
      return;
    }

    this.enabled = value;
    if (value) {
      this.begin();
    } else {
      this.end();
    }
  }

  /** Registers a callback invoked whenever the set of connected/bound gamepads changes. */
  public setOnControllersChanged(callback: (() => void) | null): void {
    this.onControllersChanged = callback;
  }

  /** Returns every gamepad currently visible to the browser and its couch-co-op slot binding, if any. */
  public getConnectedGamepads(): ConnectedGamepadInfo[] {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const result: ConnectedGamepadInfo[] = [];
    for (let i = 0; i < pads.length; i += 1) {
      const pad = pads[i];
      if (!pad) {
        continue;
      }
      result.push({ index: i, id: pad.id, slot: this.bindings.get(i) ?? null });
    }
    return result;
  }

  /** Binds a physical gamepad (by its Gamepad API index) to a local couch-co-op player slot (1-4). */
  public bindGamepad(gamepadIndex: number, slot: number): void {
    const normalizedSlot = clampSlot(slot);

    // Only one gamepad may drive a given slot at a time.
    for (const [index, boundSlot] of this.bindings) {
      if (boundSlot === normalizedSlot && index !== gamepadIndex) {
        this.bindings.delete(index);
      }
    }

    this.bindings.set(gamepadIndex, normalizedSlot);
    if (normalizedSlot === clampSlot(this.options.playerSlot)) {
      this.primaryAutoBoundIndex = gamepadIndex;
    }
    this.onControllersChanged?.();
  }

  /** Releases a gamepad's slot binding and sends a neutral frame so the slot stops applying input. */
  public unbindGamepad(gamepadIndex: number): void {
    const slot = this.bindings.get(gamepadIndex);
    if (slot === undefined) {
      return;
    }

    this.bindings.delete(gamepadIndex);
    if (this.primaryAutoBoundIndex === gamepadIndex) {
      this.primaryAutoBoundIndex = null;
    }
    this.sendGamepadFrameForSlot(slot, [], "");
    this.onControllersChanged?.();
  }

  private begin(): void {
    if (this.active) {
      return;
    }
    this.active = true;

    window.addEventListener("blur", this.onWindowBlur);
    document.addEventListener("visibilitychange", this.onVisibilityChange);
    window.addEventListener("gamepadconnected", this.onGamepadConnectionChange);
    window.addEventListener("gamepaddisconnected", this.onGamepadConnectionChange);

    this.pollGamepad();
  }

  private end(): void {
    if (this.active) {
      this.active = false;
      window.cancelAnimationFrame(this.rafHandle);
      window.removeEventListener("blur", this.onWindowBlur);
      document.removeEventListener("visibilitychange", this.onVisibilityChange);
      window.removeEventListener("gamepadconnected", this.onGamepadConnectionChange);
      window.removeEventListener("gamepaddisconnected", this.onGamepadConnectionChange);
    }

    // Always emit a neutral frame when stopping/suspending input so held
    // game buttons/stick directions are released server-side.
    this.sendNeutralState();
    this.bindings.clear();
    this.slotFrameIds.clear();
    this.primaryAutoBoundIndex = null;
  }

  private readonly onGamepadConnectionChange = (): void => {
    this.onControllersChanged?.();
  };

  private readonly pollGamepad = (): void => {
    if (!this.active || !this.enabled) {
      return;
    }

    const pads = navigator.getGamepads ? navigator.getGamepads() : [];

    // Auto-bind the first connected gamepad to the network-assigned player
    // slot so single-controller usage behaves exactly as before.
    if (this.primaryAutoBoundIndex === null) {
      for (let i = 0; i < pads.length; i += 1) {
        if (pads[i]) {
          this.bindGamepad(i, this.options.playerSlot);
          break;
        }
      }
    }

    for (const [gamepadIndex, slot] of this.bindings) {
      const pad = pads[gamepadIndex];
      if (!pad) {
        continue;
      }

      const buttons: number[] = [];
      for (let i = 0; i < Math.min(pad.buttons.length, GAMEPAD_POLL_BUTTON_COUNT); i += 1) {
        if (pad.buttons[i]?.pressed) {
          buttons.push(i);
        }
      }
      this.sendGamepadFrameForSlot(slot, buttons, this.resolveStick(pad));
    }

    this.rafHandle = window.requestAnimationFrame(this.pollGamepad);
  };

  private resolveStick(pad: Gamepad): string {
    const x = pad.axes[0] ?? 0;
    const y = pad.axes[1] ?? 0;

    if (Math.abs(x) < GAMEPAD_DEADZONE && Math.abs(y) < GAMEPAD_DEADZONE) {
      return "";
    }

    if (Math.abs(x) > Math.abs(y)) {
      return x > 0 ? "right" : "left";
    }
    return y > 0 ? "down" : "up";
  }

  private sendNeutralState(): void {
    if (this.bindings.size === 0) {
      this.sendGamepadFrameForSlot(this.options.playerSlot, [], "");
      return;
    }

    for (const slot of this.bindings.values()) {
      this.sendGamepadFrameForSlot(slot, [], "");
    }
  }

  private sendGamepadFrameForSlot(slot: number, buttons: number[], stick: string): void {
    const playerSlot = clampSlot(slot);
    const nextFrameId = (this.slotFrameIds.get(playerSlot) ?? 0) + 1;
    this.slotFrameIds.set(playerSlot, nextFrameId);

    const frame: InputFrameEnvelope = {
      v: 1,
      sessionId: this.options.sessionId,
      playerSlot,
      frameId: nextFrameId,
      timestampMs: Date.now(),
      type: "gamepad",
      buttons,
      stick,
    };

    this.send(frame);
  }

  private readonly onWindowBlur = (): void => {
    this.sendNeutralState();
  };

  private readonly onVisibilityChange = (): void => {
    if (document.hidden) {
      this.sendNeutralState();
    }
  };
}
