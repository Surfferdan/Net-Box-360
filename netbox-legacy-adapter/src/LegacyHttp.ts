// Thin HTTP transport for the existing, working XeniaManager.Api backend
// (`/api/*`). Deliberately mirrors web-port/src/services/NetBoxClient.ts's
// behavior (bearer token header, one 401 -> /api/refresh -> retry attempt)
// rather than reinventing auth - this adapter package must NOT rebuild
// authentication, per the Phase 12 restrictions. It is injectable
// (fetchImpl + token get/set) so it can run and be unit tested under
// Node's test runner exactly like netbox-client-sdk's NetBoxHttp.

export class LegacyHttpError extends Error {
  public readonly status: number;

  public constructor(status: number, message: string) {
    super(message);
    this.name = "LegacyHttpError";
    this.status = status;
  }
}

export interface LegacyHttpOptions {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
  getToken?: () => string | null;
  setToken?: (token: string) => void;
}

export class LegacyHttp {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  private readonly getToken: () => string | null;
  private readonly setToken: (token: string) => void;

  public constructor(options: LegacyHttpOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "").replace(/\/$/, "");
    this.fetchImpl = options.fetchImpl ?? globalThis.fetch;
    if (!this.fetchImpl) {
      throw new Error("No fetch implementation available; provide options.fetchImpl.");
    }
    this.getToken = options.getToken ?? (() => null);
    this.setToken = options.setToken ?? (() => undefined);
  }

  /** Unauthenticated request - used only for /api/login and /api/account/create. */
  public async requestPublic<T>(method: "GET" | "POST", path: string, body?: unknown): Promise<T> {
    return this.send<T>(method, path, body, /*auth=*/ false);
  }

  /** Authenticated request - attaches the current bearer token and, on a
   * single 401, attempts /api/refresh once before retrying (matches
   * NetBoxClient.ts's existing retry-once behavior). */
  public async requestAuthed<T>(method: "GET" | "POST" | "DELETE", path: string, body?: unknown): Promise<T> {
    const token = this.getToken();
    if (!token) {
      throw new Error("No Net Box session token available.");
    }

    try {
      return await this.send<T>(method, path, body, /*auth=*/ true);
    } catch (error) {
      if (error instanceof LegacyHttpError && error.status === 401 && path !== "/api/refresh") {
        const refreshed = await this.send<{ token?: string }>("POST", "/api/refresh", undefined, true).catch(() => null);
        if (refreshed?.token) {
          this.setToken(refreshed.token);
          return this.send<T>(method, path, body, /*auth=*/ true);
        }
      }
      throw error;
    }
  }

  private async send<T>(method: string, path: string, body: unknown, auth: boolean): Promise<T> {
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    if (auth) {
      const token = this.getToken();
      if (token) {
        headers.Authorization = `Bearer ${token}`;
      }
    }

    const init: RequestInit = { method, headers };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }

    const response = await this.fetchImpl(`${this.baseUrl}${path}`, init);
    const text = await response.text();

    if (!response.ok) {
      throw new LegacyHttpError(response.status, text || `Request failed with status ${response.status}`);
    }

    if (!text) {
      return null as T;
    }
    return JSON.parse(text) as T;
  }
}
