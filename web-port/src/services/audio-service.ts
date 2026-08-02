const SOUND_ROOT = "/assets/Assets/Audio/Sounds";

const SOUND_FILES: Record<string, string[]> = {
  startup: ["startup-after-loading.wav", "02. Startup (2010).mp3"],
  "notify-popup": ["notify-popup.wav"],
  "page-left": ["08. Page Left.mp3", "09. Page Right.mp3"],
  "page-right": ["09. Page Right.mp3", "08. Page Left.mp3"],
  tab: ["09. Page Right.mp3"],
  activate: ["10. Select A.mp3", "13. Select.mp3", "select.wav"],
  select: ["10. Select A.mp3", "13. Select.mp3", "select.wav"],
  back: ["14. Back.mp3", "15. Back 2.mp3", "back.wav"],
  focus: ["tile-hover.wav", "13. Select.mp3", "11. Select A (Alt).mp3", "focus.wav"],
  "guide-open": ["hud-open.wav", "10. Select A.mp3"],
  "guide-close": ["hud-close.wav", "14. Back.mp3"],
  "guide-blade-open": ["blade-open.wav", "hud-open.wav"],
  "guide-blade-switch-1": ["blade-switch-1.wav", "09. Page Right.mp3"],
  "guide-blade-switch-2": ["blade-switch-2.wav", "09. Page Right.mp3"],
  "guide-blade-switch-3": ["blade-switch-3.wav", "09. Page Right.mp3"],
  "guide-blade-switch-4": ["blade-switch-4.wav", "09. Page Right.mp3"],
  "guide-hover": ["guide-hover.wav", "13. Select.mp3"],
  "guide-select": ["guide-select.wav", "10. Select A.mp3"],
  "guide-back": ["guide-back.wav", "14. Back.mp3"],
};

interface WindowWithWebkitAudioContext extends Window {
  webkitAudioContext?: typeof AudioContext;
}

export class WebAudioService {
  private context: AudioContext | null = null;
  private readonly buffers = new Map<string, AudioBuffer>();
  private readonly pendingLoads = new Map<string, Promise<AudioBuffer | null>>();

  public async play(soundName: string, volume = 0.75): Promise<void> {
    const ctx = this.getContext();
    if (!ctx) {
      return;
    }

    await ctx.resume();
    const buffer = await this.resolveBuffer(soundName);
    if (!buffer) {
      return;
    }

    const source = ctx.createBufferSource();
    source.buffer = buffer;

    const gain = ctx.createGain();
    gain.gain.value = volume;

    source.connect(gain);
    gain.connect(ctx.destination);
    source.start();
  }

  public async preload(soundName: string): Promise<void> {
    await this.resolveBuffer(soundName);
  }

  private async resolveBuffer(soundName: string): Promise<AudioBuffer | null> {
    const key = soundName.toLowerCase();
    const cached = this.buffers.get(key);
    if (cached) {
      return cached;
    }

    const pending = this.pendingLoads.get(key);
    if (pending) {
      return pending;
    }

    const loadPromise = this.loadBuffer(soundName);
    this.pendingLoads.set(key, loadPromise);

    try {
      const buffer = await loadPromise;
      if (buffer) {
        this.buffers.set(key, buffer);
      }
      return buffer;
    } finally {
      this.pendingLoads.delete(key);
    }
  }

  private async loadBuffer(soundName: string): Promise<AudioBuffer | null> {
    const ctx = this.getContext();
    if (!ctx) {
      return null;
    }

    const candidates = SOUND_FILES[soundName.toLowerCase()] ?? [`${soundName}.mp3`, `${soundName}.wav`];

    for (const fileName of candidates) {
      const url = `${SOUND_ROOT}/${encodeURIComponent(fileName)}`;

      try {
        const response = await fetch(url);
        if (!response.ok) {
          continue;
        }

        const arrayBuffer = await response.arrayBuffer();
        return await ctx.decodeAudioData(arrayBuffer.slice(0));
      } catch {
        continue;
      }
    }

    return null;
  }

  private getContext(): AudioContext | null {
    if (typeof window === "undefined") {
      return null;
    }

    if (!this.context) {
      const AudioContextCtor = window.AudioContext ?? (window as WindowWithWebkitAudioContext).webkitAudioContext;
      if (!AudioContextCtor) {
        return null;
      }
      this.context = new AudioContextCtor();
    }

    return this.context;
  }
}
