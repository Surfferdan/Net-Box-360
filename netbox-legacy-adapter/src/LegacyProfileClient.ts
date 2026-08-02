import { LegacyHttp } from "./LegacyHttp.ts";
import type { LegacyProfileResponse } from "./legacy-types.ts";

/**
 * Compatibility replacement for the "Profile services" leg of the Phase
 * 12 architecture. Pure passthrough to the existing, already-working
 * `GET /api/profile/me` endpoint - no new profile storage, no
 * duplication of NetBox.Core's AccountService/profile customization
 * logic.
 */
export class LegacyProfileClient {
  private readonly http: LegacyHttp;

  public constructor(http: LegacyHttp) {
    this.http = http;
  }

  public async getCurrentProfile(): Promise<LegacyProfileResponse> {
    return this.http.requestAuthed<LegacyProfileResponse>("GET", "/api/profile/me");
  }
}
