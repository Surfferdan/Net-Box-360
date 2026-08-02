import { AnimationManager } from "../engine/animation-manager/animation-manager";
import { InputManager, type DashboardInputAction, type DashboardInputSource } from "../engine/input-manager/input-manager";
import { buildBladeLayer, type TileElement } from "./blades/blade-renderer";
import { BLADES, TAB_POSITIONS } from "./blades/blade-data";
import { GuideOverlay } from "./guide/guide-overlay";
import { LibraryOverlay } from "./library/library-overlay";
import { GameDetailsOverlay } from "./menus/game-details-overlay";
import { FriendsPanel, type SocialPanelMode } from "./friends/friends-panel";
import { ProfilePanel } from "./profile/profile-panel";
import { ProfileEditorOverlay, type ProfileEditorSavePayload } from "./profile/profile-editor-overlay";
import type { ActivityItem } from "../services/types";
import { createAccount, login, logout, type CreateAccountRequest, type LoginRequest } from "../services/AuthService";
import { getProfile, type NetBoxProfile, updateProfileCustomization } from "../services/ProfileService";
import { WebAudioService } from "../services/audio-service";
import { clearSessionToken, getSessionToken } from "../services/NetBoxClient";
import { AuthOverlay } from "./auth/auth-overlay";
import { getLaunchableGames } from "../services/XeniaLibraryService";
import { addFriendByUsername, getChatMessages, getSocialFeed, removeFriend, sendChatMessage } from "../services/SocialService";
import type { ChatMessage, Game } from "../services/types";
import { getCloudMorphStatus, getGameSession, reconnectActiveSession, startGameSession, stopGameSession } from "../services/SessionService";

interface BladeRuntime {
  root: HTMLDivElement;
  tiles: TileElement[];
}

export class DashboardApp {
  private readonly root: HTMLElement;
  private readonly animations: AnimationManager;
  private readonly input: InputManager;
  private readonly bladeTrack: HTMLDivElement;
  private readonly tabButtons = new Map<string, HTMLButtonElement>();
  private readonly runtimes = new Map<string, BladeRuntime>();

  private readonly guide: GuideOverlay;
  private readonly library: LibraryOverlay;
  private readonly details: GameDetailsOverlay;
  private readonly friends: FriendsPanel;
  private readonly profilePanel: ProfilePanel;
  private readonly profileEditor: ProfileEditorOverlay;
  private readonly authOverlay: AuthOverlay;
  private readonly audio: WebAudioService;
  private activityBanner: HTMLDivElement | null = null;
  private activityFeed: ActivityItem[] = [];

  private currentBladeIndex = 1;
  private serverProfile: NetBoxProfile | null = null;
  private currentProfile: NetBoxProfile | null = null;
  private selectedTile = new Map<string, string>();
  private isAuthed = false;
  private inGuide = false;
  private inLibrary = false;
  private inDetails = false;
  private inFriends = false;
  private currentSocialMode: SocialPanelMode = "friends";
  private inProfileEditor = false;
  private socialChatMessages: ChatMessage[] = [];
  private socialChatPollHandle: number | null = null;
  private activeSessionId: string | null = null;
  private activeStreamUrl: string | null = null;
  private activeCloudMorphHealth: { status: string; captureReady: boolean; streamReady: boolean; activeSessions: number } | null = null;
  private activeSessionPollHandle: number | null = null;

