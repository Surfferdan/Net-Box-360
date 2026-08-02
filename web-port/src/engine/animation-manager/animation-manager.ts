type EasingFn = (t: number) => number;

export interface Tween {
  update: (value: number) => void;
  complete?: () => void;
  from: number;
  to: number;
  durationMs: number;
  easing?: EasingFn;
}

interface ActiveTween {
  startedAt: number;
  tween: Tween;
}

const easeOutCubic: EasingFn = (t) => 1 - Math.pow(1 - t, 3);

export class AnimationManager {
  private readonly active: ActiveTween[] = [];

  public tween(tween: Tween): void {
    this.active.push({
      startedAt: performance.now(),
      tween: {
        ...tween,
        easing: tween.easing ?? easeOutCubic,
      },
    });
    tween.update(tween.from);
  }

  public update(now: number): void {
    for (let i = this.active.length - 1; i >= 0; i -= 1) {
      const item = this.active[i];
      const elapsed = now - item.startedAt;
      const rawT = Math.min(1, elapsed / item.tween.durationMs);
      const t = item.tween.easing!(rawT);
      const v = item.tween.from + (item.tween.to - item.tween.from) * t;
      item.tween.update(v);

      if (rawT >= 1) {
        this.active.splice(i, 1);
        item.tween.complete?.();
      }
    }
  }
}
