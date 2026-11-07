import type { DecorProps } from '../types';
import { Layers, soft, type Layer } from './Layers';

/** Four soft colour blobs drift behind the glass on a 50s loop, so the colour under the panes keeps shifting. */
const blob = (x: number, y: number, size: number, color: string, opacity: number, drift: [number, number], delay: number): Layer =>
  soft({
    x,
    y,
    w: size,
    h: size,
    blur: 100,
    color,
    style: { aspectRatio: '1', height: 'auto', opacity },
    frames: `0%,100%{transform:translate(0,0) scale(1)}50%{transform:translate(${drift[0]}vw,${drift[1]}vh) scale(1.1)}`,
    timing: `50s ease-in-out ${delay}s infinite`,
  });

const blobs = [
  blob(-120, -160, 620, '#60A5FA', 0.85, [14, 18], 0),
  blob(820, -120, 560, '#F472B6', 0.75, [-16, 22], -12),
  blob(380, 420, 640, '#A78BFA', 0.8, [12, -20], -25),
  blob(1040, 520, 520, '#22D3EE', 0.7, [-18, -14], -37),
];

export default function GlassDecor({ still }: DecorProps) {
  return <Layers name="sb-glass" layers={blobs} still={still} />;
}