  public constructor(root: HTMLElement, animations: AnimationManager, input: InputManager) {
    this.root = root;
    this.animations = animations;
    this.input = input;
    this.audio = new WebAudioService();

    this.root.className = "dash-shell";

    const stage = document.createElement("div");
    stage.className = "dash-stage";

    const tabBar = document.createElement("div");
    tabBar.className = "dash-tabbar";
    TAB_POSITIONS.forEach((tab) => {
      const button = document.createElement("button");
      button.className = "dash-tab";
      button.textContent = tab.key;
      button.style.left = `${tab.left}px`;
      button.style.width = `${tab.width}px`;
      button.addEventListener("click", () => this.switchToBlade(BLADES.findIndex((b) => b.key === tab.key)));
      tabBar.appendChild(button);
      this.tabButtons.set(tab.key, button);
    });

    const content = document.createElement("div");
    content.className = "dash-content";

    this.bladeTrack = document.createElement("div");
    this.bladeTrack.className = "blade-track";

    for (const blade of BLADES) {
      const built = buildBladeLayer(blade);
      this.bladeTrack.appendChild(built.root);
      this.runtimes.set(blade.key, { root: built.root, tiles: built.tileElements });

      built.tileElements.forEach((tile) => {
        tile.element.addEventListener("click", () => this.activateTile(tile.tile.id));
      });

      if (built.tileElements[0]) {
        this.selectedTile.set(blade.key, built.tileElements[0].tile.id);
      }
    }

    const leftPreview = document.createElement("div");
    leftPreview.className = "adjacent-preview left";
    const rightPreview = document.createElement("div");
    rightPreview.className = "adjacent-preview right";

    content.append(this.bladeTrack, leftPreview, rightPreview);

    const footer = document.createElement("div");
    footer.className = "dash-footer";
    footer.innerHTML = "<span class='hint a'>A</span> Select <span class='hint b'>B</span> Back <span class='hint x'>X</span> Details <span class='hint y'>Y</span> Search";

    stage.append(tabBar, content, footer);

    this.profilePanel = new ProfilePanel(stage);
    this.profilePanel.onOpenAction(() => {
      this.openProfileEditor();
    });
    this.profileEditor = new ProfileEditorOverlay(stage);
    this.profileEditor.onSaveAction((payload) => {
      void this.handleProfileSave(payload);
    });
    this.profileEditor.onCancelAction(() => {
      this.closeProfileEditor();
    });
    this.authOverlay = new AuthOverlay(stage);
    this.authOverlay.onSubmitAction((payload, mode) => {
      void this.handleAuthSubmit(payload, mode);
    });
    this.guide = new GuideOverlay(stage);
    this.library = new LibraryOverlay(stage);
    this.library.onSelectionChangedAction(() => {
      void this.audio.play("focus", 0.45);
    });
    this.details = new GameDetailsOverlay(stage);
    this.friends = new FriendsPanel(stage);
    this.friends.setAddFriendAction(async (username) => {
      await this.handleAddFriend(username);
    });
    this.friends.setRemoveFriendAction(async (friendUserId) => {
      await this.handleRemoveFriend(friendUserId);
    });
    this.friends.setChatSendAction(async (message, recipientUserId) => {
      await this.handleChatSend(message, recipientUserId);
    });
    this.updateControllerInputSuppression();

    this.root.appendChild(stage);

    this.updateTabState();
    this.updateTileFocus();
    this.updateBladeCarousel(-this.currentBladeIndex * 1280);
    this.input.onAction((event) => {
      void this.handleInput(event.action, event.source);
    });

    void this.audio.preload("startup");
    void this.bootstrapData();
  }

  private async bootstrapData(): Promise<void> {
    await this.refreshSocialFeed();

    this.ensureActivityBanner();
    this.renderActivityBanner();

    await this.refreshProfile();
  }

  private async refreshProfile(): Promise<void> {
    const token = getSessionToken();
    if (!token) {
      this.isAuthed = false;
      this.serverProfile = null;
      this.currentProfile = null;
      this.guide.setProfileAvatar(null);
      this.profilePanel.setVisible(false);
      await this.refreshSocialFeed();
      await this.refreshGameLibrary();
      this.authOverlay.setMessage("Sign in or create an account to continue.");
      this.authOverlay.setVisible(true);
      this.closeProfileEditor();
      return;
    }

    try {
      this.serverProfile = await getProfile();
      this.currentProfile = this.serverProfile;
      this.guide.setProfileAvatar(this.resolveGuideAvatar(this.currentProfile));
      this.profilePanel.setVisible(true);
      this.profilePanel.update(this.currentProfile);
      await this.refreshSocialFeed();
      await this.resumeActiveSessionIfPresent();
      await this.refreshGameLibrary();
      this.authOverlay.setVisible(false);
      this.isAuthed = true;
    } catch {
      this.isAuthed = false;
      this.serverProfile = null;
      this.currentProfile = null;
      this.guide.setProfileAvatar(null);
      this.profilePanel.setVisible(false);
      await this.refreshSocialFeed();
      await this.refreshGameLibrary();
      this.authOverlay.setMessage("Session expired. Sign in again.");
      this.authOverlay.setVisible(true);
      this.closeProfileEditor();
    }
  }

