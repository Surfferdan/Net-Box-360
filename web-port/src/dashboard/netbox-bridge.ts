import { LegacyNetBoxAdapter, type AdaptedSession } from "../../../netbox-legacy-adapter/src/index.ts";
import { ControllerBridge } from "../../../netbox-client-sdk/src/index.ts";
import { BackendEventClient, type BackendEventDto } from "../services/BackendEventClient";
import { getNetBoxApiBaseUrl, getSessionToken, setSessionToken } from "../services/NetBoxClient";

/**
 * Phase 12 rewrite of the Phase 10 dashboard integration layer.
 *
 * Same public method names/blade-facing shape as Phase 10 (blades do not
 * need to change), but now backed by `netbox-legacy-adapter`, which talks
 * to the EXISTING, already-working XeniaManager.Api endpoints
 * (`/api/session/*`, `/api/account/*`, `/api/games`, `/api/profile/*`)
 * instead of the never-deployed netbox-api C++ Gateway used in Phase 10.
 * Real Xenia sessions are launched through the same CloudMorph/
 * XeniaManager.Api path web-port's existing SessionService.ts already
 * uses - this bridge is an additional, netbox-client-sdk-shaped entry
 * point onto that same working backend, not a replacement for it.
 *
 * Event wiring reuses the existing, already-working
 * `web-port/src/services/BackendEventClient.ts` (`/ws/events`, the real
 * live event bus) instead of netbox-client-sdk's NetBoxEventClient (which
 * targets netbox-api's not-yet-deployed WebSocket gateway) - translating
 * the same event-name vocabulary (SessionStarted/SessionStopped/
 * SessionFailed/PlayerJoined/PlayerLeft/StreamHealthy/StreamFailed, which
 * match what Phase 11's XeniaEventBridge maps FROM) into the same
 * onRuntimeError/onStreamReady/onPlayerJoined/onPlayerLeft callback shape
 * blades already expect.
 */
export interface NetBoxDashboardBridgeOptions {
  apiBaseUrl?: string;
}

export interface HomeBladeSummary {
  activeSessionCount: number;
  sessions: AdaptedSession[];
}

export interface GameLaunchResult {
  session: AdaptedSession;
  controllerSlot: number;
}

type NetBoxBladeEventCallback = (event: BackendEventDto) => void;

const RUNTIME_FAILED_TYPES = new Set(["XeniaError", "SessionFailed"]);
const STREAM_READY_TYPES = new Set(["StreamHealthy"]);
const PLAYER_JOINED_TYPES = new Set(["PlayerJoined"]);
const PLAYER_LEFT_TYPES = new Set(["PlayerLeft"]);

export class NetBoxDashboardBridge {
  public readonly legacy: LegacyNetBoxAdapter;
  public readonly controllers: ControllerBridge;
  private readonly events: BackendEventClient;

  public constructor(options: NetBoxDashboardBridgeOptions = {}) {
    this.legacy = new LegacyNetBoxAdapter({
      http: {
        baseUrl: options.apiBaseUrl ?? getNetBoxApiBaseUrl(),
        getToken: () => getSessionToken(),
        setToken: (token: string) => setSessionToken(token),
      },
    });
    this.controllers = new ControllerBridge(
      typeof navigator !== "undefined" ? navigator : { getGamepads: () => [] },
    );
    this.events = new BackendEventClient();
  }

  /** Call once during dashboard bootstrap. */
  public start(): void {
    this.events.connect();
    this.controllers.startPolling();
  }

  public stop(): void {
    this.events.disconnect();
    this.controllers.stopPolling();
  }

  // ---- Home Blade ----
  public async getHomeBladeSummary(): Promise<HomeBladeSummary> {
    const sessions = await this.legacy.sessions.listSessions();
    return {
      activeSessionCount: sessions.filter((s) => s.state === "running").length,
      sessions,
    };
  }

  // ---- Games Blade ----
  // "Play Game -> Create Session -> Start Runtime -> Open Stream". On the
  // legacy backend, create+start are one atomic call
  // (POST /api/session/start), which actually launches the real Xenia
  // process + CloudMorph stream. The legacy backend auto-assigns the
  // controller slot on join (no requested slot).
  public async playGame(gameId: string): Promise<GameLaunchResult> {
    const session = await this.legacy.sessions.createSession(gameId);
    const player = await this.legacy.playerClientFor(session.id).join();
    return { session, controllerSlot: player.controllerSlot };
  }

  // ---- Friends Blade ----
  public async joinFriendSession(sessionId: string) {
    const player = await this.legacy.playerClientFor(sessionId).join();
    const session = await this.legacy.sessions.getSession(sessionId);
    return { session, player };
  }

  // ---- Guide Blade ----
  public async getGuideBladeStatus(sessionId: string) {
    const [session, players, stream] = await Promise.all([
      this.legacy.sessions.getSession(sessionId),
      this.legacy.playerClientFor(sessionId).getPlayers(),
      this.legacy.stream.getStreamInfo(sessionId),
    ]);
    return { session, players, stream };
  }

  // ---- Event wiring (backed by the existing, real /ws/events bus) ----
  public onRuntimeError(callback: NetBoxBladeEventCallback): () => void {
    return this.events.subscribe((event) => {
      if (RUNTIME_FAILED_TYPES.has(event.type)) callback(event);
    });
  }
  public onStreamReady(callback: NetBoxBladeEventCallback): () => void {
    return this.events.subscribe((event) => {
      if (STREAM_READY_TYPES.has(event.type)) callback(event);
    });
  }
  public onPlayerJoined(callback: NetBoxBladeEventCallback): () => void {
    return this.events.subscribe((event) => {
      if (PLAYER_JOINED_TYPES.has(event.type)) callback(event);
    });
  }
  public onPlayerLeft(callback: NetBoxBladeEventCallback): () => void {
    return this.events.subscribe((event) => {
      if (PLAYER_LEFT_TYPES.has(event.type)) callback(event);
    });
  }
}
