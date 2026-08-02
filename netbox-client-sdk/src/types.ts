// Shape mirrors netbox-api's ApiGateway JSON responses exactly (see
// netbox-api/src/api_gateway.cc SessionToJson/PlayerToJson) and
// netbox-server's SessionState/PlayerConnectionState string enums.

export type SessionState =
  | "Created"
  | "Starting"
  | "Running"
  | "Stopping"
  | "Stopped"
  | "Failed";

export interface Session {
  id: number;
  runtime: number;
  stream: number;
  state: SessionState;
  players: number[];
}

export type PlayerConnectionState = "Connected" | "Disconnected";

export interface Player {
  id: number;
  session: number;
  controller_slot: number;
  connection_state: PlayerConnectionState;
}

export type StreamState = "running" | "stopped" | "failed" | "degraded";

export interface StreamInfo {
  state: StreamState;
  connection: string;
}

// The single event vocabulary forwarded by netbox-api's WebSocketGateway
// (mirrors netbox_server::NetBoxEventType exactly - see
// netbox-server/include/netbox_server/events.h). The SDK does not invent a
// second event model; it only deserializes and re-dispatches these.
export type NetBoxEventType =
  | "RuntimeStarted"
  | "RuntimeStopped"
  | "RuntimeFailed"
  | "PlayerJoined"
  | "PlayerLeft"
  | "StreamHealthy"
  | "StreamFailed";

export interface NetBoxEvent {
  type: NetBoxEventType;
  session: number;
  player?: number;
}
