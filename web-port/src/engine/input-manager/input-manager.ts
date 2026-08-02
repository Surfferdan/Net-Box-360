export type DashboardInputAction =
  | "MoveLeft"
  | "MoveRight"
  | "MoveUp"
  | "MoveDown"
  | "Activate"
  | "Back"
  | "Details"
  | "Search"
  | "PreviousTab"
  | "NextTab"
  | "Guide";

export type DashboardInputSource = "keyboard" | "gamepad";

export interface DashboardInputEvent {
  action: DashboardInputAction;
  source: DashboardInputSource;
}

const KEY_MAP: Record<string, DashboardInputAction> = {
  ArrowLeft: "MoveLeft",
  KeyA: "MoveLeft",
  ArrowRight: "MoveRight",
  KeyD: "MoveRight",
  ArrowUp: "MoveUp",
  KeyW: "MoveUp",
  ArrowDown: "MoveDown",
  KeyS: "MoveDown",
  Enter: "Activate",
  Space: "Activate",
  Escape: "Back",
  Backspace: "Back",
  KeyB: "Back",
  KeyX: "Details",
  KeyY: "Search",
  KeyF: "Search",
  KeyQ: "PreviousTab",
  PageUp: "PreviousTab",
  KeyE: "NextTab",
  PageDown: "NextTab",
  F10: "Guide",
};

const STICK_DEADZONE = 0.5;
const STICK_REPEAT_MS = 170;

export class InputManager {
  private readonly listeners: Array<(event: DashboardInputEvent) => void> = [];
  private gamepadPollHandle = 0;
  private readonly lastButtons = new Map<string, boolean>();
  private readonly lastStickDirections = new Map<number, DashboardInputAction | null>();
  private readonly lastStickEmitAt = new Map<number, number>();

  public start(): void {
    window.addEventListener("keydown", this.onKeyDown);
    this.pollGamepad();
  }

  public dispose(): void {
    window.removeEventListener("keydown", this.onKeyDown);
    window.cancelAnimationFrame(this.gamepadPollHandle);
  }

  public onAction(listener: (event: DashboardInputEvent) => void): void {
    this.listeners.push(listener);
  }

  private emit(action: DashboardInputAction, source: DashboardInputSource): void {
    for (const listener of this.listeners) {
      listener({ action, source });
    }
  }

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    const action = KEY_MAP[event.code];
    if (!action) {
      return;
    }

    const target = event.target as HTMLElement | null;
    if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) {
      if (event.code !== "Enter" && event.code !== "Escape") {
        return;
      }
    }

    event.preventDefault();
    this.emit(action, "keyboard");
  };

  private pollGamepad = (): void => {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const pad = pads[0];
    if (pad) {
      this.checkLeftStick(pad);
      this.checkButton(pad, 14, "MoveLeft");
      this.checkButton(pad, 15, "MoveRight");
      this.checkButton(pad, 12, "MoveUp");
      this.checkButton(pad, 13, "MoveDown");
      this.checkButton(pad, 0, "Activate");
      this.checkButton(pad, 1, "Back");
      this.checkButton(pad, 2, "Details");
      this.checkButton(pad, 3, "Search");
      this.checkButton(pad, 4, "PreviousTab");
      this.checkButton(pad, 5, "NextTab");
      this.checkButton(pad, 16, "Guide");
    }

    this.gamepadPollHandle = window.requestAnimationFrame(this.pollGamepad);
  };

  private checkLeftStick(pad: Gamepad): void {
    const x = pad.axes[0] ?? 0;
    const y = pad.axes[1] ?? 0;

    let direction: DashboardInputAction | null = null;
    if (Math.abs(x) >= STICK_DEADZONE || Math.abs(y) >= STICK_DEADZONE) {
      if (Math.abs(x) > Math.abs(y)) {
        direction = x > 0 ? "MoveRight" : "MoveLeft";
      } else {
        direction = y > 0 ? "MoveDown" : "MoveUp";
      }
    }

    const previousDirection = this.lastStickDirections.get(pad.index) ?? null;
    const now = performance.now();
    const lastEmit = this.lastStickEmitAt.get(pad.index) ?? 0;

    if (!direction) {
      this.lastStickDirections.set(pad.index, null);
      return;
    }

    if (direction !== previousDirection || now - lastEmit >= STICK_REPEAT_MS) {
      this.emit(direction, "gamepad");
      this.lastStickDirections.set(pad.index, direction);
      this.lastStickEmitAt.set(pad.index, now);
    }
  }

  private checkButton(pad: Gamepad, index: number, action: DashboardInputAction): void {
    const button = pad.buttons[index];
    if (!button) {
      return;
    }

    const key = `${pad.index}:${index}`;
    const wasDown = this.lastButtons.get(key) ?? false;
    const isDown = button.pressed;
    this.lastButtons.set(key, isDown);

    if (!wasDown && isDown) {
      this.emit(action, "gamepad");
    }
  }
}
