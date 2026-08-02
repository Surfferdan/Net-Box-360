import { LegacyHttp, type LegacyHttpOptions } from "./LegacyHttp.ts";
import { LegacySessionClient } from "./LegacySessionClient.ts";
import { LegacyPlayerClient } from "./LegacyPlayerClient.ts";
import { LegacyStreamClient } from "./LegacyStreamClient.ts";
import { LegacyProfileClient } from "./LegacyProfileClient.ts";
import { LegacyGameLibraryClient } from "./LegacyGameLibraryClient.ts";
import { LegacyAccountClient } from "./LegacyAccountClient.ts";

export * from "./legacy-types.ts";
export * from "./adapted-types.ts";
export { LegacyHttp, LegacyHttpError, type LegacyHttpOptions } from "./LegacyHttp.ts";
export { LegacySessionClient } from "./LegacySessionClient.ts";
export { LegacyPlayerClient } from "./LegacyPlayerClient.ts";
export { LegacyStreamClient } from "./LegacyStreamClient.ts";
export { LegacyProfileClient } from "./LegacyProfileClient.ts";
export { LegacyGameLibraryClient } from "./LegacyGameLibraryClient.ts";
export { LegacyAccountClient } from "./LegacyAccountClient.ts";

export interface LegacyNetBoxAdapterOptions {
  http: LegacyHttpOptions;
}

/**
 * Compatibility facade for Generation 2 code (netbox-client-sdk-shaped
 * dashboard integration layers, e.g. web-port's netbox-bridge.ts) to talk
 * to the EXISTING, already-working Generation 1 backend
 * (XeniaManager.Api + CloudMorph) using the same shape netbox-client-sdk's
 * NetBoxClientSdk facade exposes (`.sessions`, `.stream`,
 * `.playerClientFor(id)`), plus the additional `.profile`/`.games`/
 * `.account` clients this bridge needs that a pure netbox-api Gateway
 * client wouldn't (since the legacy backend also owns accounts/profiles/
 * games, not just sessions).
 *
 * This class implements the "Compatibility Adapter" box in the Phase 12
 * architecture diagram:
 *
 *   netbox-client-sdk -> netbox-api -> Compatibility Adapter -> XeniaManager.Api
 *
 * In practice (documented in the Phase 12 summary): since netbox-api's
 * C++ WebSocket/REST gateway is not running as a live process in this
 * environment, this adapter is consumed directly by the dashboard
 * integration layer in place of a live netbox-api Gateway round-trip -
 * the same method names/shapes are preserved so swapping to a real
 * netbox-api Gateway later (once it's deployed) is a drop-in change.
 */
export class LegacyNetBoxAdapter {
  public readonly sessions: LegacySessionClient;
  public readonly stream: LegacyStreamClient;
  public readonly profile: LegacyProfileClient;
  public readonly games: LegacyGameLibraryClient;
  public readonly account: LegacyAccountClient;

  private readonly http: LegacyHttp;

  public constructor(options: LegacyNetBoxAdapterOptions) {
    this.http = new LegacyHttp(options.http);
    this.sessions = new LegacySessionClient(this.http);
    this.stream = new LegacyStreamClient(this.http);
    this.profile = new LegacyProfileClient(this.http);
    this.games = new LegacyGameLibraryClient(this.http);
    this.account = new LegacyAccountClient(this.http);
  }

  public playerClientFor(sessionId: string): LegacyPlayerClient {
    return new LegacyPlayerClient(this.http, sessionId);
  }
}
