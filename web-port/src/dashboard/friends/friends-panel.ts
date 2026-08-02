import type { ActivityItem, ChatMessage, Friend } from "../../services/types";

export type SocialPanelMode = "friends" | "party" | "messages" | "activity" | "chat";

export class FriendsPanel {
  private readonly root: HTMLDivElement;
  private readonly title: HTMLHeadingElement;
  private readonly subtitle: HTMLDivElement;
  private readonly list: HTMLDivElement;
  private readonly actions: HTMLDivElement;
  private readonly addFriendInput: HTMLInputElement;
  private readonly addFriendButton: HTMLButtonElement;
  private readonly removeFriendButton: HTMLButtonElement;
  private readonly composer: HTMLDivElement;
  private readonly recipientSelect: HTMLSelectElement;
  private readonly composerInput: HTMLInputElement;
  private readonly composerButton: HTMLButtonElement;
  private readonly composerStatus: HTMLDivElement;
  private readonly partyActions: HTMLDivElement;
  private readonly joinSessionInput: HTMLInputElement;
  private readonly joinSessionButton: HTMLButtonElement;
  private readonly joinSessionStatus: HTMLDivElement;
  private friends: Friend[] = [];
  private activity: ActivityItem[] = [];
  private chatMessages: ChatMessage[] = [];
  private mode: SocialPanelMode = "friends";
  private selectedFriendId: string | null = null;
  private addFriendAction: ((username: string) => Promise<void> | void) | null = null;
  private removeFriendAction: ((friendUserId: string) => Promise<void> | void) | null = null;
  private chatSendAction: ((message: string, recipientUserId?: string | null) => Promise<void> | void) | null = null;
  private joinSessionAction: ((sessionId: string) => Promise<void> | void) | null = null;

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "friends-overlay";
    this.root.hidden = true;

    this.title = document.createElement("h3");
    this.subtitle = document.createElement("div");
    this.subtitle.className = "friends-subtitle";

    this.list = document.createElement("div");
    this.list.className = "friends-list";

    this.actions = document.createElement("div");
    this.actions.className = "friends-actions";

    this.addFriendInput = document.createElement("input");
    this.addFriendInput.className = "friend-action-input";
    this.addFriendInput.type = "text";
    this.addFriendInput.placeholder = "Add friend by username";
    this.addFriendInput.maxLength = 64;

    this.addFriendButton = document.createElement("button");
    this.addFriendButton.className = "friend-action-button add";
    this.addFriendButton.textContent = "Add Friend";

    this.removeFriendButton = document.createElement("button");
    this.removeFriendButton.className = "friend-action-button remove";
    this.removeFriendButton.textContent = "Remove Selected";

    this.actions.append(this.addFriendInput, this.addFriendButton, this.removeFriendButton);

    this.composer = document.createElement("div");
    this.composer.className = "chat-composer";
    this.composer.hidden = true;

    this.recipientSelect = document.createElement("select");
    this.recipientSelect.className = "chat-recipient";

    this.composerInput = document.createElement("input");
    this.composerInput.className = "chat-input";
    this.composerInput.type = "text";
    this.composerInput.placeholder = "Type a message...";
    this.composerInput.maxLength = 300;

    this.composerButton = document.createElement("button");
    this.composerButton.className = "chat-send";
    this.composerButton.textContent = "Send";

    this.composerStatus = document.createElement("div");
    this.composerStatus.className = "chat-status";

    this.partyActions = document.createElement("div");
    this.partyActions.className = "friends-actions party-actions";
    this.partyActions.hidden = true;

    this.joinSessionInput = document.createElement("input");
    this.joinSessionInput.className = "friend-action-input";
    this.joinSessionInput.type = "text";
    this.joinSessionInput.placeholder = "Enter session ID to join";
    this.joinSessionInput.maxLength = 64;

    this.joinSessionButton = document.createElement("button");
    this.joinSessionButton.className = "friend-action-button add";
    this.joinSessionButton.textContent = "Join Session";

