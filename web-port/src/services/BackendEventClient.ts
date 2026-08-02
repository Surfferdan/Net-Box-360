import { getNetBoxApiBaseUrl, getSessionToken } from "./NetBoxClient";

export interface BackendEventDto {
  type: string;
  timestamp: string;
  data: Record<string, string>;
}

export type BackendEventHandler = (event: BackendEventDto) => void;

const RECONNECT_DELAY_MS = 3000;

function resolveEventsWsUrl(): string {
  const base = getNetBoxApiBaseUrl();
  if (base) {
    return `${base.replace(/^http/, "ws")}/ws/events`;
  }

  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}/ws/events`;
}

/**
 * Thin client for the backend's `/ws/events` WebSocket event bus
 * (`IBackendEventSink`/`BackendEventHub`). Auto-reconnects while connected
 * and fans out every received event to all subscribed handlers.
 */
export class BackendEventClient {
  private socket: WebSocket | null = null;
  private reconnectHandle: number | null = null;
  private shouldStayConnected = false;
  private readonly handlers = new Set<BackendEventHandler>();

  public subscribe(handler: BackendEventHandler): () => void {
    this.handlers.add(handler);
    return () => this.handlers.delete(handler);
  }

  public connect(): void {
    if (this.shouldStayConnected) {
      return;
    }

    this.shouldStayConnected = true;
    this.openSocket();
  }

  public disconnect(): void {
    this.shouldStayConnected = false;

    if (this.reconnectHandle !== null) {
      window.clearTimeout(this.reconnectHandle);
      this.reconnectHandle = null;
    }

    this.socket?.close();
    this.socket = null;
  }

  private openSocket(): void {
    if (!this.shouldStayConnected || typeof window === "undefined") {
      return;
    }

    const token = getSessionToken();
    const url = token ? `${resolveEventsWsUrl()}?token=${encodeURIComponent(token)}` : resolveEventsWsUrl();

    try {
      this.socket = new WebSocket(url);
    } catch {
      this.scheduleReconnect();
      return;
    }

    this.socket.onmessage = (message) => {
      try {
        const evt = JSON.parse(message.data as string) as BackendEventDto;
        for (const handler of this.handlers) {
          handler(evt);
        }
      } catch {
        // Ignore malformed events; the bus is best-effort telemetry, not a control channel.
      }
    };

    this.socket.onclose = () => {
      this.socket = null;
      this.scheduleReconnect();
    };

    this.socket.onerror = () => {
      this.socket?.close();
    };
  }

  private scheduleReconnect(): void {
    if (!this.shouldStayConnected || this.reconnectHandle !== null) {
      return;
    }

    this.reconnectHandle = window.setTimeout(() => {
      this.reconnectHandle = null;
      this.openSocket();
    }, RECONNECT_DELAY_MS);
  }
}
