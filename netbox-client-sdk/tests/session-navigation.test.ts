import { test } from "node:test";
import assert from "node:assert/strict";
import { NetBoxHttp } from "../src/http.ts";
import { SessionClient } from "../src/SessionClient.ts";
import { PlayerClient } from "../src/PlayerClient.ts";
import { StreamClient } from "../src/StreamClient.ts";
import type { Session } from "../src/types.ts";

// Exercises the exact "Play Game -> Create Session -> Start Runtime ->
// Open Stream" flow from the Phase 10 spec's Games Blade action, purely
// against mocked HTTP responses (no real netbox-api server).
function mockFetchSequence(responses: Array<{ status: number; body: string }>): typeof fetch {
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

function sessionJson(overrides: Partial<Session>): string {
  return JSON.stringify({ id: 1, runtime: 1, stream: 1, state: "Created", players: [], ...overrides });
}

test("Games Blade navigation: play game creates, starts, joins a player, and reads stream info", async () => {
  const http = new NetBoxHttp({
    fetchImpl: mockFetchSequence([
      { status: 200, body: sessionJson({ state: "Created" }) }, // createSession
      { status: 200, body: sessionJson({ state: "Running" }) }, // startSession
      { status: 200, body: JSON.stringify({ id: 9, session: 1, controller_slot: 0, connection_state: "Connected" }) }, // join
      { status: 200, body: JSON.stringify({ state: "running", connection: "webrtc" }) }, // getStreamInfo
    ]),
  });

  const sessions = new SessionClient(http);
  const stream = new StreamClient(http);

  const session = await sessions.createSession();
  assert.equal(session.state, "Created");

  const started = await sessions.startSession(session.id);
  assert.equal(started.state, "Running");

  const players = new PlayerClient(http, started.id);
  const player = await players.join(0);
  assert.equal(player.controller_slot, 0);

  const streamInfo = await stream.getStreamInfo(started.id);
  assert.equal(streamInfo.state, "running");
  assert.equal(streamInfo.connection, "webrtc");
});

test("Guide Blade navigation: reading players + stream status for an existing session", async () => {
  const http = new NetBoxHttp({
    fetchImpl: mockFetchSequence([
      { status: 200, body: JSON.stringify([{ id: 9, session: 1, controller_slot: 0, connection_state: "Connected" }]) }, // getPlayers
      { status: 200, body: JSON.stringify({ state: "running", connection: "webrtc" }) }, // getStreamInfo
    ]),
  });

  const players = new PlayerClient(http, 1);
  const stream = new StreamClient(http);

  const roster = await players.getPlayers();
  assert.equal(roster.length, 1);

  const streamInfo = await stream.getStreamInfo(1);
  assert.equal(streamInfo.state, "running");
});
