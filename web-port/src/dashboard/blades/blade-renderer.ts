import type { BladeSpec, TileSpec } from "./blade-types";

const XBOX_GREEN_CORE = "#028d02";
const XBOX_GREEN_HIGHLIGHT = "#3fbd3f";
const XBOX_GREEN_SHADOW = "#016f01";

export interface TileElement {
  tile: TileSpec;
  element: HTMLButtonElement;
  centerX: number;
  centerY: number;
}

export function buildBladeLayer(blade: BladeSpec): {
  root: HTMLDivElement;
  tileElements: TileElement[];
} {
  const root = document.createElement("div");
  root.className = "blade-layer";
  root.style.width = "1280px";
  root.style.height = "502px";

  const frame = document.createElement("div");
  frame.className = "blade-frame";
  frame.style.left = `${blade.frameLeft}px`;
  frame.style.top = `${blade.frameTop}px`;
  frame.style.width = `${blade.frameWidth}px`;
  frame.style.height = `${blade.frameHeight}px`;

  const tileElements: TileElement[] = blade.tiles.map((tile) => {
    const button = document.createElement("button");
    button.className = "dash-tile";
    button.dataset.tileId = tile.id;
    button.style.left = `${tile.x}px`;
    button.style.top = `${tile.y}px`;
    button.style.width = `${tile.w}px`;
    button.style.height = `${tile.h}px`;

    if (tile.background) {
      button.style.background = tile.background;
    }

    const isDarkGreenTile = isDarkGreenTileBackground(tile.background);
    const darkGreenBase = `linear-gradient(180deg, ${XBOX_GREEN_HIGHLIGHT} 0%, ${XBOX_GREEN_CORE} 52%, ${XBOX_GREEN_SHADOW} 100%)`;
    if (isDarkGreenTile) {
      // Force the dark-green tile group to the requested Xbox green palette.
      button.style.backgroundImage = darkGreenBase;
      button.style.backgroundSize = "cover";
      button.style.backgroundPosition = "center";
      button.style.backgroundRepeat = "no-repeat";
    }

    if (tile.image) {
      const imageShade = isDarkGreenTile
        ? "linear-gradient(to top, rgba(0, 90, 0, 0.12), rgba(255, 255, 255, 0.04))"
        : "linear-gradient(to top, rgba(0,0,0,0.55), rgba(0,0,0,0.06))";
      button.style.backgroundImage = isDarkGreenTile
        ? `${imageShade}, url('${tile.image}'), ${darkGreenBase}`
        : `${imageShade}, url('${tile.image}')`;
      button.style.backgroundSize = isDarkGreenTile ? "cover, cover, cover" : "cover, cover";
      button.style.backgroundPosition = isDarkGreenTile ? "center, center, center" : "center, center";
      button.style.backgroundRepeat = isDarkGreenTile ? "no-repeat, no-repeat, no-repeat" : "no-repeat, no-repeat";
    }

    const label = document.createElement("div");
    label.className = "dash-tile-label";
    label.textContent = tile.title ?? "";
    button.appendChild(label);

    if (tile.subtitle) {
      const sub = document.createElement("div");
      sub.className = "dash-tile-subtitle";
      sub.textContent = tile.subtitle;
      button.appendChild(sub);
    }

    frame.appendChild(button);

    return {
      tile,
      element: button,
      centerX: blade.frameLeft + tile.x + tile.w / 2,
      centerY: blade.frameTop + tile.y + tile.h / 2,
    };
  });

  root.appendChild(frame);
  return { root, tileElements };
}

function isDarkGreenTileBackground(background?: string): boolean {
  if (!background || !background.startsWith("#") || background.length !== 7) {
    return false;
  }

  const red = parseInt(background.slice(1, 3), 16);
  const green = parseInt(background.slice(3, 5), 16);
  const blue = parseInt(background.slice(5, 7), 16);

  if (Number.isNaN(red) || Number.isNaN(green) || Number.isNaN(blue)) {
    return false;
  }

  // Treat tiles as green-tinted when green is clearly dominant, excluding near-grayscale colors.
  return green >= red + 10 && green >= blue + 10;
}
