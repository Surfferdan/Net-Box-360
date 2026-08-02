import { NetBoxHttp } from "./http.ts";
import type { Player } from "./types.ts";

/**
 * Player/controller-slot client - thin wrapper over netbox-api's
 * `/sessions/{id}/players` routes.
 */
export class PlayerClient {
  private readonly http: NetBoxHttp;
  private readonly sessionId: number;

  public constructor(http: NetBoxHttp, sessionId: number) {
    this.http = http;
    this.sessionId = sessionId;
  }

  /** Joins `controllerSlot` (defaults to the first free slot, 0, if omitted -
   * callers building a slot picker UI should pass an explicit slot). */
  public async join(controllerSlot = 0): Promise<Player> {
    const player = await this.http.request<Player>("POST", `/sessions/${this.sessionId}/players`, {
      controller_slot: controllerSlot,
    });
    if (!player) {
      throw new Error("PlayerClient.join: empty response from API gateway");
    }
    return player;
  }

  public async leave(playerId: number): Promise<void> {
    await this.http.request<void>("DELETE", `/sessions/${this.sessionId}/players/${playerId}`);
  }

  public async getPlayers(): Promise<Player[]> {
    const players = await this.http.request<Player[]>("GET", `/sessions/${this.sessionId}/players`);
    return players ?? [];
  }
}
