import { test } from "node:test";
import assert from "node:assert/strict";
import { ControllerBridge, XBOX_BUTTON_MAP, type GamepadSource } from "../src/ControllerBridge.ts";

function makeGamepad(index: number, id: string, pressedButtons: number[] = []): Gamepad {
  const buttons = Array.from({ length: 17 }, (_, buttonIndex) => ({
    pressed: pressedButtons.includes(buttonIndex),
    touched: false,
    value: pressedButtons.includes(buttonIndex) ? 1 : 0,
  }));
  return { index, id, connected: true, buttons, axes: [], mapping: "standard", timestamp: 0 } as unknown as Gamepad;
}

class FakeGamepadSource implements GamepadSource {
  public gamepads: Array<Gamepad | null> = [];
  public getGamepads(): Array<Gamepad | null> {
    return this.gamepads;
  }
}

test("ControllerBridge detects a newly connected controller", () => {
  const source = new FakeGamepadSource();
  const connected: string[] = [];
  const bridge = new ControllerBridge(source, { onControllerConnected: (c) => connected.push(c.id) });

  source.gamepads = [makeGamepad(0, "Xbox 360 Controller")];
  bridge.poll();

  assert.deepEqual(connected, ["Xbox 360 Controller"]);
  assert.equal(bridge.getConnectedControllers().length, 1);
});

test("ControllerBridge detects disconnection", () => {
  const source = new FakeGamepadSource();
  const disconnected: number[] = [];
  const bridge = new ControllerBridge(source, { onControllerDisconnected: (c) => disconnected.push(c.index) });

  source.gamepads = [makeGamepad(0, "Pad A")];
  bridge.poll();
  source.gamepads = [];
  bridge.poll();

  assert.deepEqual(disconnected, [0]);
  assert.equal(bridge.getConnectedControllers().length, 0);
});

test("ControllerBridge assigns and releases player slots without any network call", () => {
  const source = new FakeGamepadSource();
  const bridge = new ControllerBridge(source);

  bridge.assignSlot(0, 2);
  assert.equal(bridge.getSlotAssignments().get(0), 2);

  bridge.releaseSlot(0);
  assert.equal(bridge.getSlotAssignments().has(0), false);
});

test("ControllerBridge maps the A button press using XBOX_BUTTON_MAP", () => {
  const source = new FakeGamepadSource();
  const presses: string[] = [];
  const bridge = new ControllerBridge(source, { onButtonDown: (_index, name) => presses.push(name) });

  source.gamepads = [makeGamepad(0, "Pad A")];
  bridge.poll(); // establish baseline (no buttons pressed)

  source.gamepads = [makeGamepad(0, "Pad A", [XBOX_BUTTON_MAP.A])];
  bridge.poll();

  assert.deepEqual(presses, ["A"]);
});

test("ControllerBridge only fires onButtonDown on the rising edge", () => {
  const source = new FakeGamepadSource();
  let pressCount = 0;
  const bridge = new ControllerBridge(source, { onButtonDown: () => pressCount++ });

  source.gamepads = [makeGamepad(0, "Pad A", [XBOX_BUTTON_MAP.A])];
  bridge.poll();
  bridge.poll(); // still held - should not re-fire

  assert.equal(pressCount, 1);
});
