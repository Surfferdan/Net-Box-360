import { test } from "node:test";
import assert from "node:assert/strict";
import { LegacyHttp } from "../src/LegacyHttp.ts";
import { LegacyPlayerClient } from "../src/LegacyPlayerClient.ts";
import { LegacyStreamClient } from "../src/LegacyStreamClient.ts";

function makeFetch(responses: Array<{ status: number; body: unknown }>) {
  let call = 0;
  const requests: string[] = [];
  const fetchImpl = (async (url: string) => {
    requests.push(url);
    const next = responses[Math.min(call, responses.length - 1)];
    call += 1;
    return {
      ok: next.status >= 200 && next.status < 300,
      status: next.status,
      text: async () => (next.body === undefined ? "" : JSON.stringify(next.body)),
    } as Response;
  }) as typeof fetch;
  return { fetchImpl, requests };
}

test("LegacyPlayerClient.join posts to /join and returns the server-assigned slot", async () => {
  const { fetchImpl, requests } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        game: "Halo 3",
        streamUrl: "https://stream/sess-1",
        controllerStatus: "connected",
        assignedControllerSlot: 2,
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const player = new LegacyPlayerClient(http, "sess-1");

  const result = await player.join();

  assert.equal(result.controllerSlot, 2);
  assert.equal(requests[0], "/api/session/sess-1/join");
});

test("LegacyPlayerClient.leave posts to /leave", async () => {
  const { fetchImpl, requests } = makeFetch([
    { status: 200, body: { success: true, status: "running", sessionId: "sess-1", playersRemaining: 1 } },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const player = new LegacyPlayerClient(http, "sess-1");

  const result = await player.leave();

  assert.equal(result.playersRemaining, 1);
  assert.equal(requests[0], "/api/session/sess-1/leave");
});

test("LegacyPlayerClient.getPlayers synthesizes from occupiedControllerSlots", async () => {
  const { fetchImpl } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        status: "running",
        game: "Halo 3",
        players: 2,
        canStopSession: true,
        streamUrl: "https://stream/sess-1",
        cloudMorphSessionId: "cm-1",
        error: null,
        streamHealth: "live",
        assignedControllerSlot: 1,
        occupiedControllerSlots: [1, 2],
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const player = new LegacyPlayerClient(http, "sess-1");

  const players = await player.getPlayers();

  assert.deepEqual(players, [{ controllerSlot: 1 }, { controllerSlot: 2 }]);
});

test("LegacyStreamClient.getStreamInfo maps streamHealth + streamUrl from session status", async () => {
  const { fetchImpl } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        status: "running",
        game: "Halo 3",
        players: 1,
        canStopSession: true,
        streamUrl: "https://stream/sess-1",
        cloudMorphSessionId: "cm-1",
        error: null,
        streamHealth: "live",
        assignedControllerSlot: 1,
        occupiedControllerSlots: [1],
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const stream = new LegacyStreamClient(http);

  const info = await stream.getStreamInfo("sess-1");

  assert.equal(info.state, "running");
  assert.equal(info.connection, "https://stream/sess-1");
});

test("LegacyStreamClient.getStreamInfo maps capture-timeout to failed", async () => {
  const { fetchImpl } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        status: "running",
        game: "Halo 3",
        players: 1,
        canStopSession: true,
        streamUrl: null,
        cloudMorphSessionId: "cm-1",
        error: "capture timed out",
        streamHealth: "capture-timeout",
        assignedControllerSlot: 1,
        occupiedControllerSlots: [1],
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const stream = new LegacyStreamClient(http);

  const info = await stream.getStreamInfo("sess-1");

  assert.equal(info.state, "failed");
});
