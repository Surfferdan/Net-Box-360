import { test } from "node:test";
import assert from "node:assert/strict";
import { LegacyHttp } from "../src/LegacyHttp.ts";
import { LegacySessionClient } from "../src/LegacySessionClient.ts";

function makeFetch(responses: Array<{ status: number; body: unknown }>) {
  let call = 0;
  const requests: Array<{ url: string; init?: RequestInit }> = [];
  const fetchImpl = (async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
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

test("LegacySessionClient.createSession posts to /api/session/start and adapts the response", async () => {
  const { fetchImpl, requests } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        game: "Halo 3",
        streamUrl: "https://stream/sess-1",
        status: "running",
        controllerStatus: "connected",
        canStopSession: true,
        assignedControllerSlot: 1,
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const client = new LegacySessionClient(http);

  const session = await client.createSession("halo3-iso");

  assert.equal(session.id, "sess-1");
  assert.equal(session.state, "running");
  assert.equal(session.streamUrl, "https://stream/sess-1");
  assert.equal(requests[0].url, "/api/session/start");
  assert.equal(JSON.parse(requests[0].init!.body as string).gameId, "halo3-iso");
});

test("LegacySessionClient.listSessions returns [] when there is no active session", async () => {
  const { fetchImpl } = makeFetch([{ status: 404, body: { error: "none" } }]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const client = new LegacySessionClient(http);

  const sessions = await client.listSessions();

  assert.deepEqual(sessions, []);
});

test("LegacySessionClient.listSessions returns the one active session when present", async () => {
  const { fetchImpl } = makeFetch([
    {
      status: 200,
      body: {
        sessionId: "sess-2",
        status: "running",
        game: "Gears of War",
        players: 2,
        canStopSession: true,
        streamUrl: "https://stream/sess-2",
        cloudMorphSessionId: "cm-1",
        error: null,
        streamHealth: "live",
        assignedControllerSlot: 1,
        occupiedControllerSlots: [1, 2],
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const client = new LegacySessionClient(http);

  const sessions = await client.listSessions();

  assert.equal(sessions.length, 1);
  assert.equal(sessions[0].id, "sess-2");
  assert.equal(sessions[0].players, 2);
});

test("LegacySessionClient.stopSession posts to /stop and re-fetches status", async () => {
  const { fetchImpl, requests } = makeFetch([
    { status: 200, body: { success: true, status: "stopping" } },
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        status: "stopped",
        game: "Halo 3",
        players: 0,
        canStopSession: false,
        streamUrl: null,
        cloudMorphSessionId: null,
        error: null,
        streamHealth: "stopped",
        assignedControllerSlot: 0,
        occupiedControllerSlots: [],
      },
    },
  ]);
  const http = new LegacyHttp({ fetchImpl, getToken: () => "token-1" });
  const client = new LegacySessionClient(http);

  const session = await client.stopSession("sess-1");

  assert.equal(session.state, "stopped");
  assert.equal(requests[0].url, "/api/session/sess-1/stop");
});

test("LegacyHttp retries once through /api/refresh on a 401", async () => {
  const { fetchImpl, requests } = makeFetch([
    { status: 401, body: { error: "expired" } },
    { status: 200, body: { token: "new-token" } },
    { status: 200, body: { sessionId: "sess-1", status: "running", game: "x", players: 1, canStopSession: true, streamUrl: null, cloudMorphSessionId: null, error: null, streamHealth: "live", assignedControllerSlot: 1, occupiedControllerSlots: [1] } },
  ]);
  let stored = "old-token";
  const http = new LegacyHttp({
    fetchImpl,
    getToken: () => stored,
    setToken: (t) => {
      stored = t;
    },
  });
  const client = new LegacySessionClient(http);

  const session = await client.getSession("sess-1");

  assert.ok(session);
  assert.equal(stored, "new-token");
  assert.equal(requests.length, 3);
});
