import { test } from "node:test";
import assert from "node:assert/strict";
import { NetBoxEventClient } from "../src/NetBoxEventClient.ts";

// Minimal mock WebSocket - supports exactly what NetBoxEventClient uses
// (constructor(url), onmessage/onclose/onerror assignment, close()).
class MockWebSocket {
  public onmessage: ((event: { data: string }) => void) | null = null;
  public onclose: (() => void) | null = null;
  public onerror: (() => void) | null = null;
  public static instances: MockWebSocket[] = [];
  public readonly url: string;

  public constructor(url: string) {
    this.url = url;
    MockWebSocket.instances.push(this);
  }

  public close(): void {
    this.onclose?.();
  }

  public emit(data: unknown): void {
    this.onmessage?.({ data: JSON.stringify(data) });
  }
}

test("NetBoxEventClient.onRuntimeStarted fires for a RuntimeStarted frame", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();

  let received: unknown = null;
  client.onRuntimeStarted((event) => {
    received = event;
  });

  MockWebSocket.instances[0].emit({ type: "RuntimeStarted", session: 1 });

  assert.deepEqual(received, { type: "RuntimeStarted", session: 1 });
  client.disconnect();
});

test("NetBoxEventClient.onRuntimeError fires for a RuntimeFailed frame", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();

  let firedCount = 0;
  client.onRuntimeError(() => {
    firedCount++;
  });

  MockWebSocket.instances[0].emit({ type: "RuntimeFailed", session: 1 });
  assert.equal(firedCount, 1);
  client.disconnect();
});

test("NetBoxEventClient.onStreamReady fires for a StreamHealthy frame", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();

  let received: unknown = null;
  client.onStreamReady((event) => {
    received = event;
  });

  MockWebSocket.instances[0].emit({ type: "StreamHealthy", session: 2 });
  assert.deepEqual(received, { type: "StreamHealthy", session: 2 });
  client.disconnect();
});

test("NetBoxEventClient.onPlayerJoined/onPlayerLeft only fire for their own event type", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();

  let joinedCount = 0;
  let leftCount = 0;
  client.onPlayerJoined(() => joinedCount++);
  client.onPlayerLeft(() => leftCount++);

  MockWebSocket.instances[0].emit({ type: "PlayerJoined", session: 1, player: 5 });
  assert.equal(joinedCount, 1);
  assert.equal(leftCount, 0);

  MockWebSocket.instances[0].emit({ type: "PlayerLeft", session: 1, player: 5 });
  assert.equal(joinedCount, 1);
  assert.equal(leftCount, 1);

  client.disconnect();
});

test("NetBoxEventClient unsubscribe stops further callbacks", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();

  let count = 0;
  const unsubscribe = client.onRuntimeStarted(() => count++);
  MockWebSocket.instances[0].emit({ type: "RuntimeStarted", session: 1 });
  unsubscribe();
  MockWebSocket.instances[0].emit({ type: "RuntimeStarted", session: 1 });

  assert.equal(count, 1);
  client.disconnect();
});

test("NetBoxEventClient ignores malformed frames without throwing", () => {
  MockWebSocket.instances = [];
  const client = new NetBoxEventClient({ wsUrl: "ws://localhost/ws/events", webSocketImpl: MockWebSocket as unknown as typeof WebSocket });
  client.connect();
  client.onRuntimeStarted(() => {
    throw new Error("should not fire for malformed frame");
  });

  assert.doesNotThrow(() => MockWebSocket.instances[0].onmessage?.({ data: "not json" }));
  client.disconnect();
});
