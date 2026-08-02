import { requestJson } from "./NetBoxClient";
import type { NetBoxProfile } from "./ProfileService";
import type { Game } from "./types";

interface XeniaGameCatalogItem {
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

interface XeniaLaunchResponse {
  isRunning: boolean;
  processId: number | null;
  executablePath: string | null;
}

const FALLBACK_COVERS = [
  "/assets/Assets/Tiles/halo4home.jpg",
  "/assets/Assets/Tiles/forzahorizongames.jpg",
  "/assets/Assets/Tiles/minecraftgames.jpg",
  "/assets/Assets/Tiles/blackops2games.jpg",
  "/assets/Assets/Tiles/kungfupanda2video.jpg",
];

function normalizeGameName(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

function pickCover(index: number): string {
  return FALLBACK_COVERS[index % FALLBACK_COVERS.length];
}

function resolveCatalogTitle(entry: XeniaGameCatalogItem): string {
  return entry.title?.trim() || entry.name.trim();
}

function normalizeCoverPath(path: string | null | undefined, index: number): string {
  const value = path?.trim();
  if (!value) {
    return pickCover(index);
  }

  if (/^https?:\/\//i.test(value) || value.startsWith("data:")) {
    return value;
  }

  if (value.startsWith("/")) {
    return encodeURI(value);
  }

  return encodeURI(`/${value.replace(/^\/+/, "")}`);
}

async function refreshCatalog(): Promise<XeniaGameCatalogItem[]> {
  return requestJson<XeniaGameCatalogItem[]>("/api/games/refresh", { method: "POST" });
}

export async function getLaunchableGames(profile: NetBoxProfile | null): Promise<Game[]> {
  let catalog: XeniaGameCatalogItem[];

  try {
    catalog = await refreshCatalog();
  } catch {
    catalog = await requestJson<XeniaGameCatalogItem[]>("/api/games", { method: "GET" });
  }

  const recent = new Set((profile?.recentGames ?? []).map(normalizeGameName));

  return catalog.map((entry, index) => {
    const title = resolveCatalogTitle(entry);
    const normalized = normalizeGameName(title);
    const recentlyPlayed = recent.has(normalized);
    const descriptor = entry.genre?.trim() || entry.extension.toUpperCase().slice(1);
    const playerLabel = entry.players && entry.players > 1 ? ` - ${entry.players} Players` : "";
    const subtitle = recentlyPlayed
      ? `Recently played - ${descriptor}${playerLabel}`
      : `Ready to launch - ${descriptor}${playerLabel}`;

    return {
      id: entry.id,
      title,
      subtitle,
      coverPath: normalizeCoverPath(entry.coverPath, index),
      launchPath: entry.fullPath,
    };
  });
}

export async function launchGameWithXenia(game: Game): Promise<XeniaLaunchResponse> {
  if (!game.launchPath) {
    throw new Error(`Selected game ${game.title} has no launch path.`);
  }

  const escapedPath = game.launchPath.replace(/"/g, '\\"');
  return requestJson<XeniaLaunchResponse>("/api/xenia/start", {
    method: "POST",
    body: JSON.stringify({
      executablePath: null,
      workingDirectory: null,
      arguments: `\"${escapedPath}\"`,
    }),
  });
}