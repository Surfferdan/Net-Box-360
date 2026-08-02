import { StreamClient, type StreamState } from "../../services/StreamClient";
import { GameControllerInput, type ConnectedGamepadInfo } from "../../engine/input-manager/GameControllerInput";
import { GamepadInputClient, type GamepadStatus } from "../../services/GamepadInputClient";

export interface CloudMorphHealthState {
  status: string;
  captureReady: boolean;
  streamReady: boolean;
  activeSessions: number;
}

type StreamPanelState = "idle" | "launching" | "connecting" | "live" | "unavailable";
type StreamFitMode = "contain" | "cover" | "fill";

const WS_URL_PATTERN = /^wss?:\/\//i;
const LOCAL_MUTE_KEY_PREFIX = "netbox.session.localMute.";
const STREAM_FIT_KEY = "netbox.stream.fitMode";
export class GameDetailsOverlay {
  private readonly root: HTMLDivElement;
  private readonly title: HTMLHeadingElement;
  private readonly status: HTMLParagraphElement;
  private readonly stageWrap: HTMLDivElement;
  private readonly video: HTMLVideoElement;
  private readonly overlayBadge: HTMLDivElement;
  private readonly fullscreenButton: HTMLButtonElement;
  private readonly muteButton: HTMLButtonElement;
  private readonly fitButton: HTMLButtonElement;
  private readonly couchButton: HTMLButtonElement;
  private readonly couchPanel: HTMLDivElement;
  private readonly stageHost: HTMLElement;
  private readonly stageAnchor: Comment;

  private client: StreamClient | null = null;
  private controllerInput: GameControllerInput | null = null;
  private gamepadInputClient: GamepadInputClient | null = null;
  private readonly gamepadStatusPanel: HTMLDivElement;
  private readonly iceDebugPanel: HTMLDivElement;
  private state: StreamPanelState = "idle";
  private isExpanded = true;
  private inputEnabled = true;
  private currentSessionId: string | null = null;
  private activePlayerSlot = 1;
  private occupiedControllerSlots: number[] = [];
  private couchPanelOpen = false;
  private localAudioMuted = false;
  private fitMode: StreamFitMode = "contain";

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "details-overlay";
    this.root.hidden = true;

    const tabs = document.createElement("div");
    tabs.className = "details-tabs";
    tabs.innerHTML = "<span class='is-active'>overview</span><span>details</span><span>extras</span><span>gallery</span>";

    this.title = document.createElement("h2");
    this.title.textContent = "Game Details";

    this.status = document.createElement("p");
    this.status.className = "details-status";
    this.status.textContent = "No active stream.";

    // The stream area is always rendered (video + status badge) so it is
    // never left blank: the badge shows the current state text whenever the
    // video is not actively playing.
    this.stageWrap = document.createElement("div");
    this.stageWrap.className = "details-stream-stage";
    this.stageAnchor = document.createComment("details-stream-stage-anchor");
    this.stageHost = parent;

    this.video = document.createElement("video");
    this.video.className = "details-stream-video";
    this.video.autoplay = true;
    this.video.playsInline = true;
    this.video.hidden = true;

    this.overlayBadge = document.createElement("div");
    this.overlayBadge.className = "details-stream-badge";
    this.overlayBadge.dataset.playerSlot = "1";

    this.fullscreenButton = document.createElement("button");
    this.fullscreenButton.type = "button";
    this.fullscreenButton.className = "details-stream-fullscreen";
    this.fullscreenButton.hidden = true;
    this.fullscreenButton.addEventListener("click", () => this.toggleExpanded());

    this.muteButton = document.createElement("button");
    this.muteButton.type = "button";
    this.muteButton.className = "details-stream-mute";
    this.muteButton.hidden = true;
    this.muteButton.addEventListener("click", () => this.toggleLocalMute());

    this.fitButton = document.createElement("button");
    this.fitButton.type = "button";
    this.fitButton.className = "details-stream-fit";
    this.fitButton.hidden = true;
    this.fitButton.addEventListener("click", () => this.toggleFitMode());

    this.couchButton = document.createElement("button");
    this.couchButton.type = "button";
    this.couchButton.className = "details-stream-couch";
    this.couchButton.textContent = "Local Co-op";
    this.couchButton.hidden = true;
    this.couchButton.addEventListener("click", () => this.toggleCouchPanel());

