import { NetBoxHttpError, getSessionToken, requestJson } from "./NetBoxClient";

const ACTIVE_SESSION_ENDPOINT_STATE_KEY = "netbox.activeSessionEndpointState";

export interface StartSessionResponse {
  sessionId: string;
  game: string;
  streamUrl: string;
  status: string;
  controllerStatus: string;
  canStopSession: boolean;
  assignedControllerSlot: number;
}

export interface SessionStatusResponse {
  sessionId: string;
  status: string;
  game: string;
  players: number;
  canStopSession: boolean;
  streamUrl: string | null;
  cloudMorphSessionId: string | null;
  error: string | null;
  streamHealth: string;
  assignedControllerSlot: number;
  occupiedControllerSlots: number[];
}

export interface LeaveSessionResponse {
  success: boolean;
  status: string;
  sessionId: string;
  playersRemaining: number;
}

export interface JoinSessionResponse {
  sessionId: string;
  game: string;
  streamUrl: string | null;
  controllerStatus: string;
  assignedControllerSlot: number;
}

export interface CloudMorphHealthResponse {
  status: string;
  captureReady: boolean;
  streamReady: boolean;
  activeSessions: number;
}

function authHeaders(): HeadersInit {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  return {
    Authorization: `Bearer ${token}`,
  };
}

export async function startGameSession(gameId: string): Promise<StartSessionResponse> {
  return requestJson<StartSessionResponse>("/api/session/start", {
    method: "POST",
    headers: authHeaders(),
    body: JSON.stringify({ gameId }),
  });
}

export async function getGameSession(sessionId: string): Promise<SessionStatusResponse> {
  return requestJson<SessionStatusResponse>(`/api/session/${encodeURIComponent(sessionId)}`, {
    method: "GET",
    headers: authHeaders(),
  });
}

export async function reconnectActiveSession(): Promise<SessionStatusResponse> {
  if (isActiveSessionEndpointUnsupported()) {
    throw new Error("Active session endpoint unavailable.");
  }

  try {
    const response = await requestJson<SessionStatusResponse | null>("/api/session/active", {
      method: "GET",
      headers: authHeaders(),
    });

    if (!response) {
      setActiveSessionEndpointSupported();
      throw new Error("No active session.");
    }

    setActiveSessionEndpointSupported();
    return response;
  } catch (error) {
    if (isNotFoundError(error)) {
      setActiveSessionEndpointUnsupported();
      throw new Error("Active session endpoint unavailable.");
    }

    throw error;
  }
}

export async function stopGameSession(sessionId: string): Promise<void> {
  await requestJson<{ success: boolean; status: string }>(`/api/session/${encodeURIComponent(sessionId)}/stop`, {
    method: "POST",
    headers: authHeaders(),
  });
}

export async function leaveGameSession(sessionId: string): Promise<LeaveSessionResponse> {
  return requestJson<LeaveSessionResponse>(`/api/session/${encodeURIComponent(sessionId)}/leave`, {
    method: "POST",
    headers: authHeaders(),
  });
}

export async function joinGameSession(sessionId: string): Promise<JoinSessionResponse> {
  return requestJson<JoinSessionResponse>(`/api/session/${encodeURIComponent(sessionId)}/join`, {
    method: "POST",
    headers: authHeaders(),
  });
}

export async function getCloudMorphStatus(): Promise<CloudMorphHealthResponse> {
  return requestJson<CloudMorphHealthResponse>("/api/cloudmorph/status", {
    method: "GET",
    headers: authHeaders(),
  });
}

function isNotFoundError(error: unknown): boolean {
  if (error instanceof NetBoxHttpError) {
    return error.status === 404;
  }

  if (error instanceof Error) {
    return /status\s*404/i.test(error.message);
  }

  return false;
}

function getActiveSessionEndpointState(): "supported" | "unsupported" | null {
  if (typeof window === "undefined") {
    return null;
  }

  const value = window.localStorage.getItem(ACTIVE_SESSION_ENDPOINT_STATE_KEY);
  return value === "supported" || value === "unsupported" ? value : null;
}

function setActiveSessionEndpointSupported(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(ACTIVE_SESSION_ENDPOINT_STATE_KEY, "supported");
}

function setActiveSessionEndpointUnsupported(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(ACTIVE_SESSION_ENDPOINT_STATE_KEY, "unsupported");
}

function isActiveSessionEndpointUnsupported(): boolean {
  return getActiveSessionEndpointState() === "unsupported";
}
