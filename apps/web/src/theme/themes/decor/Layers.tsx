import type { CSSProperties, ReactNode } from 'react';

/** Decors are drawn on the 1440 × 900 frame of the Penpot designs and stretch with the window. */
const FRAME = { width: 1440, height: 900 };

export interface Layer {
  /** Position and size on the design frame, in pixels. */
  x: number;
  y: number;
  w: number;
  h: number;
  style?: CSSProperties;
  /** Keyframes body. They replace the transform of `style`, so they repeat it where it matters. */
  frames?: string;
  /** Duration, easing, delay and count of the animation, as in the animation shorthand. */
  timing?: string;
  children?: ReactNode;
}

/**
 * A blurred shape as a gradient instead of a CSS blur filter. A filter is redrawn on every frame in software rendering,
 * which took the Board to 8 fps; a gradient is drawn once and then only moved. The layer grows by the blur on every
 * side, as the blurred shape did, and the colour fades out from the edge of the original shape to the new one.
 */
export const soft = (layer: Omit<Layer, 'x' | 'y' | 'w' | 'h'> & { x: number; y: number; w: number; h: number; blur: number; color: string }): Layer => {
  const { blur, color, x, y, w, h, style, ...rest } = layer;
  const solid = Math.max(0, Math.round(((Math.min(w, h) / 2 - blur) / (Math.min(w, h) / 2 + blur)) * 100));
  return {
    ...rest,
    x: x - blur,
    y: y - blur,
    w: w + blur * 2,
    h: h + blur * 2,
    style: { background: `radial-gradient(closest-side, ${color} ${solid}%, transparent 100%)`, ...style },
  };
};

/** A seeded random sequence, so particles land in the same places on every visit and in the design. */
export const seeded = (seed: number) => {
  let value = seed;
  return () => (value = (value * 9301 + 49297) % 233280) / 233280;
};

/**
 * Absolutely placed layers of a decor. Only transform and opacity animate, so the browser moves cached layers instead
 * of repainting blurred shapes. A still decor drops every animation and keeps the first frame.
 */
export function Layers({ name, layers, still }: { name: string; layers: Layer[]; still: boolean }) {
  const css = still ? '' : layers.map((layer, index) => (layer.frames ? `@keyframes ${name}-${index}{${layer.frames}}` : '')).join('');

  return (
    <>
      {css && <style>{css}</style>}
      {layers.map((layer, index) => {
        const animated = !still && layer.frames;
        return (
          <div
            key={index}
            style={{
              position: 'absolute',
              left: `${(layer.x / FRAME.width) * 100}%`,
              top: `${(layer.y / FRAME.height) * 100}%`,
              width: `${(layer.w / FRAME.width) * 100}%`,
              height: `${(layer.h / FRAME.height) * 100}%`,
              ...layer.style,
              animation: animated ? `${name}-${index} ${layer.timing}` : undefined,
              willChange: animated ? 'transform, opacity' : undefined,
            }}
          >
            {layer.children}
          </div>
        );
      })}
    </>
  );
}