    this.couchPanel = document.createElement("div");
    this.couchPanel.className = "details-couch-panel";
    this.couchPanel.hidden = true;

    this.gamepadStatusPanel = document.createElement("div");
    this.gamepadStatusPanel.className = "details-gamepad-status";
    this.gamepadStatusPanel.hidden = true;

    // Temporary diagnostic panel for the Radmin VPN ICE-gathering
    // investigation: shows ICE gathering/connection state and local
    // candidate info directly on-screen since remote devtools aren't
    // practical on a phone. Safe to remove once Radmin is resolved.
    this.iceDebugPanel = document.createElement("div");
    this.iceDebugPanel.className = "details-ice-debug";
    this.iceDebugPanel.hidden = true;
    this.iceDebugPanel.style.cssText =
      "position:absolute;left:4px;bottom:4px;max-width:90%;max-height:40%;overflow-y:auto;background:rgba(0,0,0,0.75);color:#0f0;font:11px monospace;padding:4px 6px;white-space:pre-wrap;z-index:50;pointer-events:none;";

    this.stageWrap.addEventListener("dblclick", () => this.toggleExpanded());

    this.stageWrap.append(
      this.video,
      this.overlayBadge,
      this.fullscreenButton,
      this.muteButton,
      this.fitButton,
      this.couchButton,
      this.couchPanel,
      this.gamepadStatusPanel,
      this.iceDebugPanel,
    );
    this.root.append(tabs, this.title, this.status, this.stageWrap);
    parent.appendChild(this.root);

