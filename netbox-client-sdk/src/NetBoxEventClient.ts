import type { NetBoxEvent, NetBoxEventType } from "./types.ts";

export interface NetBoxEventClientOptions {
  /** Full ws:// or wss:// URL to netbox-api's `/ws/events` endpoint. */
  wsUrl: string;
  /** Injectable WebSocket constructor, defaults to the global one (tests
   * supply a mock implementation). */
  webSocketImpl?: typeof WebSocket;
  reconnectDelayMs?: number;
}

type EventHandler = (event: NetBoxEvent) => void;

const DEFAULT_RECONNECT_DELAY_MS = 3000;

/**
 * WebSocket client for netbox-api's `/ws/events` endpoint. Subscribes to
 * the single existing NetBoxEventBus vocabulary
 * (RuntimeStarted/Stopped/Failed, PlayerJoined/Left, StreamHealthy/Failed)
 * and converts each into ergonomic frontend callbacks
 * (onRuntimeStarted/onPlayerJoined/onStreamReady/onRuntimeError/etc.)
 * rather than exposing raw JSON to blade code.
 */
export class NetBoxEventClient {
  private socket: WebSocket | null = null;
  private reconnectHandle: ReturnType<typeof setTimeout> | null = null;
  private shouldStayConnected = false;
  private readonly handlersByType = new Map<NetBoxEventType, Set<EventHandler>>();
  private readonly webSocketImpl: typeof WebSocket;
  private readonly reconnectDelayMs: number;
  private readonly options: NetBoxEventClientOptions;

  public constructor(options: NetBoxEventClientOptions) {
    this.options = options;
    this.webSocketImpl = options.webSocketImpl ?? globalThis.WebSocket;
    this.reconnectDelayMs = options.reconnectDelayMs ?? DEFAULT_RECONNECT_DELAY_MS;
    if (!this.webSocketImpl) {
      throw new Error("No WebSocket implementation available; provide options.webSocketImpl.");
    }
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
      clearTimeout(this.reconnectHandle);
      this.reconnectHandle = null;
    }
    this.socket?.close();
    this.socket = null;
  }

  // -- Generic subscription (used internally by the named helpers below,
  // also usable directly for forward-compatibility with new event types). --
  public on(type: NetBoxEventType, handler: EventHandler): () => void {
    let handlers = this.handlersByType.get(type);
    if (!handlers) {
      handlers = new Set();
      this.handlersByType.set(type, handlers);
    }
    handlers.add(handler);
    return () => handlers?.delete(handler);
  }

  // -- Ergonomic named callbacks, per the Phase 10 spec examples. --
  public onRuntimeStarted(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("RuntimeStarted", callback);
  }
  public onRuntimeStopped(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("RuntimeStopped", callback);
  }
  public onRuntimeError(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("RuntimeFailed", callback);
  }
  public onPlayerJoined(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("PlayerJoined", callback);
  }
  public onPlayerLeft(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("PlayerLeft", callback);
  }
  /** "Stream ready" == StreamHealthy, matching the Phase 10 spec example. */
  public onStreamReady(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("StreamHealthy", callback);
  }
  public onStreamFailed(callback: (event: NetBoxEvent) => void): () => void {
    return this.on("StreamFailed", callback);
  }

  private openSocket(): void {
    if (!this.shouldStayConnected) {
      return;
    }

    let socket: WebSocket;
    try {
      socket = new this.webSocketImpl(this.options.wsUrl);
    } catch {
      this.scheduleReconnect();
      return;
    }
    this.socket = socket;

    socket.onmessage = (message: MessageEvent<string>) => {
      this.dispatchRaw(message.data);
    };
    socket.onclose = () => {
      this.socket = null;
      this.scheduleReconnect();
    };
    socket.onerror = () => {
      socket.close();
    };
  }

  private dispatchRaw(data: string): void {
    let event: NetBoxEvent;
    try {
      event = JSON.parse(data) as NetBoxEvent;
    } catch {
      return; // Ignore malformed frames - best effort, not a control channel.
    }

    const handlers = this.handlersByType.get(event.type);
    if (!handlers) {
      return;
    }
    for (const handler of handlers) {
      handler(event);
    }
  }

  private scheduleReconnect(): void {
    if (!this.shouldStayConnected || this.reconnectHandle !== null) {
      return;
    }
    this.reconnectHandle = setTimeout(() => {
      this.reconnectHandle = null;
      this.openSocket();
    }, this.reconnectDelayMs);
  }
}
