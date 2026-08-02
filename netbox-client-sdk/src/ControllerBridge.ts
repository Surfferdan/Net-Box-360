export interface DetectedController {
  index: number;
  id: string;
  connected: boolean;
}

// Simplified Xbox 360-style button mapping over the standard Gamepad API
// button index layout, so blade UI code can work with names instead of
// magic indices.
export const XBOX_BUTTON_MAP: Record<string, number> = {
  A: 0,
  B: 1,
  X: 2,
  Y: 3,
  LB: 4,
  RB: 5,
  LT: 6,
  RT: 7,
  Back: 8,
  Start: 9,
  LeftStick: 10,
  RightStick: 11,
  DPadUp: 12,
  DPadDown: 13,
  DPadLeft: 14,
  DPadRight: 15,
  Guide: 16,
};

export interface ControllerBridgeCallbacks {
  onControllerConnected?: (controller: DetectedController) => void;
  onControllerDisconnected?: (controller: DetectedController) => void;
  onButtonDown?: (gamepadIndex: number, buttonName: string) => void;
}

export interface GamepadSource {
  getGamepads(): Array<Gamepad | null>;
}

/**
 * Frontend-only bridge over the browser Gamepad API. Per the Phase 10
 * spec, this does NOT implement backend input transport - it only
 * supports controller detection, player-slot selection, and button-name
 * mapping, ready for a future "Future Input Endpoint" to be plugged into
 * onButtonDown()/getSlotAssignments() once a real input transport exists.
 */
export class ControllerBridge {
  private pollHandle: ReturnType<typeof setInterval> | null = null;
  private readonly knownControllers = new Map<number, DetectedController>();
  private readonly slotAssignments = new Map<number, number>(); // gamepadIndex -> slot
  private readonly previousButtonState = new Map<number, boolean[]>();
  private readonly source: GamepadSource;
  private readonly callbacks: ControllerBridgeCallbacks;

  public constructor(source: GamepadSource, callbacks: ControllerBridgeCallbacks = {}) {
    this.source = source;
    this.callbacks = callbacks;
  }

  public startPolling(intervalMs = 100): void {
    if (this.pollHandle !== null) {
      return;
    }
    this.pollHandle = setInterval(() => this.poll(), intervalMs);
  }

  public stopPolling(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
  }

  /** Runs one detection/input pass immediately (also used by tests, which
   * don't want to wait on a real interval timer). */
  public poll(): void {
    const gamepads = this.source.getGamepads();
    const seenIndices = new Set<number>();

    for (const gamepad of gamepads) {
      if (!gamepad) {
        continue;
      }
      seenIndices.add(gamepad.index);

      if (!this.knownControllers.has(gamepad.index)) {
        const detected: DetectedController = { index: gamepad.index, id: gamepad.id, connected: true };
        this.knownControllers.set(gamepad.index, detected);
        this.callbacks.onControllerConnected?.(detected);
      }

      this.pollButtons(gamepad);
    }

    for (const [index, controller] of this.knownControllers) {
      if (!seenIndices.has(index)) {
        this.knownControllers.delete(index);
        this.slotAssignments.delete(index);
        this.previousButtonState.delete(index);
        this.callbacks.onControllerDisconnected?.({ ...controller, connected: false });
      }
    }
  }

  public getConnectedControllers(): DetectedController[] {
    return [...this.knownControllers.values()];
  }

  /** Assigns a detected controller (by gamepad index) to a player slot
   * (0-3, matching Xenia's 4-controller limit / netbox-server's
   * PlayerRegistry controller_slot field). Purely local bookkeeping - does
   * not call any backend endpoint. */
  public assignSlot(gamepadIndex: number, slot: number): void {
    this.slotAssignments.set(gamepadIndex, slot);
  }

  public releaseSlot(gamepadIndex: number): void {
    this.slotAssignments.delete(gamepadIndex);
  }

  public getSlotAssignments(): Map<number, number> {
    return new Map(this.slotAssignments);
  }

  private pollButtons(gamepad: Gamepad): void {
    const previous = this.previousButtonState.get(gamepad.index) ?? [];
    const current = gamepad.buttons.map((button) => button.pressed);

    for (const [name, buttonIndex] of Object.entries(XBOX_BUTTON_MAP)) {
      const wasPressed = previous[buttonIndex] ?? false;
      const isPressed = current[buttonIndex] ?? false;
      if (isPressed && !wasPressed) {
        this.callbacks.onButtonDown?.(gamepad.index, name);
      }
    }

    this.previousButtonState.set(gamepad.index, current);
  }
}
