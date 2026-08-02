import { test } from "node:test";
import assert from "node:assert/strict";
import { NetBoxHttp } from "../src/http.ts";
import { StreamClient } from "../src/StreamClient.ts";

function mockFetch(responses: Array<{ status: number; body: string }>): typeof fetch {
  let call = 0;
  return (async () => {
    const response = responses[Math.min(call, responses.length - 1)];
    call++;
    return {
      ok: response.status >= 200 && response.status < 300,
      status: response.status,
      text: async () => response.body,
    } as unknown as Response;
  }) as typeof fetch;
}

test("StreamClient.getStreamInfo returns the {state, connection} shape from the spec", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 200, body: JSON.stringify({ state: "running", connection: "webrtc" }) }]) });
  const client = new StreamClient(http);

  const info = await client.getStreamInfo(1);
  assert.equal(info.state, "running");
  assert.equal(info.connection, "webrtc");
});

test("StreamClient.getStreamInfo reports stopped before a session is started", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 200, body: JSON.stringify({ state: "stopped", connection: "webrtc" }) }]) });
  const client = new StreamClient(http);

  const info = await client.getStreamInfo(1);
  assert.equal(info.state, "stopped");
});
