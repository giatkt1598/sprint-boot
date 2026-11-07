import type { DecorProps } from '../types';
import { Layers, soft, type Layer } from './Layers';

/** The sun glow sinks and rises on a 60s loop; soft clouds drift sideways. Placed as on the Penpot board. */
const sun: Layer = {
  // The glow is a radial gradient already; its 80px blur only widens it.
  x: 900,
  y: -300,
  w: 680,
  h: 680,
  style: {
    aspectRatio: '1',
    height: 'auto',
    opacity: 0.85,
    background: 'radial-gradient(closest-side, rgb(255 224 138) 0%, rgb(255 154 90 / 0.7) 45%, rgb(255 122 144 / 0) 100%)',
  },
  frames: '0%,100%{transform:translateY(0)}50%{transform:translateY(22vh)}',
  timing: '60s ease-in-out infinite',
};

const cloud = (x: number, y: number, w: number, h: number, drift: number, seconds: number): Layer =>
  soft({
    x,
    y,
    w,
    h,
    blur: 40,
    color: 'rgb(255 255 255 / 0.55)',
    frames: `0%,100%{transform:translateX(0)}50%{transform:translateX(${drift}vw)}`,
    timing: `${seconds}s ease-in-out infinite`,
  });

const layers = [sun, cloud(120, 120, 420, 90, 6, 48), cloud(620, 300, 520, 110, -5, 62), cloud(60, 620, 600, 120, 4, 54)];

export default function SunsetDecor({ still }: DecorProps) {
  return <Layers name="sb-sunset" layers={layers} still={still} />;
}
