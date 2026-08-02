export class GuideOverlay {
  private readonly root: HTMLDivElement;
  private readonly blade: HTMLDivElement;
  private readonly rootAnchor: Comment;
  private readonly avatar: HTMLImageElement;
  private readonly menu: HTMLUListElement;
  private readonly menuElements: HTMLLIElement[] = [];
  private selectedIndex = 0;
  private badgeCounts: { friends: number; party: number; messages: number } = {
    friends: 0,
    party: 0,
    messages: 0,
  };
  private readonly items: Array<{ id: string; label: string }> = [
    { id: "home", label: "End Session" },
    { id: "friends", label: "Friends" },
    { id: "party", label: "Party" },
    { id: "messages", label: "Messages" },
    { id: "activity", label: "Beacons & Activity" },
    { id: "chat", label: "Chat" },
    { id: "storage", label: "Manage Storage" },
  ];

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "guide-overlay";
    this.root.hidden = true;
    this.rootAnchor = document.createComment("guide-overlay-anchor");

    this.blade = document.createElement("div");
    this.blade.className = "guide-blade";

    const leftRail = document.createElement("div");
    leftRail.className = "guide-rail left";
    leftRail.textContent = "Games";

    const rightRail = document.createElement("div");
    rightRail.className = "guide-rail right";
    rightRail.textContent = "Settings";

    const profileStrip = document.createElement("div");
    profileStrip.className = "guide-profile-strip";
    profileStrip.textContent = "Net Box";

    const title = document.createElement("div");
    title.className = "guide-title";
    title.textContent = "Xbox Guide";

    this.avatar = document.createElement("img");
    this.avatar.className = "guide-title-avatar";
    this.avatar.alt = "Profile avatar";
    this.avatar.hidden = true;
    title.appendChild(this.avatar);

    this.menu = document.createElement("ul");
    this.menu.className = "guide-menu";

    this.blade.append(leftRail, rightRail, profileStrip, title, this.menu);
    this.root.appendChild(this.blade);
    parent.appendChild(this.root);

    this.render();
  }

  public show(value: boolean): void {
    if (value && this.root.parentElement !== document.body) {
      this.root.parentElement?.insertBefore(this.rootAnchor, this.root);
      document.body.appendChild(this.root);
    }

    if (!value && this.root.parentElement === document.body) {
      this.rootAnchor.replaceWith(this.root);
    }

    this.root.hidden = !value;
    if (value) {
      this.root.classList.remove("guide-enter");
      void this.root.offsetWidth;
      this.root.classList.add("guide-enter");
    }
  }

  public move(delta: number): void {
    this.selectedIndex = (this.selectedIndex + delta + this.items.length) % this.items.length;
    this.renderSelection();
  }

  /** The currently highlighted guide menu item id (e.g. "home"). */
  public get selectedItem(): string {
    return this.items[this.selectedIndex]?.id ?? "home";
  }

  public setHomeActionLabel(label: "End Session" | "Leave Session"): void {
    if (this.items[0].label === label) {
      return;
    }

    this.items[0] = { ...this.items[0], label };
    this.render();
  }

  public setBadgeCounts(counts: Partial<{ friends: number; party: number; messages: number }>): void {
    this.badgeCounts = {
      ...this.badgeCounts,
      ...counts,
    };
    this.renderBadges();
  }

  public setProfileAvatar(src: string | null): void {
    if (!src || src.trim().length === 0) {
      this.avatar.hidden = true;
      this.avatar.removeAttribute("src");
      return;
    }

    this.avatar.src = src;
    this.avatar.hidden = false;
  }

  private render(): void {
    this.menu.innerHTML = "";
    this.menuElements.length = 0;
    this.items.forEach((item, index) => {
      const li = document.createElement("li");
      li.className = index === this.selectedIndex ? "is-selected" : "";

      const label = document.createElement("span");
      label.className = "guide-item-label";
      label.textContent = item.label;

      const meta = document.createElement("span");
      meta.className = "guide-item-meta";

      if (item.id === "friends") {
        meta.textContent = `${this.badgeCounts.friends}`;
      } else if (item.id === "party") {
        meta.textContent = `${this.badgeCounts.party}`;
      } else if (item.id === "messages") {
        meta.textContent = `${this.badgeCounts.messages}`;
      } else if (item.id === "activity") {
        meta.textContent = "*";
      } else if (item.id === "chat") {
        meta.textContent = "IM";
      } else {
        meta.textContent = "";
      }

      li.append(label, meta);
      this.menu.appendChild(li);
      this.menuElements.push(li);
    });
  }

  private renderSelection(): void {
    for (let i = 0; i < this.menuElements.length; i += 1) {
      this.menuElements[i].classList.toggle("is-selected", i === this.selectedIndex);
    }
  }

  private renderBadges(): void {
    for (const li of this.menuElements) {
      const label = li.querySelector(".guide-item-label")?.textContent;
      const meta = li.querySelector(".guide-item-meta");
      if (!meta || !label) {
        continue;
      }

      if (label === "Friends") {
        meta.textContent = `${this.badgeCounts.friends}`;
      } else if (label === "Party") {
        meta.textContent = `${this.badgeCounts.party}`;
      } else if (label === "Messages") {
        meta.textContent = `${this.badgeCounts.messages}`;
      }
    }
  }
}
