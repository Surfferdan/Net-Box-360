export interface Profile {
  gamertag: string;
  gamerscore: number;
  friendCount: number;
  avatarPath: string;
}

export interface Friend {
  id: string;
  gamertag: string;
  subtitle: string;
  status: string;
  avatarPath: string;
  activeSessionId?: string | null;
  activeGameTitle?: string | null;
}

export interface Game {
  id: string;
  title: string;
  subtitle: string;
  coverPath: string;
  launchPath?: string;
}

export interface ActivityItem {
  id: string;
  text: string;
}

export interface ChatMessage {
  id: string;
  fromGamertag: string;
  toGamertag: string | null;
  message: string;
  sentAtUtc: string;
  isMine: boolean;
}
