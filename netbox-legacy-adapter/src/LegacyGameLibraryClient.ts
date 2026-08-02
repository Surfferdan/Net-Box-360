import { LegacyHttp } from "./LegacyHttp.ts";
import type { LegacyGameCatalogItemDto } from "./legacy-types.ts";

/**
 * Games Blade data source - passthrough to the existing, already-working
 * `GET /api/games` endpoint (XeniaManager.Api's GamesController), which
 * itself already reads from the real Xenia game library/catalog. No new
 * catalog, no duplicated storage.
 */
export class LegacyGameLibraryClient {
  private readonly http: LegacyHttp;

  public constructor(http: LegacyHttp) {
    this.http = http;
  }

  public async listGames(): Promise<LegacyGameCatalogItemDto[]> {
    const games = await this.http.requestAuthed<LegacyGameCatalogItemDto[]>("GET", "/api/games");
    return games ?? [];
  }
}
