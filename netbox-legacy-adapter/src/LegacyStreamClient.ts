import { LegacyHttp } from "./LegacyHttp.ts";
import type { LegacySessionStatusResponse } from "./legacy-types.ts";
import { normalizeStreamHealth, type AdaptedStreamInfo } from "./adapted-types.ts";

/**
 * Compatibility replacement for netbox-client-sdk's StreamClient. There is
 * no separate `/stream` endpoint on the legacy backend - stream state and
 * the WebRTC connection URL are both part of the existing session status
 * response, so `getStreamInfo` reads from `GET /api/session/{id}` (the
 * same, already-working endpoint used for session polling) rather than a
 * new route.
 */
export class LegacyStreamClient {
  private readonly http: LegacyHttp;

  public constructor(http: LegacyHttp) {
    this.http = http;
  }

  public async getStreamInfo(sessionId: string): Promise<AdaptedStreamInfo> {
    const status = await this.http.requestAuthed<LegacySessionStatusResponse>(
      "GET",
      `/api/session/${encodeURIComponent(sessionId)}`,
    );
    return {
      state: normalizeStreamHealth(status.streamHealth),
      connection: status.streamUrl,
    };
  }
}
