// Adapter-facing shapes, matching netbox-client-sdk's Session/Player/
// StreamInfo public field names as closely as the legacy backend's actual
// data allows, so netbox-bridge.ts (and any other netbox-client-sdk
// consumer) needs minimal changes to compile against this package instead.
// Session ids are `string` here (matching the legacy backend's GUID-like
// session ids), NOT `number` like netbox-client-sdk's Session.id - this is
// documented as an intentional, unavoidable compatibility difference.

export type LegacySessionState =
  | "pending"
  | "launching"
  | "running"
  | "stopping"
  | "stopped"
  | "failed"
  | "unknown";

export interface AdaptedSession {
  id: string;
  game: string;
  state: LegacySessionState;
  streamUrl: string | null;
  canStopSession: boolean;
  assignedControllerSlot: number;
  players: number;
  occupiedControllerSlots: number[];
  error: string | null;
}

export interface AdaptedPlayer {
  controllerSlot: number;
}

export type AdaptedStreamState = "running" | "stopped" | "failed" | "degraded";

export interface AdaptedStreamInfo {
  state: AdaptedStreamState;
  connection: string | null;
}

export function normalizeLegacyState(status: string): LegacySessionState {
  const normalized = status.trim().toLowerCase();
  switch (normalized) {
    case "pending":
    case "launching":
    case "running":
    case "stopping":
    case "stopped":
    case "failed":
      return normalized;
    default:
      return "unknown";
  }
}

export function normalizeStreamHealth(streamHealth: string): AdaptedStreamState {
  const normalized = streamHealth.trim().toLowerCase();
  if (normalized === "live" || normalized === "game" || normalized === "ready" || normalized === "connected") {
    return "running";
  }
  if (normalized === "capture-timeout" || normalized === "failed" || normalized === "error") {
    return "failed";
  }
  if (normalized === "stopped" || normalized === "offline") {
    return "stopped";
  }
  return "degraded";
}
