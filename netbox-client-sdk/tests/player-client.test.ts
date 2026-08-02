import { test } from "node:test";
import assert from "node:assert/strict";
import { NetBoxHttp } from "../src/http.ts";
import { PlayerClient } from "../src/PlayerClient.ts";

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

test("PlayerClient.join posts controller_slot and returns the player", async () => {
  const http = new NetBoxHttp({
    fetchImpl: mockFetch([{ status: 200, body: JSON.stringify({ id: 1, session: 1, controller_slot: 0, connection_state: "Connected" }) }]),
  });
  const client = new PlayerClient(http, 1);

  const player = await client.join(0);
  assert.equal(player.controller_slot, 0);
  assert.equal(player.connection_state, "Connected");
});

test("PlayerClient.getPlayers returns an empty array by default", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 200, body: "[]" }]) });
  const client = new PlayerClient(http, 1);

  const players = await client.getPlayers();
  assert.deepEqual(players, []);
});

test("PlayerClient.leave issues a DELETE without throwing on empty body", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 204, body: "" }]) });
  const client = new PlayerClient(http, 1);

  await assert.doesNotReject(() => client.leave(1));
});

test("PlayerClient.join surfaces a conflict as a rejected promise", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 409, body: '{"error":"controller_slot_occupied"}' }]) });
  const client = new PlayerClient(http, 1);

  await assert.rejects(() => client.join(0));
});