  private async refreshSocialFeed(): Promise<void> {
    try {
      const social = await getSocialFeed();
      this.friends.setFeed(social.friends, social.activity);
      this.activityFeed = social.activity;
      this.guide.setBadgeCounts({
        friends: social.friends.length,
      });
    } catch {
      this.friends.setFeed([], []);
      this.activityFeed = [{ id: "social-unavailable", text: "Social feed unavailable." }];
      this.guide.setBadgeCounts({ friends: 0 });
    }
  }

  private async refreshChatMessages(): Promise<void> {
    try {
      this.socialChatMessages = await getChatMessages(80);
      this.friends.setChatMessages(this.socialChatMessages);
      this.guide.setBadgeCounts({
        messages: this.socialChatMessages.filter((item) => !item.isMine).length,
      });
    } catch {
      this.socialChatMessages = [];
      this.friends.setChatMessages([]);
      this.guide.setBadgeCounts({ messages: 0 });
    }
  }

  private beginSocialChatPolling(): void {
    this.stopSocialChatPolling();
    this.socialChatPollHandle = window.setInterval(() => {
      if (this.inFriends && this.currentSocialMode === "chat") {
        void this.refreshChatMessages();
      }
    }, 4000);
  }

  private stopSocialChatPolling(): void {
    if (this.socialChatPollHandle === null) {
      return;
    }

    window.clearInterval(this.socialChatPollHandle);
    this.socialChatPollHandle = null;
  }

  private async handleChatSend(message: string, recipientUserId?: string | null): Promise<void> {
    await sendChatMessage(message, recipientUserId ?? null);
    await this.refreshChatMessages();
  }

  private async handleAddFriend(username: string): Promise<void> {
    await addFriendByUsername(username);
    await this.refreshSocialFeed();
  }

  private async handleRemoveFriend(friendUserId: string): Promise<void> {
    await removeFriend(friendUserId);
    await this.refreshSocialFeed();
  }

  private async refreshGameLibrary(): Promise<void> {
    try {
      const games = await getLaunchableGames(this.currentProfile);
      this.library.setGames(games);
      this.updateGamesBladeContent(games);

      if (games.length === 0 && this.activityFeed.length === 0) {
        this.activityFeed = [{ id: "no-games", text: "No launchable games found in your configured Xenia games folder." }];
      }

      this.renderActivityBanner();
    } catch {
      this.library.setGames([]);
      this.updateGamesBladeContent([]);

      if (this.activityFeed.length === 0) {
        this.activityFeed = [{ id: "game-catalog-error", text: "Could not load Xenia game catalog." }];
      }

      this.renderActivityBanner();
    }
  }

  private updateGamesBladeContent(games: Game[]): void {
    const gamesBlade = this.runtimes.get("games");
    if (!gamesBlade) {
      return;
    }

    const featured = games[0] ?? null;
    const secondary = games[1] ?? null;
    const tertiary = games[2] ?? null;

    this.populateGameTile(gamesBlade, "games-forza", featured, "Forza Horizon", "December IGN Pack");
    this.populateGameTile(gamesBlade, "games-minecraft", secondary, "Minecraft", "Latest installed title");
    this.populateGameTile(gamesBlade, "games-blackops", tertiary, "Black Ops II", "Launch ready");
  }

