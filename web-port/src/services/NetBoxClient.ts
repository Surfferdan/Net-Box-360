const DEFAULT_API_BASE_URL = "";
const TOKEN_STORAGE_KEY = "netbox.sessionToken";

export class NetBoxHttpError extends Error {
  public readonly status: number;
  public readonly responseText: string;

  public constructor(status: number, responseText: string) {
    super(responseText || `Request failed with status ${status}`);
    this.name = "NetBoxHttpError";
    this.status = status;
    this.responseText = responseText;
  }
}

export function getNetBoxApiBaseUrl(): string {
  return (import.meta.env.VITE_NETBOX_API_BASE_URL ?? DEFAULT_API_BASE_URL).replace(/\/$/, "");
}

export function getSessionToken(): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  return window.localStorage.getItem(TOKEN_STORAGE_KEY);
}

export function setSessionToken(token: string): void {
  window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
}

export function clearSessionToken(): void {
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
}

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers ?? {});
  headers.set("Content-Type", "application/json");
  const requestInit: RequestInit = { ...init, headers };

  let response = await fetch(`${getNetBoxApiBaseUrl()}${path}`, requestInit);

  if (response.status === 401 && path !== "/api/refresh" && typeof window !== "undefined") {
    const currentToken = window.localStorage.getItem(TOKEN_STORAGE_KEY);
    if (currentToken) {
      const refreshResponse = await fetch(`${getNetBoxApiBaseUrl()}/api/refresh`, {
        method: "POST",
        headers: {
          Authorization: `Bearer ${currentToken}`,
          "Content-Type": "application/json",
        },
      });

      if (refreshResponse.ok) {
        const refreshData = await refreshResponse.json() as { token?: string };
        if (refreshData.token) {
          setSessionToken(refreshData.token);
          const retryHeaders = new Headers(init?.headers ?? {});
          retryHeaders.set("Content-Type", "application/json");
          retryHeaders.set("Authorization", `Bearer ${refreshData.token}`);
          response = await fetch(`${getNetBoxApiBaseUrl()}${path}`, { ...requestInit, headers: retryHeaders });
        }
      } else {
        clearSessionToken();
      }
    }
  }

  if (!response.ok) {
    const text = await response.text();
    throw new NetBoxHttpError(response.status, text);
  }

  return response.json() as Promise<T>;
}

export { requestJson };
