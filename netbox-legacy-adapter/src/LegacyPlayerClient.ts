import { LegacyHttp } from "./LegacyHttp.ts";
import type { LegacyJoinSessionResponse, LegacyLeaveSessionResponse } from "./legacy-types.ts";
import type { AdaptedPlayer } from "./adapted-types.ts";
import { LegacySessionClient } from "./LegacySessionClient.ts";

/**
 * Compatibility replacement for netbox-client-sdk's PlayerClient, backed
 * by the existing `/api/session/{id}/join` and `/api/session/{id}/leave`
 * routes.
 *
 * Semantic difference from PlayerClient (unavoidable, documented): the
 * legacy backend assigns the next free controller slot server-side - it
 * does not accept a requested slot number, so `join()` ignores any
 * requested slot and returns whatever slot the server actually assigned.
 * There is also no per-player list endpoint, so `getPlayers()` synthesizes
 * a flat array from the session status's `occupiedControllerSlots` (the
 * closest existing equivalent), not real per-player connection records.
 */
export class LegacyPlayerClient {
  private readonly http: LegacyHttp;
  private readonly sessionId: string;
  private readonly sessions: LegacySessionClient;

  public constructor(http: LegacyHttp, sessionId: string) {
    this.http = http;
    this.sessionId = sessionId;
    this.sessions = new LegacySessionClient(http);
  }

  public async join(): Promise<AdaptedPlayer> {
    const response = await this.http.requestAuthed<LegacyJoinSessionResponse>(
      "POST",
      `/api/session/${encodeURIComponent(this.sessionId)}/join`,
    );
    return { controllerSlot: response.assignedControllerSlot };
  }

  public async leave(): Promise<LegacyLeaveSessionResponse> {
    return this.http.requestAuthed<LegacyLeaveSessionResponse>(
      "POST",
      `/api/session/${encodeURIComponent(this.sessionId)}/leave`,
    );
  }

  /** Synthesized from GameSessionStatusResponse.occupiedControllerSlots -
   * there is no dedicated "list players" endpoint on the legacy backend. */
  public async getPlayers(): Promise<AdaptedPlayer[]> {
    const session = await this.sessions.getSession(this.sessionId);
    if (!session) {
      return [];
    }
    return session.occupiedControllerSlots.map((slot) => ({ controllerSlot: slot }));
  }
}