  private populateGameTile(gamesBlade: BladeRuntime, tileId: string, game: Game | null, fallbackTitle: string, fallbackSubtitle: string): void {
    const tile = gamesBlade.tiles.find((entry) => entry.tile.id === tileId);
    if (!tile) {
      return;
    }

    const label = tile.element.querySelector(".dash-tile-label") as HTMLElement | null;
    const subtitle = tile.element.querySelector(".dash-tile-subtitle") as HTMLElement | null;
    const title = game?.title ?? fallbackTitle;
    const body = game?.subtitle ?? fallbackSubtitle;

    if (label) {
      label.textContent = title;
    }

    if (subtitle) {
      subtitle.textContent = body;
    }

    const fallbackCover = tile.tile.image ?? "";
    const cover = game?.coverPath;

    const applyBackground = (path: string | null, dark = false): void => {
      tile.element.style.backgroundColor = dark ? "#0d1112" : tile.tile.background ?? "#3b8465";
      tile.element.style.backgroundImage = path
        ? `linear-gradient(to top, rgba(0,0,0,0.72), rgba(0,0,0,0.12)), url('${path}')`
        : "";
      tile.element.style.backgroundSize = "cover";
      tile.element.style.backgroundPosition = "center";
    };

    if (!cover) {
      applyBackground(fallbackCover || null, false);
      return;
    }

    const probe = new Image();
    probe.onload = () => {
      applyBackground(cover, true);
    };
    probe.onerror = () => {
      applyBackground(fallbackCover || null, false);
    };
    probe.src = cover;
  }

  private ensureActivityBanner(): void {
    if (this.activityBanner) {
      return;
    }

    this.activityBanner = document.createElement("div");
    this.activityBanner.className = "activity-banner";
    this.root.querySelector(".dash-stage")?.appendChild(this.activityBanner);
  }

  private renderActivityBanner(): void {
    if (!this.activityBanner) {
      return;
    }

    const first = this.activityFeed[0];
    this.activityBanner.textContent = first?.text ?? "Welcome to dashX360.";
  }

  private async handleAuthSubmit(payload: LoginRequest | CreateAccountRequest, mode: "login" | "create"): Promise<void> {
    try {
      if (mode === "create") {
        const createPayload: CreateAccountRequest = {
          username: payload.username,
          password: payload.password,
          displayName: "displayName" in payload ? payload.displayName : payload.username,
          email: "email" in payload ? payload.email : undefined,
        };
        const created = await createAccount(createPayload);
        await login({ username: payload.username, password: payload.password });
        this.authOverlay.setMessage(`Account created for ${created.profile.displayName}. Loading profile...`);
      } else {
        await login(payload);
      }

      await this.refreshProfile();
    } catch (error) {
      this.isAuthed = false;
      this.authOverlay.setMessage(error instanceof Error ? error.message : "Authentication failed.");
      this.authOverlay.setVisible(true);
    }
  }

