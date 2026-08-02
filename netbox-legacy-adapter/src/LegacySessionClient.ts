import { LegacyHttp } from "./LegacyHttp.ts";
import type {
  LegacyStartSessionResponse,
  LegacySessionStatusResponse,
  LegacyStopSessionResponse,
} from "./legacy-types.ts";
import { normalizeLegacyState, type AdaptedSession } from "./adapted-types.ts";

function toAdaptedSession(
  status: LegacySessionStatusResponse | LegacyStartSessionResponse,
): AdaptedSession {
  const isStatusShape = "status" in status && "sessionId" in status;
  const raw = isStatusShape ? (status as LegacySessionStatusResponse) : null;
  const started = status as LegacyStartSessionResponse;

  return {
    id: status.sessionId,
    game: status.game,
    state: normalizeLegacyState(raw?.status ?? started.status),
    streamUrl: raw ? raw.streamUrl : started.streamUrl,
    canStopSession: status.canStopSession,
    assignedControllerSlot: status.assignedControllerSlot,
    players: raw?.players ?? 1,
    occupiedControllerSlots: raw?.occupiedControllerSlots ?? [],
    error: raw?.error ?? null,
  };
}

/**
 * Compatibility replacement for netbox-client-sdk's SessionClient, backed
 * by the EXISTING, already-working `/api/session/*` routes on
 * XeniaManager.Api - no new backend endpoints, no rewritten session logic.
 *
 * Key semantic differences from netbox-client-sdk's SessionClient (an
 * unavoidable consequence of bridging two different session models, not a
 * bug):
 *  - Session ids are `string` (legacy GUID-like ids), not `number`.
 *  - There is no separate create-then-start step: `createSession(gameId)`
 *    calls the existing `POST /api/session/start`, which creates AND
 *    starts the real Xenia runtime + CloudMorph stream in one call.
 *    `startSession(id)` is therefore a no-op re-fetch for interface
 *    parity (idempotent, matches SessionClient.startSession's already-
 *    idempotent contract).
 *  - There is no "list all sessions" endpoint - only "my current active
 *    session" (`GET /api/session/active`), since the legacy backend is
 *    single-session-per-user. `listSessions()` reflects that.
 */
export class LegacySessionClient {
  private readonly http: LegacyHttp;

  public constructor(http: LegacyHttp) {
    this.http = http;
  }

  /** Reflects the legacy backend's single-active-session-per-user model:
   * returns at most one session (this user's active one), not a global
   * list. Returns [] if the user has no active session. */
  public async listSessions(): Promise<AdaptedSession[]> {
    const active = await this.http
      .requestAuthed<LegacySessionStatusResponse>("GET", "/api/session/active")
      .catch(() => null);
    return active ? [toAdaptedSession(active)] : [];
  }

  /** Creates AND starts a real Xenia session for `gameId` via the
   * existing `POST /api/session/start` (atomic on the legacy backend). */
  public async createSession(gameId: string): Promise<AdaptedSession> {
    const response = await this.http.requestAuthed<LegacyStartSessionResponse>(
      "POST",
      "/api/session/start",
      { gameId },
    );
    return toAdaptedSession(response);
  }

  public async getSession(id: string): Promise<AdaptedSession | null> {
    const response = await this.http
      .requestAuthed<LegacySessionStatusResponse>("GET", `/api/session/${encodeURIComponent(id)}`)
      .catch(() => null);
    return response ? toAdaptedSession(response) : null;
  }

  /** No-op re-fetch for interface parity - the legacy backend has no
   * separate "start" step after create (see class doc comment above). */
  public async startSession(id: string): Promise<AdaptedSession> {
    const session = await this.getSession(id);
    if (!session) {
      throw new Error(`startSession: session ${id} not found`);
    }
    return session;
  }

  public async stopSession(id: string): Promise<AdaptedSession> {
    await this.http.requestAuthed<LegacyStopSessionResponse>(
      "POST",
      `/api/session/${encodeURIComponent(id)}/stop`,
    );
    const session = await this.getSession(id);
    if (!session) {
      // Session record may already be gone after stop; synthesize a
      // minimal stopped record rather than throwing, matching
      // SessionClient.stopSession's "always returns a Session" contract.
      return {
        id,
        game: "",
        state: "stopped",
        streamUrl: null,
        canStopSession: false,
        assignedControllerSlot: 0,
        players: 0,
        occupiedControllerSlots: [],
        error: null,
      };
    }
    return session;
  }
}
