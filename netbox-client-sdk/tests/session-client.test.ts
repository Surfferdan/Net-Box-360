import { test } from "node:test";
import assert from "node:assert/strict";
import { NetBoxHttp } from "../src/http.ts";
import { SessionClient } from "../src/SessionClient.ts";

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

test("SessionClient.createSession posts to /sessions and returns the session", async () => {
  const http = new NetBoxHttp({
    fetchImpl: mockFetch([{ status: 200, body: JSON.stringify({ id: 1, runtime: 1, stream: 1, state: "Created", players: [] }) }]),
  });
  const client = new SessionClient(http);

  const session = await client.createSession();
  assert.equal(session.id, 1);
  assert.equal(session.state, "Created");
});

test("SessionClient.listSessions returns an empty array when there are none", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 200, body: "[]" }]) });
  const client = new SessionClient(http);

  const sessions = await client.listSessions();
  assert.deepEqual(sessions, []);
});

test("SessionClient.startSession returns Running state", async () => {
  const http = new NetBoxHttp({
    fetchImpl: mockFetch([{ status: 200, body: JSON.stringify({ id: 1, runtime: 1, stream: 1, state: "Running", players: [] }) }]),
  });
  const client = new SessionClient(http);

  const session = await client.startSession(1);
  assert.equal(session.state, "Running");
});

test("SessionClient.startSession throws NetBoxHttpError on failure status", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 409, body: '{"error":"session_failed_to_start"}' }]) });
  const client = new SessionClient(http);

  await assert.rejects(() => client.startSession(1));
});

test("SessionClient.destroySession issues a DELETE and does not throw on empty body", async () => {
  const http = new NetBoxHttp({ fetchImpl: mockFetch([{ status: 204, body: "" }]) });
  const client = new SessionClient(http);

  await assert.doesNotReject(() => client.destroySession(1));
});
