import { LegacyHttp } from "./LegacyHttp.ts";
import type { LegacyLoginResponse, LegacyCreateAccountResponse } from "./legacy-types.ts";

/**
 * Compatibility replacement covering the "/api/account/*" leg of the
 * Phase 12 architecture. Deliberately thin - it does not reimplement
 * authentication, it only calls the existing, already-working
 * AccountController endpoints and hands the resulting token back to the
 * caller (which is expected to persist it via the same
 * getToken/setToken hooks passed into LegacyHttp, matching the existing
 * NetBoxClient.ts localStorage-backed token store used elsewhere in
 * web-port).
 */
export class LegacyAccountClient {
  private readonly http: LegacyHttp;

  public constructor(http: LegacyHttp) {
    this.http = http;
  }

  public async login(username: string, password: string): Promise<LegacyLoginResponse> {
    return this.http.requestPublic<LegacyLoginResponse>("POST", "/api/login", { username, password });
  }

  public async createAccount(
    username: string,
    password: string,
    displayName: string,
  ): Promise<LegacyCreateAccountResponse> {
    return this.http.requestPublic<LegacyCreateAccountResponse>("POST", "/api/account/create", {
      username,
      password,
      displayName,
    });
  }

  public async logout(): Promise<void> {
    await this.http.requestAuthed<{ success: boolean }>("POST", "/api/logout");
  }
}
