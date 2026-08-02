import { getNetBoxApiBaseUrl, getSessionToken } from "./NetBoxClient";

/** Xbox 360 XINPUT button bitmask values (must match xe::hid X_INPUT_GAMEPAD_BUTTON). */
const XINPUT_DPAD_UP = 0x0001;
const XINPUT_DPAD_DOWN = 0x0002;
const XINPUT_DPAD_LEFT = 0x0004;
const XINPUT_DPAD_RIGHT = 0x0008;
const XINPUT_START = 0x0010;
const XINPUT_BACK = 0x0020;
const XINPUT_LEFT_THUMB = 0x0040;
const XINPUT_RIGHT_THUMB = 0x0080;
const XINPUT_LEFT_SHOULDER = 0x0100;
const XINPUT_RIGHT_SHOULDER = 0x0200;
const XINPUT_GUIDE = 0x0400;
const XINPUT_A = 0x1000;
const XINPUT_B = 0x2000;
const XINPUT_X = 0x4000;
const XINPUT_Y = 0x8000;

/** Standard Gamepad API button index -> XINPUT bit (W3C "standard" gamepad mapping). */
const STANDARD_BUTTON_TO_XINPUT: Record<number, number> = {
  0: XINPUT_A,
  1: XINPUT_B,
  2: XINPUT_X,
  3: XINPUT_Y,
  4: XINPUT_LEFT_SHOULDER,
  5: XINPUT_RIGHT_SHOULDER,
  8: XINPUT_BACK,
  9: XINPUT_START,
  10: XINPUT_LEFT_THUMB,
  11: XINPUT_RIGHT_THUMB,
  12: XINPUT_DPAD_UP,
  13: XINPUT_DPAD_DOWN,
  14: XINPUT_DPAD_LEFT,
  15: XINPUT_DPAD_RIGHT,
  16: XINPUT_GUIDE,
};

const STICK_DEADZONE = 0.08;
const POLL_HZ = 200;
const POLL_INTERVAL_MS = 1000 / POLL_HZ;

function applyDeadzone(value: number): number {
  return Math.abs(value) < STICK_DEADZONE ? 0 : value;
}

function toStickInt16(value: number): number {
  const clamped = Math.max(-1, Math.min(1, applyDeadzone(value)));
  return Math.round(clamped * 32767);
}

function toTriggerByte(value: number): number {
  const clamped = Math.max(0, Math.min(1, value));
  return Math.round(clamped * 255);
}

