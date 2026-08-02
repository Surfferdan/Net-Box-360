import { getSessionToken, requestJson } from "./NetBoxClient";

const PROFILE_CUSTOMIZATION_FALLBACK_KEY = "netbox.profileCustomizationFallback";
const PROFILE_CUSTOMIZATION_ENDPOINT_STATE_KEY = "netbox.profileCustomizationEndpointState";

export interface NetBoxAchievement {
  id: string;
  name: string;
  description: string;
  gamerscore: number;
  isUnlocked: boolean;
  unlockedAt: string | null;
  progressPercent: number | null;
}

export interface NetBoxProfile {
  username: string;
  displayName: string;
  avatar: string | null;
  motto: string;
  cardStyle: "classic" | "emerald" | "sunset" | "midnight";
  gamerscore: number;
  recentGames: string[];
  achievements: NetBoxAchievement[];
  settings: {
    userId: number;
    username: string;
    email: string | null;
    avatar: string | null;
    theme: string;
    controllerPreference: string;
    language: string;
  };
  customization: {
    displayName: string;
    motto: string;
    cardStyle: "classic" | "emerald" | "sunset" | "midnight";
    avatarDataUrl: string | null;
  };
}

type NetBoxProfilePayload = Partial<NetBoxProfile> & {
  gamertag?: string;
  settings?: Partial<NetBoxProfile["settings"]>;
  customization?: Partial<NetBoxProfile["customization"]>;
};

export interface UpdateProfileCustomizationRequest {
  displayName: string;
  motto: string;
  cardStyle: "classic" | "emerald" | "sunset" | "midnight";
  avatarDataUrl: string | null;
}

