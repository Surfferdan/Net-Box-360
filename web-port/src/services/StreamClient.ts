export type StreamState = "connecting" | "connected" | "failed" | "closed";

export interface StreamClientCallbacks {
  onState?: (state: StreamState) => void;
  onTrack?: (stream: MediaStream) => void;
  onDataChannelOpen?: () => void;
  /**
   * Temporary diagnostic hook for the Radmin VPN ICE-gathering investigation:
   * reports ICE gathering state changes and local-candidate counts so this
   * can be surfaced on-screen without needing remote devtools on a phone.
   */
  onIceDebug?: (text: string) => void;
}

interface SignalMessage {
  type: string;
  data: string;
}

// STUN alone cannot traverse symmetric NAT or VPN tunnels that block
// arbitrary UDP (e.g. Radmin VPN, some mobile carrier NATs). Include TURN
// relay fallbacks, including a TCP/443 transport for networks that only
// allow outbound TCP, matching the server's ICE server list.
const ICE_SERVERS: RTCIceServer[] = [
  { urls: "stun:stun.l.google.com:19302" },
  { urls: "turn:openrelay.metered.ca:80?transport=tcp", username: "openrelayproject", credential: "openrelayproject" },
  { urls: "turn:openrelay.metered.ca:80", username: "openrelayproject", credential: "openrelayproject" },
  { urls: "turn:openrelay.metered.ca:443", username: "openrelayproject", credential: "openrelayproject" },
  { urls: "turn:openrelay.metered.ca:443?transport=tcp", username: "openrelayproject", credential: "openrelayproject" },
];

function encodeBase64Json(value: unknown): string {
  return btoa(JSON.stringify(value));
}

function decodeBase64Json<T>(value: string): T {
  return JSON.parse(atob(value)) as T;
}

/**
 * Native WebRTC client for the CloudMorph/Xenia bridge signaling protocol.
 *
 * Wire format matches pkg/core/go/cloudapp/webrtc: the server always creates
 * the offer, all SDP/ICE payloads are base64(JSON(...)) inside a
 * `{ type, data }` envelope sent over a plain WebSocket. Gamepad/keyboard/
 * mouse input is forwarded as raw (non-base64) JSON over the "app-input"
 * WebRTC data channel that the server opens.
 */
export class StreamClient {
  private ws: WebSocket | null = null;
  private pc: RTCPeerConnection | null = null;
  private dataChannel: RTCDataChannel | null = null;
  private readonly remoteStream = new MediaStream();
  private closed = false;
  private localCandidateCount = 0;

  public constructor(private readonly signalUrl: string, private readonly callbacks: StreamClientCallbacks = {}) {}

  public connect(): void {
    this.closed = false;
    this.callbacks.onState?.("connecting");

    const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
    this.pc = pc;

    pc.ontrack = (event) => {
      this.remoteStream.addTrack(event.track);
      this.callbacks.onTrack?.(this.remoteStream);
    };

    pc.ondatachannel = (event) => {
      this.dataChannel = event.channel;
      this.dataChannel.onopen = () => this.callbacks.onDataChannelOpen?.();
    };

    pc.oniceconnectionstatechange = () => {
      const state = pc.iceConnectionState;
      this.callbacks.onIceDebug?.(`ICE conn: ${state} (candidates sent: ${this.localCandidateCount})`);
      if (state === "connected" || state === "completed") {
        this.callbacks.onState?.("connected");
      } else if (state === "failed" || state === "disconnected" || state === "closed") {
        if (!this.closed) {
          this.callbacks.onState?.("failed");
        }
      }
    };

    pc.onicegatheringstatechange = () => {
      this.callbacks.onIceDebug?.(`ICE gathering: ${pc.iceGatheringState} (candidates sent: ${this.localCandidateCount})`);
    };

    pc.onicecandidate = (event) => {
      if (event.candidate) {
        this.localCandidateCount += 1;
        this.callbacks.onIceDebug?.(
          `Local candidate #${this.localCandidateCount}: typ=${event.candidate.type ?? "?"} proto=${event.candidate.protocol ?? "?"} addr=${event.candidate.address ?? "?"}`,
        );
        this.sendSignal({ type: "candidate", data: encodeBase64Json(event.candidate.toJSON()) });
      } else {
        this.callbacks.onIceDebug?.(`Local candidate gathering complete (total sent: ${this.localCandidateCount})`);
      }
    };

    const ws = new WebSocket(this.signalUrl);
    this.ws = ws;

    ws.onerror = () => {
      if (!this.closed) {
        this.callbacks.onState?.("failed");
      }
    };
    ws.onclose = () => {
      if (!this.closed) {
        this.callbacks.onState?.("failed");
      }
    };
    ws.onmessage = (event) => {
      void this.onSignalMessage(event);
    };
  }

  private async onSignalMessage(event: MessageEvent<string>): Promise<void> {
    if (!this.pc) {
      return;
    }

    let message: SignalMessage;
    try {
      message = JSON.parse(event.data) as SignalMessage;
    } catch {
      return;
    }

    try {
      if (message.type === "offer") {
        const offer = decodeBase64Json<RTCSessionDescriptionInit>(message.data);
        await this.pc.setRemoteDescription(offer);
        const answer = await this.pc.createAnswer();
        await this.pc.setLocalDescription(answer);
        this.sendSignal({ type: "answer", data: encodeBase64Json(answer) });
      } else if (message.type === "candidate" && message.data) {
        const candidate = decodeBase64Json<RTCIceCandidateInit>(message.data);
        await this.pc.addIceCandidate(candidate);
      }
    } catch (error) {
      console.warn("[StreamClient] failed to handle signal message", message.type, error);
    }
  }

  private sendSignal(message: SignalMessage): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(message));
    }
  }

  /** Sends a raw (non-base64) JSON control message over the input data channel. */
  public sendControl(message: unknown): void {
    if (this.dataChannel && this.dataChannel.readyState === "open") {
      this.dataChannel.send(JSON.stringify(message));
    }
  }

  public close(): void {
    if (this.closed) {
      return;
    }
    this.closed = true;

    this.dataChannel?.close();
    this.dataChannel = null;

    this.pc?.close();
    this.pc = null;

    this.ws?.close();
    this.ws = null;

    this.callbacks.onState?.("closed");
  }
}
