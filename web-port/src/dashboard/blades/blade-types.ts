export interface TileSpec {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
  title?: string;
  subtitle?: string;
  image?: string;
  background?: string;
}

export interface BladeSpec {
  key: string;
  label: string;
  frameLeft: number;
  frameTop: number;
  frameWidth: number;
  frameHeight: number;
  tiles: TileSpec[];
}
