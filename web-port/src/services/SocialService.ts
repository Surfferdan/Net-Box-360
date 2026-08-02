import { getSessionToken, requestJson } from "./NetBoxClient";
import type { ActivityItem, ChatMessage, Friend } from "./types";

interface SocialFeedResponse {
  friends: Friend[];
  activity: ActivityItem[];
}

interface FriendMutationResponse {
  success: boolean;
  error?: string;
}

export async function getSocialFeed(): Promise<SocialFeedResponse> {
  const token = getSessionToken();
  return requestJson<SocialFeedResponse>("/api/netbox/social/feed", {
    method: "GET",
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
}

export async function getChatMessages(limit = 50): Promise<ChatMessage[]> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  return requestJson<ChatMessage[]>(`/api/netbox/social/chat?limit=${encodeURIComponent(String(limit))}`, {
    method: "GET",
    headers: { Authorization: `Bearer ${token}` },
  });
}

export async function sendChatMessage(message: string, recipientUserId?: string | null): Promise<ChatMessage> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  return requestJson<ChatMessage>("/api/netbox/social/chat", {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: JSON.stringify({
      message,
      recipientUserId: recipientUserId ?? null,
    }),
  });
}

export async function addFriendByUsername(username: string): Promise<void> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  const payload = await requestJson<FriendMutationResponse>("/api/netbox/social/friends", {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: JSON.stringify({ username }),
  });

  if (!payload.success) {
    throw new Error(payload.error || "Could not add friend.");
  }
}

export async function removeFriend(friendUserId: string): Promise<void> {
  const token = getSessionToken();
  if (!token) {
    throw new Error("No Net Box session token available.");
  }

  const payload = await requestJson<FriendMutationResponse>(`/api/netbox/social/friends/${encodeURIComponent(friendUserId)}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!payload.success) {
    throw new Error(payload.error || "Could not remove friend.");
  }
}