import * as THREE from "three";

export class RendererManager {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly mount: HTMLElement;

  public constructor(mount: HTMLElement) {
    this.mount = mount;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.domElement.className = "dash-canvas";
    this.mount.appendChild(this.renderer.domElement);
  }

  public render(scene: THREE.Scene, camera: THREE.PerspectiveCamera): void {
    this.renderer.render(scene, camera);
  }

  public resize(width: number, height: number): void {
    this.renderer.setSize(width, height);
  }

  public get domElement(): HTMLCanvasElement {
    return this.renderer.domElement;
  }
}