function resolveInputWsUrl(sessionId: string, token: string): string {
  const base = getNetBoxApiBaseUrl();
  const query = `token=${encodeURIComponent(token)}&sessionId=${encodeURIComponent(sessionId)}`;
  if (base) {
    return `${base.replace(/^http/, "ws")}/ws/input?${query}`;
  }

  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}/ws/input?${query}`;
}

export type ConnectionQuality = "unknown" | "good" | "fair" | "poor" | "disconnected";

export interface GamepadStatus {
  connected: boolean;
  name: string | null;
  index: number | null;
  quality: ConnectionQuality;
}

export type GamepadStatusListener = (status: GamepadStatus) => void;

/**
 * Browser Gamepad API -> native XInput bridge over the NetBox `/ws/input`
 * WebSocket. Polls navigator.getGamepads() at up to 200Hz, maps the W3C
 * "standard" gamepad layout to the Xbox 360 XINPUT button bitmask, and
 * streams fixed 16-byte binary frames tagged with an incrementing sequence
 * number so the server can drop stale/out-of-order packets. This is the
 * authoritative controller-input path for the NetBox pipeline - no
 * keyboard/mouse emulation, no synthetic Windows input. Distinct from (and
 * unrelated to) GameControllerInput.ts, which drives the older CloudMorph
 * WebRTC data-channel keyboard-emulation pathway.
 */
export class GamepadInputClient {
  private socket: WebSocket | null = null;
  private pollHandle: number | null = null;
  private sequence = 0;
  private gamepadIndex: number | null = null;
  private active = false;
  private connected = false;
  private lastPongAt = 0;
  private statusListener: GamepadStatusListener | null = null;

  public constructor(
    private readonly sessionId: string,
  ) {}

  public setStatusListener(listener: GamepadStatusListener | null): void {
    this.statusListener = listener;
    this.emitStatus();
  }

  public start(): void {
    if (this.active) {
      return;
    }

    const token = getSessionToken();
    if (!token) {
      return;
    }

    this.active = true;
    this.socket = new WebSocket(resolveInputWsUrl(this.sessionId, token));
    this.socket.binaryType = "arraybuffer";
    this.socket.addEventListener("open", () => {
      this.connected = true;
      this.emitStatus();
    });
    this.socket.addEventListener("close", () => {
      this.connected = false;
      this.emitStatus();
    });
    this.socket.addEventListener("error", () => {
      this.connected = false;
      this.emitStatus();
    });

    window.addEventListener("gamepadconnected", this.onGamepadChanged);
    window.addEventListener("gamepaddisconnected", this.onGamepadChanged);
    this.pollHandle = window.setInterval(this.poll, POLL_INTERVAL_MS);
  }

  public stop(): void {
    this.active = false;
    if (this.pollHandle !== null) {
      window.clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
    window.removeEventListener("gamepadconnected", this.onGamepadChanged);
    window.removeEventListener("gamepaddisconnected", this.onGamepadChanged);
    this.socket?.close();
    this.socket = null;
    this.connected = false;
    this.gamepadIndex = null;
    this.emitStatus();
  }

  private readonly onGamepadChanged = (): void => {
    this.emitStatus();
  };

  private emitStatus(): void {
    if (!this.statusListener) {
      return;
    }

    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const pad = this.gamepadIndex !== null ? pads[this.gamepadIndex] : null;

    this.statusListener({
      connected: !!pad,
      name: pad?.id ?? null,
      index: this.gamepadIndex,
      quality: !this.connected ? "disconnected" : pad ? "good" : "unknown",
    });
  }

  private readonly poll = (): void => {
    if (!this.active) {
      return;
    }

    const pads = navigator.getGamepads ? navigator.getGamepads() : [];

    if (this.gamepadIndex === null || !pads[this.gamepadIndex]) {
      this.gamepadIndex = null;
      for (let i = 0; i < pads.length; i += 1) {
        if (pads[i]) {
          this.gamepadIndex = i;
          break;
        }
      }
    }

    const pad = this.gamepadIndex !== null ? pads[this.gamepadIndex] : null;
    if (pad && this.socket && this.socket.readyState === WebSocket.OPEN) {
      this.sendFrame(pad);
    }
  };

  private sendFrame(pad: Gamepad): void {
    let buttons = 0;
    for (const [indexStr, bit] of Object.entries(STANDARD_BUTTON_TO_XINPUT)) {
      const index = Number(indexStr);
      if (pad.buttons[index]?.pressed) {
        buttons |= bit;
      }
    }

    const leftStickX = toStickInt16(pad.axes[0] ?? 0);
    const leftStickY = toStickInt16(-(pad.axes[1] ?? 0));
    const rightStickX = toStickInt16(pad.axes[2] ?? 0);
    const rightStickY = toStickInt16(-(pad.axes[3] ?? 0));
    const leftTrigger = toTriggerByte(pad.buttons[6]?.value ?? 0);
    const rightTrigger = toTriggerByte(pad.buttons[7]?.value ?? 0);

    this.sequence = (this.sequence + 1) >>> 0;

    const buffer = new ArrayBuffer(16);
    const view = new DataView(buffer);
    view.setUint32(0, this.sequence, true);
    view.setUint16(4, buttons, true);
    view.setInt16(6, leftStickX, true);
    view.setInt16(8, leftStickY, true);
    view.setInt16(10, rightStickX, true);
    view.setInt16(12, rightStickY, true);
    view.setUint8(14, leftTrigger);
    view.setUint8(15, rightTrigger);

    this.socket?.send(buffer);
  }
}

export const GAMEPAD_INPUT_POLL_INTERVAL_MS = POLL_INTERVAL_MS;
