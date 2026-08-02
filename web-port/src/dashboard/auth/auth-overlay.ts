import type { CreateAccountRequest, LoginRequest } from "../../services/AuthService";

type AuthMode = "login" | "create";

export class AuthOverlay {
  private readonly root: HTMLDivElement;
  private readonly title: HTMLHeadingElement;
  private readonly modeSwitch: HTMLButtonElement;
  private readonly message: HTMLDivElement;
  private readonly username: HTMLInputElement;
  private readonly password: HTMLInputElement;
  private readonly displayName: HTMLInputElement;
  private readonly primaryButton: HTMLButtonElement;
  private readonly secondaryButton: HTMLButtonElement;
  private mode: AuthMode = "login";
  private fieldCounter = 0;
  private onSubmit: ((payload: LoginRequest | CreateAccountRequest, mode: AuthMode) => Promise<void> | void) | null = null;

  public constructor(parent: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "auth-overlay";

    const card = document.createElement("div");
    card.className = "auth-card";

    this.title = document.createElement("h2");
    this.title.textContent = "Net Box Sign In";

    const subtitle = document.createElement("p");
    subtitle.className = "auth-subtitle";
    subtitle.textContent = "Create a Net Box account or sign in to continue.";

    const form = document.createElement("div");
    form.className = "auth-form";

    this.username = this.buildInput("Username", "text", form);
    this.password = this.buildInput("Password", "password", form);
    this.displayName = this.buildInput("Display Name", "text", form);

    this.message = document.createElement("div");
    this.message.className = "auth-message";

    const actions = document.createElement("div");
    actions.className = "auth-actions";

    this.primaryButton = document.createElement("button");
    this.primaryButton.className = "auth-primary";
    this.primaryButton.textContent = "Sign In";

    this.secondaryButton = document.createElement("button");
    this.secondaryButton.className = "auth-secondary";
    this.secondaryButton.textContent = "Create Account";

    this.modeSwitch = document.createElement("button");
    this.modeSwitch.className = "auth-switch";
    this.modeSwitch.textContent = "Need an account?";

    actions.append(this.primaryButton, this.secondaryButton, this.modeSwitch);
    card.append(this.title, subtitle, form, this.message, actions);
    this.root.appendChild(card);
    parent.appendChild(this.root);

    this.primaryButton.addEventListener("click", () => {
      void this.submit();
    });

    this.secondaryButton.addEventListener("click", () => {
      this.setMode(this.mode === "login" ? "create" : "login");
    });

    this.modeSwitch.addEventListener("click", () => {
      this.setMode(this.mode === "login" ? "create" : "login");
    });

    this.setMode("login");
  }

  public setVisible(value: boolean): void {
    this.root.hidden = !value;
  }

  public setMessage(message: string): void {
    this.message.textContent = message;
  }

  public onSubmitAction(handler: (payload: LoginRequest | CreateAccountRequest, mode: AuthMode) => Promise<void> | void): void {
    this.onSubmit = handler;
  }

  private setMode(mode: AuthMode): void {
    this.mode = mode;
    this.root.dataset.mode = mode;
    this.title.textContent = mode === "login" ? "Net Box Sign In" : "Create Net Box Account";
    this.primaryButton.textContent = mode === "login" ? "Sign In" : "Create Account";
    this.secondaryButton.textContent = mode === "login" ? "Create Account" : "Back to Sign In";
    this.modeSwitch.textContent = mode === "login" ? "Need an account?" : "Already have an account?";
    this.displayName.parentElement!.hidden = mode !== "create";
    this.message.textContent = "";
  }

  private async submit(): Promise<void> {
    if (!this.onSubmit) {
      return;
    }

    const username = this.username.value.trim();
    const password = this.password.value;
    const displayName = this.displayName.value.trim() || username;

    if (!username || !password) {
      this.setMessage("Enter a username and password.");
      return;
    }

    if (this.mode === "create" && !displayName) {
      this.setMessage("Enter a display name.");
      return;
    }

    this.setMessage("Working...");
    await this.onSubmit(
      this.mode === "login"
        ? { username, password }
        : { username, password, displayName },
      this.mode,
    );
  }

  private buildInput(labelText: string, type: string, form: HTMLElement): HTMLInputElement {
    const row = document.createElement("label");
    row.className = "auth-field";

    const label = document.createElement("span");
    label.textContent = labelText;

    const input = document.createElement("input");
    const key = labelText.toLowerCase().replace(/[^a-z0-9]+/g, "-");
    const id = `auth-${key}-${this.fieldCounter++}`;
    input.type = type;
    input.id = id;
    input.name = key;
    input.autocomplete = type === "password" ? "current-password" : "username";
    input.spellcheck = false;

    row.htmlFor = id;

    row.append(label, input);
    form.appendChild(row);
    return input;
  }
}
