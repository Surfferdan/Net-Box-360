import { clearSessionToken, getSessionToken, requestJson, setSessionToken } from "./NetBoxClient";

export interface CreateAccountRequest {
  username: string;
  password: string;
  displayName: string;
  email?: string | null;
}

export interface CreateAccountResponse {
  success: boolean;
  userId: number;
  profile: {
    username: string;
    displayName: string;
  };
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userId: number;
}

export async function createAccount(request: CreateAccountRequest): Promise<CreateAccountResponse> {
  return requestJson<CreateAccountResponse>("/api/account/create", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await requestJson<LoginResponse>("/api/login", {
    method: "POST",
    body: JSON.stringify(request),
  });

  setSessionToken(response.token);
  return response;
}

export async function logout(): Promise<void> {
  const token = getSessionToken();
  if (!token) {
    clearSessionToken();
    return;
  }

  await requestJson<{ success: boolean }>("/api/logout", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
    },
  });

  clearSessionToken();
}
