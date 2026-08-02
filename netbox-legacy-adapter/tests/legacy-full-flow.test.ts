import { test } from "node:test";
import assert from "node:assert/strict";
import { LegacyHttp } from "../src/LegacyHttp.ts";
import { LegacyNetBoxAdapter } from "../src/index.ts";

// End-to-end mocked "real user flow" from the Phase 12 verification
// checklist, steps 1-8 (login, load profile, open Games Blade, launch
// game, create session, start Xenia, connect WebRTC stream info).
// Controller connection (step 9) and actual gameplay (step 10) are
// browser/hardware-only and remain covered by netbox-client-sdk's
// ControllerBridge + StreamClient.connectWebRTC, unchanged by this
// package - only the account/games/session/stream data plane is
// exercised here.
function makeSequencedFetch(responses: Array<{ status: number; body: unknown }>) {
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

test("Full legacy-backed flow: login -> profile -> games -> launch -> join -> stream -> stop", async () => {
  const { fetchImpl, requests } = makeSequencedFetch([
    { status: 200, body: { token: "session-token", userId: 1, username: "player1" } }, // login
    { status: 200, body: { userId: 1, username: "player1", displayName: "Player One" } }, // profile
    {
      status: 200,
      body: [
        {
          id: "halo3",
          name: "halo3.iso",
          titleId: "4D5307E6",
          title: "Halo 3",
          relativePath: "halo3.iso",
          fullPath: "C:/games/halo3.iso",
          extension: ".iso",
          sizeBytes: 123456,
          genre: null,
          players: 4,
          lastWriteTimeUtc: "2026-07-01T00:00:00Z",
          coverPath: null,
        },
      ],
    }, // games
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
    }, // POST /api/session/start
    {
      status: 200,
      body: {
        sessionId: "sess-1",
        game: "Halo 3",
        streamUrl: "https://stream/sess-1",
        controllerStatus: "connected",
        assignedControllerSlot: 1,
      },
    }, // POST /api/session/sess-1/join
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
    }, // GET stream info
    { status: 200, body: { success: true, status: "stopping" } }, // POST stop
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
    }, // GET after stop
  ]);

  let token: string | null = null;
  const adapter = new LegacyNetBoxAdapter({
    http: { fetchImpl, getToken: () => token, setToken: (t) => (token = t) },
  });

  // 1-2. Login.
  const login = await adapter.account.login("player1", "hunter2");
  token = login.token;
  assert.equal(token, "session-token");

  // 3. Load profile.
  const profile = await adapter.profile.getCurrentProfile();
  assert.equal(profile.displayName, "Player One");

  // 4. Open Games Blade.
  const games = await adapter.games.listGames();
  assert.equal(games.length, 1);
  assert.equal(games[0].title, "Halo 3");

  // 5-7. Launch game -> create session -> start Xenia (atomic on legacy backend).
  const session = await adapter.sessions.createSession(games[0].id);
  assert.equal(session.state, "running");
  assert.equal(session.id, "sess-1");

  // 9 (assignment half). Join with a controller (server-assigned slot).
  const player = await adapter.playerClientFor(session.id).join();
  assert.equal(player.controllerSlot, 1);

  // 8. Connect WebRTC stream (info step - actual peer connection is
  // netbox-client-sdk's StreamClient.connectWebRTC, unchanged).
  const streamInfo = await adapter.stream.getStreamInfo(session.id);
  assert.equal(streamInfo.state, "running");
  assert.equal(streamInfo.connection, "https://stream/sess-1");

  // Teardown.
  const stopped = await adapter.sessions.stopSession(session.id);
  assert.equal(stopped.state, "stopped");

  assert.equal(requests[0], "/api/login");
  assert.equal(requests[1], "/api/profile/me");
  assert.equal(requests[2], "/api/games");
  assert.equal(requests[3], "/api/session/start");
  assert.equal(requests[4], "/api/session/sess-1/join");
  assert.equal(requests[5], "/api/session/sess-1");
  assert.equal(requests[6], "/api/session/sess-1/stop");
});
