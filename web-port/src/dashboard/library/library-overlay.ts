import type { Game } from "../../services/types";

export class LibraryOverlay {
  private readonly root: HTMLDivElement;
  private readonly header: HTMLDivElement;
  private readonly strip: HTMLDivElement;
  private readonly details: HTMLDivElement;
  private readonly itemButtons: HTMLButtonElement[] = [];
  private onSelectionChanged: (() => void) | null = null;
  private selectedIndex = 0;
  private games: Game[] = [];
  private readonly fallbackCovers = [
    "/assets/Assets/Tiles/halo4home.jpg",
    "/assets/Assets/Tiles/forzahorizongames.jpg",
    "/assets/Assets/Tiles/minecraftgames.jpg",
    "/assets/Assets/Tiles/blackops2games.jpg",
    "/assets/Assets/Tiles/kungfupanda2video.jpg",
  ];

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "library-overlay";
    this.root.hidden = true;

    this.header = document.createElement("div");
    this.header.className = "library-header";

    this.strip = document.createElement("div");
    this.strip.className = "library-strip";

    this.details = document.createElement("div");
    this.details.className = "library-details";

    this.root.append(this.header, this.strip, this.details);
    parent.appendChild(this.root);
  }

  public setGames(games: Game[]): void {
    this.games = games;
    this.selectedIndex = 0;
    this.render(true);
  }

  public show(value: boolean): void {
    this.root.hidden = !value;
    if (value) {
      this.render(false);
    }
  }

  public move(delta: number): void {
    if (this.games.length === 0) {
      return;
    }
    this.selectedIndex = (this.selectedIndex + delta + this.games.length) % this.games.length;
    this.render(false, true);
    this.onSelectionChanged?.();
  }

  public onSelectionChangedAction(handler: () => void): void {
    this.onSelectionChanged = handler;
  }

  public get selectedGame(): Game | null {
    return this.games[this.selectedIndex] ?? null;
  }

  private render(forceRebuild: boolean, animated = false): void {
    if (forceRebuild || this.itemButtons.length !== this.games.length) {
      this.rebuildItems();
    }

    this.renderHeader();
    this.renderDetails();
    this.syncSelection(animated);
  }

  private renderHeader(): void {
    const current = this.games.length === 0 ? 0 : this.selectedIndex + 1;

    this.header.innerHTML = `
      <div class="library-header-left">
        <button type="button" class="library-filter">show me<br>all games</button>
        <button type="button" class="library-filter">sort<br>titles</button>
      </div>
      <div class="library-header-right">
        <h3>My Games</h3>
        <p>${current} of ${this.games.length}</p>
      </div>
    `;
  }

  private rebuildItems(): void {
    this.strip.innerHTML = "";
    this.itemButtons.length = 0;

    this.games.forEach((game, index) => {
      const item = document.createElement("button");
      item.className = "library-item";
      item.title = game.title;
      item.addEventListener("click", () => {
        this.selectedIndex = index;
        this.render(false, true);
        this.onSelectionChanged?.();
      });

      const img = document.createElement("img");
      img.className = "library-item-cover";
      img.alt = `${game.title} cover`;
      img.src = this.resolveCover(game, index);
      img.addEventListener("error", () => {
        img.src = this.pickFallback(index);
      }, { once: true });

      const badge = document.createElement("span");
      badge.className = "library-item-badge";
      badge.textContent = "ARCADE";

      const label = document.createElement("span");
      label.className = "library-item-label";
      label.textContent = game.title;

      const subtitle = document.createElement("span");
      subtitle.className = "library-item-subtitle";
      subtitle.textContent = game.subtitle;

      const meta = document.createElement("div");
      meta.className = "library-item-meta";
      meta.innerHTML = "<span class='library-platform'>Xbox 360</span><span class='library-dot'>+</span>";

      item.append(img, badge, label, subtitle, meta);
      this.itemButtons.push(item);
      this.strip.appendChild(item);
    });
  }

  private renderDetails(): void {
    const selected = this.selectedGame;
    if (!selected) {
      this.details.innerHTML = "<span class='library-bottom-hints'><span class='hint-circle a'>A</span> Launch <span class='hint-circle b'>B</span> Back <span class='hint-circle x'>X</span> Game Details</span>";
      return;
    }

    this.details.innerHTML = "<span class='library-bottom-hints'><span class='hint-circle a'>A</span> Launch <span class='hint-circle b'>B</span> Back <span class='hint-circle x'>X</span> Game Details</span>";
  }

  private syncSelection(animated: boolean): void {
    this.itemButtons.forEach((item, index) => {
      item.classList.toggle("is-selected", index === this.selectedIndex);
    });

    const active = this.itemButtons[this.selectedIndex];
    if (!active) {
      return;
    }

    active.scrollIntoView({
      behavior: animated ? "smooth" : "auto",
      block: "nearest",
      inline: "center",
    });
  }

  private resolveCover(game: Game, index: number): string {
    if (!game.coverPath || game.coverPath.trim().length === 0) {
      return this.pickFallback(index);
    }

    return this.toPortraitCover(game.coverPath);
  }

  private toPortraitCover(path: string): string {
    if (path.includes("steamstatic.com") || path.includes("akamaihd.net")) {
      return path.replace(/\/(?:capsule_[^/]*|header|library_hero|library_capsule)\.(jpg|png)(\?.*)?$/i, "/library_600x900_2x.jpg$2");
    }

    return path;
  }

  private pickFallback(index: number): string {
    return this.fallbackCovers[index % this.fallbackCovers.length];
  }
}
