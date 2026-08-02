// Minimal, dependency-free HTTP transport for the NetBox API Gateway. Kept
// separate from any specific fetch/XHR implementation detail so tests can
// inject a mock `fetchImpl`.

export interface NetBoxHttpOptions {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
}

export class NetBoxHttpError extends Error {
  public readonly status: number;

  public constructor(status: number, message: string) {
    super(message);
    this.name = "NetBoxHttpError";
    this.status = status;
  }
}

/**
 * Thin REST transport shared by SessionClient/PlayerClient/StreamClient.
 * Talks directly to the routes exposed by netbox-api's ApiGateway
 * (POST/GET/DELETE /sessions, /sessions/{id}/..., etc.) with no
 * authentication, matching Phase 9's explicit scope boundaries.
 */
export class NetBoxHttp {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;

  public constructor(options: NetBoxHttpOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "").replace(/\/$/, "");
    this.fetchImpl = options.fetchImpl ?? globalThis.fetch;
    if (!this.fetchImpl) {
      throw new Error("No fetch implementation available; provide options.fetchImpl.");
    }
  }

  public async request<T>(method: "GET" | "POST" | "DELETE", path: string, body?: unknown): Promise<T | null> {
    const init: RequestInit = { method };
    if (body !== undefined) {
      init.headers = { "Content-Type": "application/json" };
      init.body = JSON.stringify(body);
    }

    const response = await this.fetchImpl(`${this.baseUrl}${path}`, init);
    const text = await response.text();

    if (!response.ok) {
      throw new NetBoxHttpError(response.status, text || `Request failed with status ${response.status}`);
    }

    if (!text) {
      return null;
    }

    return JSON.parse(text) as T;
  }
}