  private async handleInput(action: DashboardInputAction, _source: DashboardInputSource): Promise<void> {
    if (action === "Guide") {
      this.inGuide = !this.inGuide;
      this.guide.show(this.inGuide);
      void this.audio.play(this.inGuide ? "guide-open" : "guide-close", 0.7);
      this.updateControllerInputSuppression();
      return;
    }

    if (this.inGuide) {
      if (action === "MoveUp") {
        this.guide.move(-1);
        void this.audio.play("guide-hover", 0.6);
      }
      if (action === "MoveDown") {
        this.guide.move(1);
        void this.audio.play("guide-hover", 0.6);
      }
      if (action === "Activate") {
        void this.audio.play("guide-select", 0.7);
        if (this.guide.selectedItem === "Xbox Home") {
          await this.closeActiveGameAndGoHome();
        } else if (this.guide.selectedItem === "Friends") {
          this.inGuide = false;
          this.guide.show(false);
          this.openSocialPanel("friends");
        } else if (this.guide.selectedItem === "Party") {
          this.inGuide = false;
          this.guide.show(false);
          this.openSocialPanel("party");
        } else if (this.guide.selectedItem === "Messages") {
          this.inGuide = false;
          this.guide.show(false);
          this.openSocialPanel("messages");
        } else if (this.guide.selectedItem === "Beacons & Activity") {
          this.inGuide = false;
          this.guide.show(false);
          this.openSocialPanel("activity");
        } else if (this.guide.selectedItem === "Chat") {
          this.inGuide = false;
          this.guide.show(false);
          this.openSocialPanel("chat");
        } else if (this.guide.selectedItem === "Manage Storage") {
          this.inGuide = false;
          this.guide.show(false);
          const settingsIndex = BLADES.findIndex((blade) => blade.key === "settings");
          if (settingsIndex >= 0) {
            this.switchToBlade(settingsIndex);
          }
        }
      }
      if (action === "Back") {
        this.inGuide = false;
        this.guide.show(false);
        void this.audio.play("guide-back", 0.7);
        this.updateControllerInputSuppression();
      }
      return;
    }

    if (this.inFriends) {
      if (action === "Back") {
        this.closeSocialPanel();
      }
      return;
    }

    if (this.inProfileEditor) {
      if (action === "Back") {
        this.closeProfileEditor();
      }
      return;
    }

    if (this.inLibrary) {
      if (action === "MoveLeft") this.library.move(-1);
      if (action === "MoveRight") this.library.move(1);
      if (action === "Details") {
        const selected = this.library.selectedGame;
        if (selected) {
          this.inLibrary = false;
          this.library.show(false);
          this.inDetails = true;
          this.details.show(true, selected.title);
          this.updateControllerInputSuppression();
        }
      }
      if (action === "Activate") {
        const selected = this.library.selectedGame;
        if (selected) {
          void this.audio.play("select", 0.7);
          this.inLibrary = false;
          this.library.show(false);
          this.inDetails = true;
          this.details.showLaunching(selected.title);

          try {
            const session = await startGameSession(selected.id);
            this.activeSessionId = session.sessionId;
            this.activeStreamUrl = session.streamUrl && session.streamUrl.trim().length > 0
              ? session.streamUrl
              : null;
            try {
              this.activeCloudMorphHealth = await getCloudMorphStatus();
            } catch {
              this.activeCloudMorphHealth = null;
            }

            this.details.showLaunching(this.activeStreamUrl ? `Streaming ${selected.title}` : `Session started for ${selected.title}`);
            this.details.connectStream(this.activeStreamUrl, this.activeCloudMorphHealth, this.activeSessionId);
            this.beginActiveSessionPolling();
          } catch (error) {
            this.activeSessionId = null;
            this.activeStreamUrl = null;
            try {
              this.activeCloudMorphHealth = await getCloudMorphStatus();
            } catch {
              this.activeCloudMorphHealth = null;
            }
            this.details.showLaunching(selected.title);
            this.details.connectStream(null, this.activeCloudMorphHealth, null);
            console.warn("[dashboard] failed to start game session", error);
          }
          this.updateControllerInputSuppression();
        }
      }
      if (action === "Back") {
        this.inLibrary = false;
        this.library.show(false);
        this.updateControllerInputSuppression();
      }
      return;
    }

    if (this.inDetails) {
      if (!this.activeSessionId && action === "Back") {
        this.inDetails = false;
        this.details.close();
        this.updateControllerInputSuppression();
      }

      // While a game is streaming, every controller/keyboard input other than
      // Guide (handled above, unconditionally) is meant for the game itself -
      // GameControllerInput forwards it directly over the data channel. The
      // dashboard must not intercept Back/B (or anything else) here, since
      // stopping the game via a plain gameplay button makes no sense; the
      // only way to close the game from here is the Guide's "Xbox Home" item.
      return;
    }

    switch (action) {
      case "PreviousTab":
        this.switchToBlade(Math.max(0, this.currentBladeIndex - 1));
        void this.audio.play("page-left", 0.6);
        break;
      case "NextTab":
        this.switchToBlade(Math.min(BLADES.length - 1, this.currentBladeIndex + 1));
        void this.audio.play("page-right", 0.6);
        break;
      case "MoveLeft":
      case "MoveRight":
      case "MoveUp":
      case "MoveDown":
        this.moveTileFocus(action);
        break;
      case "Activate":
        this.activateCurrentTile();
        void this.audio.play("activate", 0.7);
        break;
      case "Details":
        this.openDetails();
        break;
      case "Search":
        this.switchToBlade(0);
        void this.audio.play("guide-select", 0.7);
        break;
      case "Back":
        void this.audio.play("back", 0.7);
        break;
      default:
        break;
    }
  }