    this.fitMode = this.readFitMode();
    this.applyFitMode();
  }

  /**
   * Toggles the stream stage between its default full-viewport presentation
   * and a smaller windowed view. This is implemented as a plain CSS class
   * (rather than the browser's native Fullscreen API) so it can be applied
   * automatically the instant a stream goes live (no user gesture required)
   * and so it stacks as a normal z-index layer.
   */
  private toggleExpanded(): void {
    this.setExpanded(!this.isExpanded);
  }

  private setExpanded(value: boolean): void {
    this.isExpanded = value;
    this.stageWrap.classList.toggle("is-expanded", value);
    this.fullscreenButton.textContent = value ? "Exit Fullscreen" : "Fullscreen";

    if (value && this.stageWrap.parentElement !== document.body) {
      this.root.insertBefore(this.stageAnchor, this.stageWrap);
      document.body.appendChild(this.stageWrap);
      return;
    }

    if (!value && this.stageWrap.parentElement === document.body) {
      this.stageAnchor.replaceWith(this.stageWrap);
    }
  }

  /** Opens/closes the overlay for non-streaming detail views (no session). */
  public show(value: boolean, title = "Game Details"): void {
    this.root.hidden = !value;
    this.title.textContent = title;
    this.teardownStream();
    this.setState("idle", "No active stream.");
  }

  /** Opens the overlay immediately in a "launching" state, before the session API call resolves. */
  public showLaunching(title: string): void {
    this.root.hidden = false;
    this.title.textContent = title;
    this.teardownStream();
    this.setState("launching", "Launching Xenia...");
  }

  /**
   * Connects (or shows the offline fallback for) the stream once a session
   * response is available.
   */
  public connectStream(signalUrl: string | null, cloudMorphHealth: CloudMorphHealthState | null = null, sessionId: string | null = null): void {
    this.teardownStream();

    this.currentSessionId = sessionId;
    this.syncSlotBadge();
    this.localAudioMuted = this.readLocalMute(sessionId);
    this.applyLocalMuteToVideo();
    this.updateMuteButtonLabel();

    if (!signalUrl || !WS_URL_PATTERN.test(signalUrl)) {
      this.setState("unavailable", this.describeUnavailable(cloudMorphHealth));
      return;
    }

    this.setState("connecting", "Connecting to stream...");

    this.iceDebugPanel.hidden = false;
    this.iceDebugPanel.textContent = "";

    this.client = new StreamClient(this.resolveSignalUrl(signalUrl), {
      onState: (streamState) => this.handleStreamState(streamState),
      onIceDebug: (text) => this.appendIceDebug(text),
      onTrack: (stream) => {
        this.video.srcObject = stream;
        this.video.hidden = false;
        this.applyLocalMuteToVideo();
        void this.video.play().catch(() => {
          // Autoplay can be blocked without a user gesture; the badge stays
          // visible with the current status so the area is never blank.
        });
      },
      onDataChannelOpen: () => {
        // NOTE: GameControllerInput (the legacy WebRTC data-channel gamepad
        // bridge) is intentionally NOT started here anymore. Its server-side
        // counterpart in xenia_bridge.go composites gamepad state into a
        // synthesized *keyboard* input stream, which causes controller
        // presses to literally type characters into whatever has focus on
        // the PC. GamepadInputClient (native /ws/input -> XInput bridge,
        // started in handleStreamState on "connected") is now the sole
        // controller input path.
        this.renderCouchPanel();
      },
    });
    this.client.connect();
  }

  public close(): void {
    this.root.hidden = true;
    this.teardownStream();
    this.setState("idle", "No active stream.");
  }

  public setControllerInputEnabled(value: boolean): void {
    this.inputEnabled = value;
    this.controllerInput?.setEnabled(value);
  }

  public setAssignedPlayerSlot(slot: number): void {
    this.activePlayerSlot = this.normalizePlayerSlot(slot);
    this.syncSlotBadge();
    this.controllerInput?.stop();
    this.controllerInput = null;
    this.renderCouchPanel();
  }

  /** Updates which player slots are already claimed (by the owner or network guests) so local couch bindings can avoid colliding with them. */
  public setOccupiedControllerSlots(slots: number[]): void {
    this.occupiedControllerSlots = slots;
    this.renderCouchPanel();
  }

  private handleStreamState(streamState: StreamState): void {
    if (streamState === "connected") {
      this.setState("live", "Stream live.");
      this.startGamepadInputClient();
    } else if ((streamState === "failed" || streamState === "closed") && this.state !== "idle") {
      this.setState("unavailable", "Stream connection lost.");
      this.stopGamepadInputClient();
    }
  }

  /** Temporary diagnostic for the Radmin VPN ICE-gathering investigation. Safe to remove once resolved. */
  private appendIceDebug(text: string): void {
    const line = document.createElement("div");
    line.textContent = `${new Date().toLocaleTimeString()} ${text}`;
    this.iceDebugPanel.appendChild(line);
    this.iceDebugPanel.scrollTop = this.iceDebugPanel.scrollHeight;
  }

  /** Starts the native XInput bridge (/ws/input) for the current session, independent of the WebRTC data channel. */
  private startGamepadInputClient(): void {
    this.stopGamepadInputClient();
    if (!this.currentSessionId) {
      return;
    }

    this.gamepadStatusPanel.hidden = false;
    this.gamepadInputClient = new GamepadInputClient(this.currentSessionId);
    this.gamepadInputClient.setStatusListener((status) => this.renderGamepadStatus(status));
    this.gamepadInputClient.start();
  }

  private stopGamepadInputClient(): void {
    this.gamepadInputClient?.stop();
    this.gamepadInputClient = null;
    this.gamepadStatusPanel.hidden = true;
    this.gamepadStatusPanel.replaceChildren();
  }

  /** Renders the Controller Connected/Player Assigned/Name/Number/Connection Quality panel. Battery level is not exposed by the standard Gamepad API - shown as "N/A". */
  private renderGamepadStatus(status: GamepadStatus): void {
    this.gamepadStatusPanel.replaceChildren();

    const rows: Array<[string, string]> = [
      ["Controller", status.connected ? "Connected" : "Not connected"],
      ["Player", `Slot ${this.activePlayerSlot}`],
      ["Name", status.name ?? "—"],
      ["Number", status.index !== null ? String(status.index + 1) : "—"],
      ["Battery", "N/A"],
      ["Quality", status.quality],
    ];

    for (const [label, value] of rows) {
      const row = document.createElement("div");
      row.className = "details-gamepad-status-row";
      row.innerHTML = `<span class="details-gamepad-status-label">${label}</span><span class="details-gamepad-status-value">${value}</span>`;
      this.gamepadStatusPanel.appendChild(row);
    }
  }

  /**
   * The API resolves the CloudMorph signaling URL using its own loopback
   * address (e.g. "ws://127.0.0.1:8080/..."), since it has no reliable way
   * to know which network interface the browser used to reach it. That's
   * correct for the machine running the API itself, but "127.0.0.1" refers
   * to the *client* device when interpreted by a remote browser (e.g. a
   * phone on the LAN), which makes it try to connect to itself and fail
   * immediately. Rewrite the loopback/localhost host to whatever hostname
   * the page itself was loaded from, so remote clients target the actual
   * server instead of themselves. The port from the API response (the
   * CloudMorph media-plane port, not Vite's) is preserved.
   */
  private resolveSignalUrl(signalUrl: string): string {
    try {
      const url = new URL(signalUrl);
      const isLoopback = url.hostname === "127.0.0.1" || url.hostname === "localhost" || url.hostname === "[::1]";
      const pageHostname = window.location.hostname;
      if (isLoopback && pageHostname && pageHostname !== "127.0.0.1" && pageHostname !== "localhost") {
        url.hostname = pageHostname;
      }
      return url.toString();
    } catch {
      return signalUrl;
    }
  }

  private describeUnavailable(cloudMorphHealth: CloudMorphHealthState | null): string {
    if (cloudMorphHealth) {
      const captureText = cloudMorphHealth.captureReady ? "capture ready" : "capture not ready";
      const streamText = cloudMorphHealth.streamReady ? "stream ready" : "stream not ready";
      return `Cloud stream unavailable. CloudMorph ${cloudMorphHealth.status}: ${captureText}, ${streamText}, active sessions ${cloudMorphHealth.activeSessions}.`;
    }
    return "Cloud stream is currently unavailable. The session is active, but no stream endpoint could be reached.";
  }

  private setState(state: StreamPanelState, message: string): void {
    this.state = state;
    this.status.textContent = message;
    this.overlayBadge.textContent = state === "live"
      ? `Player slot ${this.activePlayerSlot}`
      : message;
    this.overlayBadge.hidden = state === "live";
    this.overlayBadge.dataset.state = state;
    this.fullscreenButton.hidden = state !== "live";
    this.muteButton.hidden = state !== "live";
    this.fitButton.hidden = state !== "live";
    this.couchButton.hidden = state !== "live";
    if (state !== "live") {
      this.couchPanelOpen = false;
      this.couchPanel.hidden = true;
    }
    this.updateMuteButtonLabel();
    this.updateFitButtonLabel();

    if (state !== "idle") {
      this.setExpanded(true);
    }

    if (state !== "live") {
      this.video.hidden = true;
    }
  }

  private toggleLocalMute(): void {
    this.localAudioMuted = !this.localAudioMuted;
    this.applyLocalMuteToVideo();
    this.persistLocalMute(this.currentSessionId, this.localAudioMuted);
    this.updateMuteButtonLabel();
  }

  private toggleFitMode(): void {
    this.fitMode = this.fitMode === "contain"
      ? "cover"
      : this.fitMode === "cover"
        ? "fill"
        : "contain";
    this.applyFitMode();
    this.persistFitMode(this.fitMode);
  }

  private applyFitMode(): void {
    this.stageWrap.dataset.fit = this.fitMode;
    this.updateFitButtonLabel();
  }

  private updateFitButtonLabel(): void {
    const label = this.fitMode === "contain"
      ? "Fit: Contain"
      : this.fitMode === "cover"
        ? "Fit: Cover"
        : "Fit: Stretch";
    this.fitButton.textContent = label;
  }

  private readFitMode(): StreamFitMode {
    if (typeof window === "undefined") {
      return "contain";
    }

    const value = window.localStorage.getItem(STREAM_FIT_KEY);
    if (value === "cover" || value === "fill" || value === "contain") {
      return value;
    }

    return "contain";
  }

  private persistFitMode(mode: StreamFitMode): void {
    if (typeof window === "undefined") {
      return;
    }

    window.localStorage.setItem(STREAM_FIT_KEY, mode);
  }

  private applyLocalMuteToVideo(): void {
    this.video.muted = this.localAudioMuted;
    this.video.volume = this.localAudioMuted ? 0 : 1;
  }

  private updateMuteButtonLabel(): void {
    this.muteButton.textContent = this.localAudioMuted ? "Unmute Local" : "Mute Local";
    this.muteButton.setAttribute("aria-pressed", this.localAudioMuted ? "true" : "false");
  }

  private readLocalMute(sessionId: string | null): boolean {
    if (!sessionId || typeof window === "undefined") {
      return false;
    }

    const value = window.localStorage.getItem(`${LOCAL_MUTE_KEY_PREFIX}${sessionId}`);
    return value === "1";
  }

  private persistLocalMute(sessionId: string | null, muted: boolean): void {
    if (!sessionId || typeof window === "undefined") {
      return;
    }

    window.localStorage.setItem(`${LOCAL_MUTE_KEY_PREFIX}${sessionId}`, muted ? "1" : "0");
  }

  private normalizePlayerSlot(slot: number): number {
    if (!Number.isFinite(slot)) {
      return 1;
    }

    return Math.min(4, Math.max(1, Math.trunc(slot)));
  }

  private syncSlotBadge(): void {
    this.overlayBadge.dataset.playerSlot = String(this.activePlayerSlot);
    this.overlayBadge.title = `Assigned slot ${this.activePlayerSlot}`;
    if (this.state !== "live") {
      this.overlayBadge.textContent = this.status.textContent;
    } else {
      this.overlayBadge.textContent = `Player slot ${this.activePlayerSlot}`;
    }
  }

  private teardownStream(): void {
    this.controllerInput?.stop();
    this.controllerInput = null;
    this.stopGamepadInputClient();
    this.client?.close();
    this.client = null;
    this.video.srcObject = null;
    this.video.hidden = true;
    this.muteButton.hidden = true;
    this.fitButton.hidden = true;
    this.couchButton.hidden = true;
    this.couchPanelOpen = false;
    this.couchPanel.hidden = true;
    this.setExpanded(false);
  }

  private toggleCouchPanel(): void {
    this.couchPanelOpen = !this.couchPanelOpen;
    this.renderCouchPanel();
  }

  /**
   * Renders the local couch co-op controller binding panel: every gamepad
   * currently visible to the browser, with buttons to assign it to an
   * additional player slot (2-4). The primary controller (auto-bound to the
   * network-assigned slot) is shown read-only. Slots already claimed by the
   * session owner or a network guest (per setOccupiedControllerSlots) are
   * disabled to avoid colliding with a remote participant's input.
   */
  private renderCouchPanel(): void {
    this.couchPanel.hidden = !this.couchPanelOpen || this.state !== "live";
    this.couchPanel.replaceChildren();

    if (!this.couchPanelOpen || this.state !== "live") {
      return;
    }

    const gamepads: ConnectedGamepadInfo[] = this.controllerInput?.getConnectedGamepads() ?? [];

    const heading = document.createElement("div");
    heading.className = "details-couch-panel-heading";
    heading.textContent = "Local Controllers";
    this.couchPanel.appendChild(heading);

    if (gamepads.length === 0) {
      const empty = document.createElement("div");
      empty.className = "details-couch-panel-empty";
      empty.textContent = "No controllers detected. Connect a gamepad and press a button.";
      this.couchPanel.appendChild(empty);
      return;
    }

    for (const gamepad of gamepads) {
      const row = document.createElement("div");
      row.className = "details-couch-panel-row";

      const label = document.createElement("span");
      label.className = "details-couch-panel-label";
      label.textContent = gamepad.id.length > 28 ? `${gamepad.id.slice(0, 28)}...` : gamepad.id;
      row.appendChild(label);

      if (gamepad.slot === this.activePlayerSlot) {
        const primaryBadge = document.createElement("span");
        primaryBadge.className = "details-couch-slot-badge";
        primaryBadge.textContent = "Primary (You)";
        row.appendChild(primaryBadge);
        this.couchPanel.appendChild(row);
        continue;
      }

      for (const slot of [1, 2, 3, 4]) {
        if (slot === this.activePlayerSlot) {
          continue;
        }

        const button = document.createElement("button");
        button.type = "button";
        button.className = "details-couch-slot-btn";
        button.textContent = `P${slot}`;

        const isBoundHere = gamepad.slot === slot;
        const isClaimedRemotely = this.occupiedControllerSlots.includes(slot) && !isBoundHere;

        button.classList.toggle("is-active", isBoundHere);
        button.disabled = isClaimedRemotely;
        button.title = isClaimedRemotely
          ? `Player slot ${slot} is already in use.`
          : isBoundHere
            ? `Unbind from player slot ${slot}`
            : `Assign to player slot ${slot}`;

        button.addEventListener("click", () => {
          if (isBoundHere) {
            this.controllerInput?.unbindGamepad(gamepad.index);
          } else {
            this.controllerInput?.bindGamepad(gamepad.index, slot);
          }
        });

        row.appendChild(button);
      }

      this.couchPanel.appendChild(row);
    }
  }
}
