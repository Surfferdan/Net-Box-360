// Shapes mirror the EXISTING, already-working XeniaManager.Api DTOs
// exactly (see `xenia api/NetBox.Models/GameSessionContracts.cs`,
// `AccountContracts.cs`, `ProfileContracts.cs`, and
// `XeniaManager.Api/Controllers/GamesController.cs`'s GameCatalogItemDto).
// This file does not invent any new wire contract - it just gives the
// legacy JSON shapes TypeScript types so the rest of this adapter package
// can consume them safely.

export interface LegacyStartSessionResponse {
  sessionId: string;
  game: string;
  streamUrl: string;
  status: string;
  controllerStatus: string;
  canStopSession: boolean;
  assignedControllerSlot: number;
}

export interface LegacySessionStatusResponse {
  sessionId: string;
  status: string;
  game: string;
  players: number;
  canStopSession: boolean;
  streamUrl: string | null;
  cloudMorphSessionId: string | null;
  error: string | null;
  streamHealth: string;
  assignedControllerSlot: number;
  occupiedControllerSlots: number[];
}

export interface LegacyStopSessionResponse {
  success: boolean;
  status: string;
}

export interface LegacyLeaveSessionResponse {
  success: boolean;
  status: string;
  sessionId: string;
  playersRemaining: number;
}

export interface LegacyJoinSessionResponse {
  sessionId: string;
  game: string;
  streamUrl: string | null;
  controllerStatus: string;
  assignedControllerSlot: number;
}

export interface LegacyLoginResponse {
  token: string;
  userId: number;
  username: string;
}

export interface LegacyCreateAccountResponse {
  token: string;
  userId: number;
  username: string;
}

export interface LegacyProfileResponse {
  userId: number;
  username: string;
  displayName: string;
  [key: string]: unknown;
}

export interface LegacyGameCatalogItemDto {
  id: string;
  name: string;
  titleId: string;
  title: string;
  relativePath: string;
  fullPath: string;
  extension: string;
  sizeBytes: number;
  genre: string | null;
  players: number | null;
  lastWriteTimeUtc: string;
  coverPath: string | null;
}