  /**
   * Stops any active game session and returns to the Home blade. This is the
    * only way to exit a running game now (via the Guide's "Xbox Home" item) -
   * gameplay buttons (including B/Back) are left alone to control the game.
   */
  private async closeActiveGameAndGoHome(): Promise<void> {
    if (this.activeSessionId) {
      try {
        await stopGameSession(this.activeSessionId);
      } catch {
        // Ignore stop failures during stream close.
      }
      this.activeSessionId = null;
      this.activeStreamUrl = null;
      this.stopActiveSessionPolling();
    }

    this.inDetails = false;
    this.details.close();
    this.inLibrary = false;
    this.library.show(false);
    this.inGuide = false;
    this.guide.show(false);
    this.updateControllerInputSuppression();

    const homeIndex = BLADES.findIndex((blade) => blade.key === "home");
    this.switchToBlade(homeIndex >= 0 ? homeIndex : 0);
  }

  private switchToBlade(index: number): void {
    if (index === this.currentBladeIndex || index < 0 || index >= BLADES.length) {
      return;
    }

    const from = this.currentBladeIndex;
    this.currentBladeIndex = index;
    const startX = -from * 1280;
    const endX = -index * 1280;

    this.animations.tween({
      from: startX,
      to: endX,
      durationMs: 420,
      update: (x) => {
        this.bladeTrack.style.transform = `translateX(${x}px)`;
        this.updateBladeCarousel(x);
      },
    });

    void this.audio.play("guide-blade-switch-1", 0.6);
    this.updateTabState();
    this.updateTileFocus();
  }

  private updateBladeCarousel(trackX: number): void {
    const virtualIndex = -trackX / 1280;

    BLADES.forEach((blade, index) => {
      const runtime = this.runtimes.get(blade.key);
      if (!runtime) {
        return;
      }

      const distance = index - virtualIndex;
      const absDistance = Math.abs(distance);
      const scale = Math.max(0.74, 1 - absDistance * 0.16);

      // Pull neighboring blades toward center so they are visible at the sides.
      const pull = Math.sign(distance) * -Math.min(absDistance, 1.85) * 360;
      runtime.root.style.transform = `translateX(${pull}px) scale(${scale})`;
      runtime.root.style.transformOrigin = "center center";

      // Fade blades with distance to reinforce carousel depth.
      runtime.root.style.opacity = `${Math.max(0.26, 1 - absDistance * 0.48)}`;
      runtime.root.style.zIndex = `${Math.max(1, 100 - Math.round(absDistance * 10))}`;
    });
  }

  private updateTabState(): void {
    const key = BLADES[this.currentBladeIndex].key;
    this.tabButtons.forEach((button, k) => {
      button.classList.toggle("is-active", k === key);
    });
  }

  private moveTileFocus(action: DashboardInputAction): void {
    const blade = BLADES[this.currentBladeIndex];
    const runtime = this.runtimes.get(blade.key);
    if (!runtime) {
      return;
    }

    const currentId = this.selectedTile.get(blade.key);
    const current = runtime.tiles.find((t) => t.tile.id === currentId) ?? runtime.tiles[0];
    if (!current) {
      return;
    }

    let next: TileElement | undefined;
    let bestScore = Number.POSITIVE_INFINITY;

    for (const candidate of runtime.tiles) {
      if (candidate.tile.id === current.tile.id) {
        continue;
      }

      const dx = candidate.centerX - current.centerX;
      const dy = candidate.centerY - current.centerY;

      const valid =
        (action === "MoveLeft" && dx < 0) ||
        (action === "MoveRight" && dx > 0) ||
        (action === "MoveUp" && dy < 0) ||
        (action === "MoveDown" && dy > 0);

      if (!valid) {
        continue;
      }

      const primary = action === "MoveLeft" || action === "MoveRight" ? Math.abs(dx) : Math.abs(dy);
      const secondary = action === "MoveLeft" || action === "MoveRight" ? Math.abs(dy) : Math.abs(dx);
      const score = primary * 1 + secondary * 0.55;

      if (score < bestScore) {
        bestScore = score;
        next = candidate;
      }
    }

    if (next) {
      this.selectedTile.set(blade.key, next.tile.id);
      this.updateTileFocus();
      void this.audio.play("focus", 0.45);
    }
  }