    this.joinSessionStatus = document.createElement("div");
    this.joinSessionStatus.className = "chat-status";

    this.partyActions.append(this.joinSessionInput, this.joinSessionButton, this.joinSessionStatus);

    this.addFriendButton.addEventListener("click", () => {
      void this.handleAddFriend();
    });

    this.removeFriendButton.addEventListener("click", () => {
      void this.handleRemoveFriend();
    });

    this.addFriendInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        void this.handleAddFriend();
      }
    });

    this.composerButton.addEventListener("click", () => {
      void this.handleSend();
    });

    this.composerInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        void this.handleSend();
      }
    });

    this.composer.append(this.recipientSelect, this.composerInput, this.composerButton, this.composerStatus);

    this.joinSessionButton.addEventListener("click", () => {
      void this.handleJoinSession();
    });

    this.joinSessionInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        void this.handleJoinSession();
      }
    });

    this.root.append(this.title, this.subtitle, this.list, this.actions, this.partyActions, this.composer);
    parent.appendChild(this.root);

    this.render();
  }

  public setFeed(friends: Friend[], activity: ActivityItem[]): void {
    this.friends = [...friends];
    this.activity = [...activity];
    this.render();
  }

  public setFriends(friends: Friend[]): void {
    this.friends = [...friends];
    if (this.selectedFriendId && !this.friends.some((friend) => friend.id === this.selectedFriendId)) {
      this.selectedFriendId = null;
    }
    this.render();
  }

  public setMode(mode: SocialPanelMode): void {
    this.mode = mode;
    this.render();
  }

  public setChatMessages(messages: ChatMessage[]): void {
    this.chatMessages = [...messages];
    if (this.mode === "chat" || this.mode === "messages") {
      this.render();
    }
  }

  public setAddFriendAction(action: (username: string) => Promise<void> | void): void {
    this.addFriendAction = action;
  }

  public setRemoveFriendAction(action: (friendUserId: string) => Promise<void> | void): void {
    this.removeFriendAction = action;
  }

  public setChatSendAction(action: (message: string, recipientUserId?: string | null) => Promise<void> | void): void {
    this.chatSendAction = action;
  }

  public setJoinSessionAction(action: (sessionId: string) => Promise<void> | void): void {
    this.joinSessionAction = action;
  }

  public show(value: boolean, mode: SocialPanelMode = this.mode): void {
    this.mode = mode;
    this.render();
    this.root.hidden = !value;
  }

  private render(): void {
    const onlineFriends = this.friends.filter((friend) => friend.subtitle.toLowerCase() === "online");
    const rowData = this.getRowsForMode(onlineFriends);

    this.title.textContent = rowData.title;
    this.subtitle.textContent = rowData.subtitle;
    this.actions.hidden = this.mode !== "friends";
    this.partyActions.hidden = this.mode !== "party";
    this.composer.hidden = !(this.mode === "chat" || this.mode === "messages");

    this.list.innerHTML = "";

    this.recipientSelect.innerHTML = "";
    const allOption = document.createElement("option");
    allOption.value = "";
    allOption.textContent = "All friends (public)";
    this.recipientSelect.appendChild(allOption);

    for (const friend of this.friends) {
      const option = document.createElement("option");
      option.value = friend.id;
      option.textContent = friend.gamertag;
      this.recipientSelect.appendChild(option);
    }

    if (this.selectedFriendId) {
      this.recipientSelect.value = this.selectedFriendId;
    }

    this.removeFriendButton.disabled = !this.selectedFriendId;

    if (rowData.rows.length === 0) {
      const empty = document.createElement("div");
      empty.className = "friend-row friend-row-empty";
      empty.textContent = rowData.emptyMessage;
      this.list.appendChild(empty);
      return;
    }

    for (const item of rowData.rows) {
      const rowElement = document.createElement("div");
      rowElement.className = "friend-row";

      if (item.friendId) {
        if (item.friendId === this.selectedFriendId) {
          rowElement.classList.add("is-selected");
        }

        rowElement.addEventListener("click", () => {
          this.selectedFriendId = item.friendId ?? null;
          this.composerStatus.textContent = item.primary ? `Selected ${item.primary}` : "";
          this.render();
        });
      }

      if (item.avatarPath) {
        rowElement.innerHTML = `
          <img src="${item.avatarPath}" alt="" />
          <div>
            <div class="friend-name">${item.primary}</div>
            <div class="friend-sub">${item.secondary}</div>
          </div>
        `;
      } else {
        rowElement.innerHTML = `
          <div class="friend-pill">${item.badge}</div>
          <div>
            <div class="friend-name">${item.primary}</div>
            <div class="friend-sub">${item.secondary}</div>
          </div>
        `;
      }

      if (item.joinSessionId) {
        const joinButton = document.createElement("button");
        joinButton.type = "button";
        joinButton.className = "friend-row-join-button";
        joinButton.textContent = "Join";
        joinButton.addEventListener("click", (event) => {
          event.stopPropagation();
          void this.handleJoinFriendSession(item.joinSessionId as string, joinButton);
        });
        rowElement.appendChild(joinButton);
      }

      this.list.appendChild(rowElement);
    }
  }

  private async handleJoinFriendSession(sessionId: string, button: HTMLButtonElement): Promise<void> {
    if (!this.joinSessionAction) {
      this.composerStatus.textContent = "Joining sessions is unavailable.";
      return;
    }

    button.disabled = true;
    try {
      await this.joinSessionAction(sessionId);
    } catch (error) {
      this.composerStatus.textContent = error instanceof Error ? error.message : "Could not join session.";
      button.disabled = false;
    }
  }

  private getRowsForMode(onlineFriends: Friend[]): {
    title: string;
    subtitle: string;
    emptyMessage: string;
    rows: Array<{ primary: string; secondary: string; avatarPath?: string; badge?: string; friendId?: string; joinSessionId?: string }>;
  } {
    if (this.mode === "friends") {
      return {
        title: "Friends",
        subtitle: `${onlineFriends.length} online / ${this.friends.length} total`,
        emptyMessage: "No friends in your list. Add one by username.",
        rows: this.friends.map((friend) => ({
          primary: friend.gamertag,
          secondary: `${friend.subtitle} - ${friend.status}`,
          avatarPath: friend.avatarPath,
          friendId: friend.id,
        })),
      };
    }

    if (this.mode === "party") {
      return {
        title: "Party",
        subtitle: "Invite friends, or enter a session ID below to join theirs",
        emptyMessage: "No online friends available for party invites.",
        rows: onlineFriends.map((friend) => ({
          primary: friend.gamertag,
          secondary: friend.activeSessionId
            ? `Playing ${friend.activeGameTitle ?? "a game"} - Join to hop in`
            : "Ready to invite - open Party from Guide.",
          avatarPath: friend.avatarPath,
          joinSessionId: friend.activeSessionId ?? undefined,
        })),
      };
    }

    if (this.mode === "messages") {
      return {
        title: "Messages",
        subtitle: "Direct message feed",
        emptyMessage: "No messages yet.",
        rows: this.chatMessages.map((message) => ({
          primary: message.isMine ? `You: ${message.message}` : `${message.fromGamertag}: ${message.message}`,
          secondary: this.formatChatMeta(message),
          badge: message.toGamertag ? "DM" : "IM",
        })),
      };
    }

    if (this.mode === "activity") {
      return {
        title: "Beacons & Activity",
        subtitle: "Live Net Box updates",
        emptyMessage: "No recent activity.",
        rows: this.activity.map((item) => ({
          primary: item.text,
          secondary: "Activity",
          badge: "A",
        })),
      };
    }

    return {
      title: "Chat",
      subtitle: "Text chat presence",
      emptyMessage: "No chat presence available.",
      rows: this.chatMessages.map((message) => ({
        primary: message.isMine ? `You: ${message.message}` : `${message.fromGamertag}: ${message.message}`,
        secondary: this.formatChatMeta(message),
        badge: message.toGamertag ? "DM" : message.isMine ? "ME" : "IM",
      })),
    };
  }

  private formatChatMeta(message: ChatMessage): string {
    const sent = new Date(message.sentAtUtc);
    const localTime = Number.isNaN(sent.getTime())
      ? "unknown time"
      : sent.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    const target = message.toGamertag ? ` to ${message.toGamertag}` : "";
    return `${message.fromGamertag}${target} at ${localTime}`;
  }

  private async handleSend(): Promise<void> {
    const value = this.composerInput.value.trim();
    if (!value) {
      return;
    }

    if (!this.chatSendAction) {
      this.composerStatus.textContent = "Chat send is unavailable.";
      return;
    }

    this.composerInput.disabled = true;
    this.recipientSelect.disabled = true;
    this.composerButton.disabled = true;
    this.composerStatus.textContent = "Sending...";

    try {
      const recipientUserId = this.recipientSelect.value || null;
      await this.chatSendAction(value, recipientUserId);
      this.composerInput.value = "";
      this.composerStatus.textContent = "Sent.";
    } catch (error) {
      this.composerStatus.textContent = error instanceof Error ? error.message : "Send failed.";
    } finally {
      this.composerInput.disabled = false;
      this.recipientSelect.disabled = false;
      this.composerButton.disabled = false;
      this.composerInput.focus();
    }
  }

  private async handleJoinSession(): Promise<void> {
    const sessionId = this.joinSessionInput.value.trim();
    if (!sessionId) {
      this.joinSessionStatus.textContent = "Enter a session ID to join.";
      return;
    }

    if (!this.joinSessionAction) {
      this.joinSessionStatus.textContent = "Joining sessions is unavailable.";
      return;
    }

    this.joinSessionInput.disabled = true;
    this.joinSessionButton.disabled = true;
    this.joinSessionStatus.textContent = "Joining...";
    try {
      await this.joinSessionAction(sessionId);
      this.joinSessionInput.value = "";
      this.joinSessionStatus.textContent = "Joined session.";
    } catch (error) {
      this.joinSessionStatus.textContent = error instanceof Error ? error.message : "Could not join session.";
    } finally {
      this.joinSessionInput.disabled = false;
      this.joinSessionButton.disabled = false;
    }
  }

  private async handleAddFriend(): Promise<void> {
    const username = this.addFriendInput.value.trim();
    if (!username) {
      this.composerStatus.textContent = "Enter a username to add.";
      return;
    }

    if (!this.addFriendAction) {
      this.composerStatus.textContent = "Friend add is unavailable.";
      return;
    }

    this.addFriendInput.disabled = true;
    this.addFriendButton.disabled = true;
    this.composerStatus.textContent = "Adding friend...";
    try {
      await this.addFriendAction(username);
      this.addFriendInput.value = "";
      this.composerStatus.textContent = "Friend added.";
    } catch (error) {
      this.composerStatus.textContent = error instanceof Error ? error.message : "Could not add friend.";
    } finally {
      this.addFriendInput.disabled = false;
      this.addFriendButton.disabled = false;
      this.addFriendInput.focus();
    }
  }

  private async handleRemoveFriend(): Promise<void> {
    if (!this.selectedFriendId) {
      this.composerStatus.textContent = "Select a friend to remove.";
      return;
    }

    if (!this.removeFriendAction) {
      this.composerStatus.textContent = "Friend remove is unavailable.";
      return;
    }

    this.removeFriendButton.disabled = true;
    this.composerStatus.textContent = "Removing friend...";
    try {
      await this.removeFriendAction(this.selectedFriendId);
      this.selectedFriendId = null;
      this.composerStatus.textContent = "Friend removed.";
    } catch (error) {
      this.composerStatus.textContent = error instanceof Error ? error.message : "Could not remove friend.";
    } finally {
      this.removeFriendButton.disabled = false;
      this.render();
    }
  }
}
