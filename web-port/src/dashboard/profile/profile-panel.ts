import type { NetBoxProfile } from "../../services/ProfileService";

export class ProfilePanel {
  private readonly root: HTMLDivElement;
  private readonly tag: HTMLDivElement;
  private readonly stats: HTMLDivElement;
  private readonly motto: HTMLDivElement;
  private readonly recent: HTMLDivElement;
  private readonly avatar: HTMLImageElement;
  private onOpen: (() => void) | null = null;

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "profile-panel";

    this.tag = document.createElement("div");
    this.tag.className = "profile-tag";

    this.stats = document.createElement("div");
    this.stats.className = "profile-stats";

    this.motto = document.createElement("div");
    this.motto.className = "profile-motto";

    this.recent = document.createElement("div");
    this.recent.className = "profile-recent";

    this.avatar = document.createElement("img");
    this.avatar.className = "profile-avatar";

    this.root.addEventListener("click", () => {
      this.onOpen?.();
    });

    this.root.append(this.tag, this.stats, this.motto, this.recent, this.avatar);
    parent.appendChild(this.root);
  }

  public update(profile: NetBoxProfile): void {
    const resolvedName = profile.displayName?.trim() || profile.username?.trim() || "Player";
    this.tag.textContent = resolvedName;
    this.stats.textContent = `${profile.gamerscore}G • ${profile.achievements.length} achievements`;
    this.motto.textContent = profile.motto?.trim() ? profile.motto : "";
    this.recent.textContent = profile.recentGames.length > 0 ? profile.recentGames.slice(0, 2).join(" • ") : "No recent games yet";
    this.avatar.src = profile.avatar ?? "/assets/Assets/Profile/profilepicture.jpg";
    this.root.classList.remove("profile-card-classic", "profile-card-emerald", "profile-card-sunset", "profile-card-midnight");
    this.root.classList.add(`profile-card-${profile.cardStyle ?? "classic"}`);
  }

  public onOpenAction(handler: () => void): void {
    this.onOpen = handler;
  }

  public setVisible(value: boolean): void {
    this.root.hidden = !value;
  }
}
