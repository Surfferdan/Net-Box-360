import { RendererManager } from "./engine/renderer/renderer-manager";
import { SceneManager } from "./engine/scene-manager/scene-manager";
import { AnimationManager } from "./engine/animation-manager/animation-manager";
import { InputManager } from "./engine/input-manager/input-manager";
import { DashboardApp } from "./dashboard/dashboard-app";
import "./styles.css";

const appRoot = document.querySelector<HTMLDivElement>("#app");
if (!appRoot) {
  throw new Error("#app root missing");
}

const worldLayer = document.createElement("div");
worldLayer.className = "world-layer";
appRoot.appendChild(worldLayer);

const uiLayer = document.createElement("div");
uiLayer.className = "ui-layer";
appRoot.appendChild(uiLayer);

const rendererManager = new RendererManager(worldLayer);
const sceneManager = new SceneManager();
const animationManager = new AnimationManager();
const inputManager = new InputManager();

new DashboardApp(uiLayer, animationManager, inputManager);
inputManager.start();

const DASH_BASE_WIDTH = 1280;
const DASH_BASE_HEIGHT = 720;

const onResize = (): void => {
  const scale = Math.min(window.innerWidth / DASH_BASE_WIDTH, window.innerHeight / DASH_BASE_HEIGHT);
  uiLayer.style.setProperty("--dash-scale", String(scale));
  rendererManager.resize(window.innerWidth, window.innerHeight);
  sceneManager.resize(window.innerWidth, window.innerHeight);
};

window.addEventListener("resize", onResize);

const frame = (now: number): void => {
  const seconds = now * 0.001;
  sceneManager.update(seconds);
  animationManager.update(now);
  rendererManager.render(sceneManager.scene, sceneManager.camera);
  requestAnimationFrame(frame);
};

requestAnimationFrame(frame);