export async function getProfile(): Promise<NetBoxProfile> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  const payload = await requestJson<NetBoxProfilePayload>("/api/profile/me", {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`,
    },
  });

  return applyLocalCustomizationFallback(normalizeProfilePayload(payload));
}

export async function updateProfileCustomization(request: UpdateProfileCustomizationRequest): Promise<NetBoxProfile> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  if (isCustomizationEndpointUnsupported()) {
    return applyCustomizationLocally(request);
  }

  try {
    const payload = await requestJson<NetBoxProfilePayload>("/api/profile/me/customization", {
      method: "PUT",
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(request),
    });

    const normalized = normalizeProfilePayload(payload);
    setCustomizationEndpointSupported();
    clearLocalCustomizationFallback(normalized.username);
    return normalized;
  } catch (primaryError) {
    try {
      const payload = await requestJson<NetBoxProfilePayload>("/api/profile/me/customization", {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(request),
      });

      const normalized = normalizeProfilePayload(payload);
      setCustomizationEndpointSupported();
      clearLocalCustomizationFallback(normalized.username);
      return normalized;
    } catch (secondaryError) {
      if (!isNotFoundError(primaryError) && !isNotFoundError(secondaryError)) {
        throw primaryError;
      }

      setCustomizationEndpointUnsupported();
      return applyCustomizationLocally(request);
    }
  }
}

async function applyCustomizationLocally(request: UpdateProfileCustomizationRequest): Promise<NetBoxProfile> {
  const current = await getProfile();
  const patched = {
    ...current,
    displayName: request.displayName.trim() || current.displayName,
    motto: request.motto,
    cardStyle: request.cardStyle,
    avatar: request.avatarDataUrl ?? current.avatar,
    customization: {
      ...current.customization,
      displayName: request.displayName.trim() || current.displayName,
      motto: request.motto,
      cardStyle: request.cardStyle,
      avatarDataUrl: request.avatarDataUrl,
    },
  } satisfies NetBoxProfile;

  saveLocalCustomizationFallback(patched.username, patched.customization);
  return patched;
}

function normalizeProfilePayload(payload: NetBoxProfilePayload): NetBoxProfile {
  const username = (payload.username ?? payload.settings?.username ?? "Player").trim() || "Player";
  const rawDisplayName = payload.displayName ?? payload.customization?.displayName ?? payload.gamertag ?? username;
  const displayName = (rawDisplayName ?? username).trim() || username;

  const rawCardStyle = (payload.cardStyle ?? payload.customization?.cardStyle ?? "classic").toLowerCase();
  const cardStyle: "classic" | "emerald" | "sunset" | "midnight" =
    rawCardStyle === "emerald" || rawCardStyle === "sunset" || rawCardStyle === "midnight"
      ? rawCardStyle
      : "classic";

  const motto = payload.motto ?? payload.customization?.motto ?? "";
  const avatarDataUrl = payload.customization?.avatarDataUrl ?? null;
  const avatar = payload.avatar ?? avatarDataUrl;

  return {
    username,
    displayName,
    avatar: avatar ?? null,
    motto,
    cardStyle,
    gamerscore: payload.gamerscore ?? 0,
    recentGames: payload.recentGames ?? [],
    achievements: payload.achievements ?? [],
    settings: {
      userId: payload.settings?.userId ?? 0,
      username,
      email: payload.settings?.email ?? null,
      avatar: payload.settings?.avatar ?? avatar ?? null,
      theme: payload.settings?.theme ?? "Metro",
      controllerPreference: payload.settings?.controllerPreference ?? "Xbox",
      language: payload.settings?.language ?? "en-US",
    },
    customization: {
      displayName,
      motto,
      cardStyle,
      avatarDataUrl,
    },
  };
}

function isNotFoundError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  return /status\s*404/i.test(error.message);
}

function readLocalFallbackStore(): Record<string, NetBoxProfile["customization"]> {
  if (typeof window === "undefined") {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(PROFILE_CUSTOMIZATION_FALLBACK_KEY);
    if (!raw) {
      return {};
    }

    const parsed = JSON.parse(raw) as Record<string, NetBoxProfile["customization"]>;
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

function writeLocalFallbackStore(store: Record<string, NetBoxProfile["customization"]>): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(PROFILE_CUSTOMIZATION_FALLBACK_KEY, JSON.stringify(store));
}

function saveLocalCustomizationFallback(username: string, customization: NetBoxProfile["customization"]): void {
  const key = username.trim().toLowerCase();
  if (!key) {
    return;
  }

  const store = readLocalFallbackStore();
  store[key] = customization;
  writeLocalFallbackStore(store);
}

function clearLocalCustomizationFallback(username: string): void {
  const key = username.trim().toLowerCase();
  if (!key) {
    return;
  }

  const store = readLocalFallbackStore();
  if (!(key in store)) {
    return;
  }

  delete store[key];
  writeLocalFallbackStore(store);
}

function applyLocalCustomizationFallback(profile: NetBoxProfile): NetBoxProfile {
  const key = profile.username.trim().toLowerCase();
  if (!key || typeof window === "undefined") {
    return profile;
  }

  const store = readLocalFallbackStore();
  const fallback = store[key];
  if (!fallback) {
    return profile;
  }

  const displayName = fallback.displayName?.trim() || profile.displayName;

  return {
    ...profile,
    displayName,
    motto: fallback.motto ?? profile.motto,
    cardStyle: fallback.cardStyle ?? profile.cardStyle,
    avatar: fallback.avatarDataUrl ?? profile.avatar,
    customization: {
      ...profile.customization,
      displayName,
      motto: fallback.motto ?? profile.customization.motto,
      cardStyle: fallback.cardStyle ?? profile.customization.cardStyle,
      avatarDataUrl: fallback.avatarDataUrl ?? profile.customization.avatarDataUrl,
    },
  };
}

function getCustomizationEndpointState(): "supported" | "unsupported" | null {
  if (typeof window === "undefined") {
    return null;
  }

  const value = window.localStorage.getItem(PROFILE_CUSTOMIZATION_ENDPOINT_STATE_KEY);
  return value === "supported" || value === "unsupported" ? value : null;
}

function setCustomizationEndpointSupported(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(PROFILE_CUSTOMIZATION_ENDPOINT_STATE_KEY, "supported");
}

function setCustomizationEndpointUnsupported(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(PROFILE_CUSTOMIZATION_ENDPOINT_STATE_KEY, "unsupported");
}

function isCustomizationEndpointUnsupported(): boolean {
  return getCustomizationEndpointState() === "unsupported";
}
