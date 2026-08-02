import { NetBoxHttp } from "./http.ts";
import type { StreamInfo } from "./types.ts";

export interface WebRTCConnectOptions {
  /** Signaling WebSocket URL for the CloudMorph/Xenia WebRTC bridge (not
   * part of netbox-api's own REST/WS surface - this is the existing
   * streaming signaling channel the dashboard already knows how to
   * negotiate with, per the project's existing WebRTC client). */
  signalUrl: string;
  iceServers?: RTCIceServer[];
  onTrack?: (stream: MediaStream) => void;
  onStateChange?: (state: RTCIceConnectionState) => void;
}

const DEFAULT_ICE_SERVERS: RTCIceServer[] = [{ urls: "stun:stun.l.google.com:19302" }];

/**
 * Stream status + WebRTC session client. getStreamInfo() reads
 * netbox-api's `GET /sessions/{id}/stream` (`{state, connection}`, per the
 * Phase 9 spec). connectWebRTC() creates the browser-side RTCPeerConnection
 * that the dashboard's stream player view attaches to - this class does
 * NOT implement any WebRTC signaling server; it only creates the client
 * peer connection and exposes the remote MediaStream/connection state via
 * callbacks for the caller (e.g. the Games/Guide blade integration) to
 * place into the existing dashboard UI.
 */
export class StreamClient {
  private readonly http: NetBoxHttp;

  public constructor(http: NetBoxHttp) {
    this.http = http;
  }

  public async getStreamInfo(sessionId: number): Promise<StreamInfo> {
    const info = await this.http.request<StreamInfo>("GET", `/sessions/${sessionId}/stream`);
    if (!info) {
      throw new Error("getStreamInfo: empty response from API gateway");
    }
    return info;
  }

  /**
   * Creates the client-side RTCPeerConnection and negotiates over
   * `options.signalUrl`. Returns the RTCPeerConnection so callers can
   * additionally send input over its data channel (future controller
   * bridge work) or tear it down. Requires a global RTCPeerConnection/
   * WebSocket (browser environment) - not available/tested under plain
   * Node, by design, since this is a browser-only concern.
   */
  public connectWebRTC(options: WebRTCConnectOptions): RTCPeerConnection {
    const pc = new RTCPeerConnection({ iceServers: options.iceServers ?? DEFAULT_ICE_SERVERS });
    const remoteStream = new MediaStream();

    pc.ontrack = (event) => {
      remoteStream.addTrack(event.track);
      options.onTrack?.(remoteStream);
    };
    pc.oniceconnectionstatechange = () => {
      options.onStateChange?.(pc.iceConnectionState);
    };

    const ws = new WebSocket(options.signalUrl);

    pc.onicecandidate = (event) => {
      if (event.candidate && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: "candidate", data: btoa(JSON.stringify(event.candidate.toJSON())) }));
      }
    };

    ws.onmessage = async (message: MessageEvent<string>) => {
      const parsed = JSON.parse(message.data) as { type: string; data: string };
      if (parsed.type === "offer") {
        const offer = JSON.parse(atob(parsed.data)) as RTCSessionDescriptionInit;
        await pc.setRemoteDescription(offer);
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        ws.send(JSON.stringify({ type: "answer", data: btoa(JSON.stringify(answer)) }));
      } else if (parsed.type === "candidate" && parsed.data) {
        const candidate = JSON.parse(atob(parsed.data)) as RTCIceCandidateInit;
        await pc.addIceCandidate(candidate);
      }
    };

    return pc;
  }
}
