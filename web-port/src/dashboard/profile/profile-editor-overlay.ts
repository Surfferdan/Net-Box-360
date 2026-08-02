import type { NetBoxProfile } from "../../services/ProfileService";

export interface ProfileEditorSavePayload {
  displayName: string;
  motto: string;
  cardStyle: "classic" | "emerald" | "sunset" | "midnight";
  avatarDataUrl: string | null;
}

export class ProfileEditorOverlay {
  private readonly root: HTMLDivElement;
  private readonly panel: HTMLDivElement;
  private readonly form: HTMLFormElement;
  private readonly message: HTMLDivElement;
  private readonly displayNameInput: HTMLInputElement;
  private readonly mottoInput: HTMLInputElement;
  private readonly cardSelect: HTMLSelectElement;
  private readonly avatarPreview: HTMLImageElement;
  private readonly avatarFileInput: HTMLInputElement;
  private fieldCounter = 0;
  private onSave: ((payload: ProfileEditorSavePayload) => void) | null = null;
  private onCancel: (() => void) | null = null;
  private avatarDataUrl: string | null = null;

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "profile-editor-overlay";
    this.root.hidden = true;

    this.panel = document.createElement("div");
    this.panel.className = "profile-editor-panel";

    const title = document.createElement("h2");
    title.textContent = "Profile Editor";

    const subtitle = document.createElement("p");
    subtitle.className = "profile-editor-subtitle";
    subtitle.textContent = "Customize your account card and upload a custom profile picture.";

    this.form = document.createElement("form");
    this.form.className = "profile-editor-form";

    const avatarBlock = document.createElement("div");
    avatarBlock.className = "profile-editor-avatar-block";

    this.avatarPreview = document.createElement("img");
    this.avatarPreview.className = "profile-editor-avatar";
    this.avatarPreview.alt = "Profile preview";

    this.avatarFileInput = document.createElement("input");
    this.avatarFileInput.id = "profile-avatar-file";
    this.avatarFileInput.name = "avatarFile";
    this.avatarFileInput.type = "file";
    this.avatarFileInput.accept = "image/png,image/jpeg,image/webp";
    this.avatarFileInput.hidden = true;
    this.avatarFileInput.addEventListener("change", () => {
      const file = this.avatarFileInput.files?.[0];
      if (!file) {
        return;
      }

      if (file.size > 5 * 1024 * 1024) {
        this.message.textContent = "Choose an image smaller than 5 MB.";
        this.avatarFileInput.value = "";
        return;
      }

      const reader = new FileReader();
      reader.onload = () => {
        const result = typeof reader.result === "string" ? reader.result : null;
        if (!result) {
          return;
        }

        this.avatarDataUrl = result;
        this.avatarPreview.src = result;
        this.message.textContent = "Custom avatar loaded. Save to keep changes.";
      };
      reader.readAsDataURL(file);
    });

    const avatarActions = document.createElement("div");
    avatarActions.className = "profile-editor-avatar-actions";

    const uploadButton = document.createElement("button");
    uploadButton.type = "button";
    uploadButton.className = "profile-editor-upload";
    uploadButton.textContent = "Upload Picture";
    uploadButton.addEventListener("click", () => {
      this.avatarFileInput.click();
    });

    const clearButton = document.createElement("button");
    clearButton.type = "button";
    clearButton.className = "profile-editor-clear";
    clearButton.textContent = "Use Default";
    clearButton.addEventListener("click", () => {
      this.avatarDataUrl = null;
      this.avatarFileInput.value = "";
      this.avatarPreview.src = "/assets/Assets/Profile/profilepicture.jpg";
      this.message.textContent = "Default profile image selected.";
    });

    avatarActions.append(uploadButton, clearButton);
    avatarBlock.append(this.avatarPreview, avatarActions, this.avatarFileInput);

    const fieldGrid = document.createElement("div");
    fieldGrid.className = "profile-editor-fields";

    this.displayNameInput = this.createInput("Display Name", "text", fieldGrid);
    this.mottoInput = this.createInput("Motto / Status", "text", fieldGrid);

    const cardField = document.createElement("label");
    cardField.className = "profile-editor-field";
    cardField.textContent = "Character Card";
    this.cardSelect = document.createElement("select");
    this.cardSelect.id = "profile-card-style";
    this.cardSelect.name = "cardStyle";
    cardField.htmlFor = this.cardSelect.id;
    this.cardSelect.innerHTML = `
      <option value="classic">Classic</option>
      <option value="emerald">Emerald</option>
      <option value="sunset">Sunset</option>
      <option value="midnight">Midnight</option>
    `;
    cardField.appendChild(this.cardSelect);
    fieldGrid.appendChild(cardField);

    this.message = document.createElement("div");
    this.message.className = "profile-editor-message";

    const actions = document.createElement("div");
    actions.className = "profile-editor-actions";

    const saveButton = document.createElement("button");
    saveButton.type = "submit";
    saveButton.className = "profile-editor-save";
    saveButton.textContent = "Save Profile";

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "profile-editor-cancel";
    cancelButton.textContent = "Cancel";
    cancelButton.addEventListener("click", () => {
      this.onCancel?.();
    });

    actions.append(saveButton, cancelButton);

    this.form.append(avatarBlock, fieldGrid, this.message, actions);
    this.form.addEventListener("submit", (event) => {
      event.preventDefault();

      const payload: ProfileEditorSavePayload = {
        displayName: this.displayNameInput.value.trim(),
        motto: this.mottoInput.value.trim(),
        cardStyle: (this.cardSelect.value as "classic" | "emerald" | "sunset" | "midnight") ?? "classic",
        avatarDataUrl: this.avatarDataUrl,
      };

      if (!payload.displayName) {
        this.message.textContent = "Display name cannot be empty.";
        return;
      }

      this.onSave?.(payload);
    });

    this.panel.append(title, subtitle, this.form);
    this.root.appendChild(this.panel);
    parent.appendChild(this.root);
  }

  public onSaveAction(handler: (payload: ProfileEditorSavePayload) => void): void {
    this.onSave = handler;
  }

  public onCancelAction(handler: () => void): void {
    this.onCancel = handler;
  }

  public setMessage(value: string): void {
    this.message.textContent = value;
  }

  public show(value: boolean): void {
    this.root.hidden = !value;
  }

  public hydrate(profile: NetBoxProfile): void {
    this.displayNameInput.value = profile.displayName?.trim() || profile.username?.trim() || "Player";
    this.mottoInput.value = profile.motto ?? "";
    this.cardSelect.value = profile.cardStyle ?? "classic";
    this.avatarDataUrl = profile.customization?.avatarDataUrl ?? profile.avatar ?? null;
    this.avatarPreview.src = this.avatarDataUrl ?? "/assets/Assets/Profile/profilepicture.jpg";
    this.avatarFileInput.value = "";
    this.message.textContent = "";
  }

  private createInput(labelText: string, type: string, parent: HTMLElement): HTMLInputElement {
    const wrapper = document.createElement("label");
    wrapper.className = "profile-editor-field";
    wrapper.textContent = labelText;

    const input = document.createElement("input");
    const key = labelText.toLowerCase().replace(/[^a-z0-9]+/g, "-");
    const id = `profile-${key}-${this.fieldCounter++}`;
    input.type = type;
    input.id = id;
    input.name = key;

    wrapper.htmlFor = id;

    wrapper.appendChild(input);
    parent.appendChild(wrapper);
    return input;
  }
}
