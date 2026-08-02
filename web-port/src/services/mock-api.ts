import type { ActivityItem, Friend, Game, Profile } from "./types";

const MOCK_PROFILE: Profile = {
  gamertag: "PlayerOne",
  gamerscore: 18320,
  friendCount: 42,
  avatarPath: "/assets/Assets/Profile/profilepicture.jpg",
};

const MOCK_FRIENDS: Friend[] = [
  {
    id: "f-1",
    gamertag: "Arbiter VII",
    subtitle: "Online",
    status: "Playing Halo Reach",
    avatarPath: "/assets/Assets/Profile/FriendPool/20002.png",
  },
  {
    id: "f-2",
    gamertag: "Nova Drift",
    subtitle: "Away",
    status: "Last seen 21m ago",
    avatarPath: "/assets/Assets/Profile/FriendPool/20003.png",
  },
  {
    id: "f-3",
    gamertag: "MetroGhost",
    subtitle: "Online",
    status: "Watching The Dark Knight",
    avatarPath: "/assets/Assets/Profile/FriendPool/20006.png",
  },
];

const MOCK_GAMES: Game[] = [
  { id: "halo4", title: "Halo 4", subtitle: "Campaign", coverPath: "/assets/Assets/Tiles/halo4home.jpg" },
  { id: "forza", title: "Forza Horizon", subtitle: "Racing", coverPath: "/assets/Assets/Tiles/forzahorizongames.jpg" },
  { id: "minecraft", title: "Minecraft", subtitle: "Sandbox", coverPath: "/assets/Assets/Tiles/minecraftgames.jpg" },
  { id: "blackops", title: "Black Ops II", subtitle: "Shooter", coverPath: "/assets/Assets/Tiles/blackops2games.jpg" },
  { id: "panda", title: "Kung Fu Panda 2", subtitle: "Video", coverPath: "/assets/Assets/Tiles/kungfupanda2video.jpg" },
];

const MOCK_ACTIVITY: ActivityItem[] = [
  { id: "a1", text: "3 friends are online" },
  { id: "a2", text: "2 new marketplace offers" },
  { id: "a3", text: "Welcome back to dashX360" },
];

const delay = async (ms: number): Promise<void> => {
  await new Promise((resolve) => window.setTimeout(resolve, ms));
};

export async function getProfile(): Promise<Profile> {
  await delay(60);
  return { ...MOCK_PROFILE };
}

export async function getFriends(): Promise<Friend[]> {
  await delay(90);
  return [...MOCK_FRIENDS];
}

export async function getGameLibrary(): Promise<Game[]> {
  await delay(100);
  return [...MOCK_GAMES];
}

export async function getActivity(): Promise<ActivityItem[]> {
  await delay(80);
  return [...MOCK_ACTIVITY];
}

export async function launchGame(gameId: string): Promise<{ ok: boolean; message: string }> {
  await delay(150);
  return { ok: true, message: `launchGame placeholder invoked for ${gameId}` };
}