  private updateTileFocus(): void {
    const activeBlade = BLADES[this.currentBladeIndex].key;
    this.runtimes.forEach((runtime, key) => {
      const selected = this.selectedTile.get(key);
      runtime.tiles.forEach((tile) => {
        const isFocused = key === activeBlade && tile.tile.id === selected;
        tile.element.classList.toggle("is-focused", isFocused);
      });
    });
  }

  private activateCurrentTile(): void {
    const blade = BLADES[this.currentBladeIndex];
    const tileId = this.selectedTile.get(blade.key);
    if (!tileId) {
      return;
    }
    this.activateTile(tileId);
  }

  private activateTile(tileId: string): void {
    if (tileId === "games-my-games" || tileId === "apps-myapps") {
      this.inLibrary = true;
      this.library.show(true);
      this.updateControllerInputSuppression();
      return;
    }

    if (tileId === "social-friends") {
      this.openSocialPanel("friends");
      return;
    }

    if (tileId === "social-themes") {
      this.openSocialPanel("activity");
      return;
    }

    if (tileId === "social-signin") {
      if (this.isAuthed) {
        void this.handleSignOut();
      } else {
        this.authOverlay.setMessage("Sign in or create an account to continue.");
        this.authOverlay.setVisible(true);
      }
      return;
    }

    if (tileId === "settings-profile") {
      this.openProfileEditor();
      return;
    }

    if (tileId === "bing-search") {
      const q = window.prompt("Search Xbox:", "");
      if (q) {
        console.info("Search placeholder:", q);
      }
      return;
    }

    this.activityFeed = [{ id: `tile-${tileId}`, text: `${tileId.replace(/-/g, " ")} is not wired yet.` }, ...this.activityFeed].slice(0, 6);
    this.renderActivityBanner();
  }

  private openSocialPanel(mode: SocialPanelMode): void {
    this.currentSocialMode = mode;
    if (mode === "chat" || mode === "messages") {
      void this.refreshChatMessages();
      this.beginSocialChatPolling();
    } else {
      this.stopSocialChatPolling();
      void this.refreshSocialFeed();
    }

    this.inFriends = true;
    this.friends.show(true, mode);
    this.updateControllerInputSuppression();
  }

  private closeSocialPanel(): void {
    this.stopSocialChatPolling();
    this.inFriends = false;
    this.friends.show(false, this.currentSocialMode);
    this.updateControllerInputSuppression();
  }

  private openDetails(title?: string): void {
    this.inDetails = true;
    this.details.show(true, title ?? "Game Details");
    this.updateControllerInputSuppression();
  }

  private shouldSuppressGameInput(): boolean {
    return this.inGuide || this.inLibrary || this.inFriends || this.inProfileEditor;
  }

  private updateControllerInputSuppression(): void {
    this.details.setControllerInputEnabled(!this.shouldSuppressGameInput());
  }

  private openProfileEditor(): void {
    if (!this.isAuthed || !this.currentProfile || !this.serverProfile) {
      this.authOverlay.setMessage("Sign in first to edit your profile.");
      this.authOverlay.setVisible(true);
      return;
    }

    this.inProfileEditor = true;
    this.profileEditor.hydrate(this.serverProfile);
    this.profileEditor.show(true);
    this.updateControllerInputSuppression();
  }

  private closeProfileEditor(): void {
    this.inProfileEditor = false;
    this.profileEditor.show(false);
    this.updateControllerInputSuppression();
  }

