import { NetBoxHttp, type NetBoxHttpOptions } from "./http.ts";
import { SessionClient } from "./SessionClient.ts";
import { PlayerClient } from "./PlayerClient.ts";
import { StreamClient } from "./StreamClient.ts";
import { NetBoxEventClient, type NetBoxEventClientOptions } from "./NetBoxEventClient.ts";

export * from "./types.ts";
export * from "./http.ts";
export { SessionClient } from "./SessionClient.ts";
export { PlayerClient } from "./PlayerClient.ts";
export { StreamClient, type WebRTCConnectOptions } from "./StreamClient.ts";
export { NetBoxEventClient, type NetBoxEventClientOptions } from "./NetBoxEventClient.ts";
export {
  ControllerBridge,
  XBOX_BUTTON_MAP,
  type DetectedController,
  type ControllerBridgeCallbacks,
  type GamepadSource,
} from "./ControllerBridge.ts";

export interface NetBoxClientSdkOptions {
  http: NetBoxHttpOptions;
  events?: Omit<NetBoxEventClientOptions, "wsUrl"> & { wsUrl?: string };
}

/**
 * Convenience facade that wires up SessionClient/StreamClient/
 * NetBoxEventClient against one shared HTTP transport - this is the object
 * the dashboard's integration layer instantiates once and passes to blade
 * adapters. PlayerClient is created per-session via `playerClientFor()`
 * since its routes are scoped to a session id.
 */
export class NetBoxClientSdk {
  public readonly sessions: SessionClient;
  public readonly stream: StreamClient;
  public readonly events: NetBoxEventClient | null;

  private readonly http: NetBoxHttp;

  public constructor(options: NetBoxClientSdkOptions) {
    this.http = new NetBoxHttp(options.http);
    this.sessions = new SessionClient(this.http);
    this.stream = new StreamClient(this.http);
    this.events = options.events?.wsUrl
      ? new NetBoxEventClient({ ...options.events, wsUrl: options.events.wsUrl })
      : null;
  }

  public playerClientFor(sessionId: number): PlayerClient {
    return new PlayerClient(this.http, sessionId);
  }
}