  private async handleProfileSave(payload: ProfileEditorSavePayload): Promise<void> {
    if (!this.serverProfile) {
      return;
    }

    try {
      const updated = await updateProfileCustomization({
        displayName: payload.displayName,
        motto: payload.motto,
        cardStyle: payload.cardStyle,
        avatarDataUrl: payload.avatarDataUrl,
      });

      this.serverProfile = updated;
      this.currentProfile = updated;
      this.guide.setProfileAvatar(this.resolveGuideAvatar(updated));
      this.profilePanel.update(updated);
      this.profileEditor.setMessage("Profile saved.");
      this.closeProfileEditor();
    } catch (error) {
      this.profileEditor.setMessage(error instanceof Error ? error.message : "Could not save profile. Try again.");
    }
  }

  private async handleSignOut(): Promise<void> {
    if (this.activeSessionId) {
      try {
        await stopGameSession(this.activeSessionId);
      } catch {
        // Ignore stop errors during sign out.
      }
      this.activeSessionId = null;
      this.activeStreamUrl = null;
      this.stopActiveSessionPolling();
    }

    try {
      await logout();
    } catch {
      clearSessionToken();
    }

    this.inFriends = false;
    this.inLibrary = false;
    this.inDetails = false;
    this.inGuide = false;
    this.friends.show(false, this.currentSocialMode);
    this.library.show(false);
    this.details.close();
    this.guide.show(false);
    this.closeProfileEditor();
    this.stopSocialChatPolling();
    this.updateControllerInputSuppression();
    this.activityFeed = [{ id: "signed-out", text: "Signed out. Sign in to continue." }];
    this.renderActivityBanner();
    await this.refreshProfile();
  }

  private async resumeActiveSessionIfPresent(): Promise<void> {
    try {
      const active = await reconnectActiveSession();
      this.activeSessionId = active.sessionId;
      this.activeStreamUrl = active.streamUrl && active.streamUrl.trim().length > 0
        ? active.streamUrl
        : null;

      try {
        this.activeCloudMorphHealth = await getCloudMorphStatus();
      } catch {
        this.activeCloudMorphHealth = null;
      }

      this.inDetails = true;
      this.details.showLaunching(`Streaming ${active.game}`);
      this.details.connectStream(this.activeStreamUrl, this.activeCloudMorphHealth, this.activeSessionId);
      this.beginActiveSessionPolling();
      this.updateControllerInputSuppression();
    } catch {
      this.activeSessionId = null;
      this.activeStreamUrl = null;
      this.stopActiveSessionPolling();
    }
  }

  private beginActiveSessionPolling(): void {
    this.stopActiveSessionPolling();
    this.activeSessionPollHandle = window.setInterval(() => {
      void this.syncActiveSessionStatus();
    }, 3500);
  }

  private stopActiveSessionPolling(): void {
    if (this.activeSessionPollHandle === null) {
      return;
    }

    window.clearInterval(this.activeSessionPollHandle);
    this.activeSessionPollHandle = null;
  }

  private async syncActiveSessionStatus(): Promise<void> {
    if (!this.activeSessionId) {
      this.stopActiveSessionPolling();
      return;
    }

    try {
      const status = await getGameSession(this.activeSessionId);
      const nextStreamUrl = status.streamUrl && status.streamUrl.trim().length > 0
        ? status.streamUrl
        : null;

      if (nextStreamUrl !== this.activeStreamUrl) {
        this.activeStreamUrl = nextStreamUrl;
        this.details.connectStream(this.activeStreamUrl, this.activeCloudMorphHealth, this.activeSessionId);
      }

      if (status.status === "stopped" || status.status === "failed") {
        this.activeSessionId = null;
        this.activeStreamUrl = null;
        this.stopActiveSessionPolling();
      }
    } catch {
      this.activeSessionId = null;
      this.activeStreamUrl = null;
      this.stopActiveSessionPolling();
    }
  }

  private resolveGuideAvatar(profile: NetBoxProfile | null): string | null {
    if (!profile) {
      return null;
    }

    const avatar = profile.customization.avatarDataUrl
      ?? profile.avatar
      ?? profile.settings.avatar
      ?? null;

    if (avatar && avatar.trim().length > 0) {
      return avatar;
    }

    return "/assets/Assets/Profile/FriendPool/20002.png";
  }
}
