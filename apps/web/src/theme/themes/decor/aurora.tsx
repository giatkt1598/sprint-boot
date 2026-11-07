import type { DecorProps } from '../types';
import { Layers, soft, type Layer } from './Layers';

/**
 * Three blurred ribbons of light, drawn where the Penpot board has them, swaying on a 40s loop. Each ribbon fades into
 * the colour of the next, and their brightness rises and falls out of step, so the sky slowly shifts hue.
 */
const ribbon = (x: number, y: number, w: number, h: number, angle: number, blur: number, colors: [string, string], sway: [number, number, number], delay: number): Layer =>
  soft({
    x,
    y,
    w,
    h,
    blur,
    color: colors[0],
    style: { transform: `rotate(${angle}deg)` },
    children: (
      <div style={{ position: 'absolute', inset: 0, background: `radial-gradient(closest-side at 70% 50%, ${colors[1]}, transparent)` }} />
    ),
    frames: `0%,100%{transform:rotate(${angle}deg) translate(0,0);opacity:1}50%{transform:rotate(${angle + sway[0]}deg) translate(${sway[1]}%,${sway[2]}%);opacity:.65}`,
    timing: `40s ease-in-out ${delay}s infinite`,
  });

const ribbons = [
  ribbon(-120, 80, 1100, 260, -12, 90, ['rgb(45 212 191 / 0.825)', 'rgb(34 197 94 / 0.35)'], [5, 8, 18], 0),
  ribbon(520, 220, 1100, 300, 10, 110, ['rgb(167 139 250 / 0.675)', 'rgb(236 72 153 / 0.3)'], [-6, -10, -12], -14),
  ribbon(120, 560, 1300, 240, -6, 120, ['rgb(74 222 128 / 0.45)', 'rgb(20 184 166 / 0.3)'], [4, 6, -16], -27),
];

export default function AuroraDecor({ still }: DecorProps) {
  return <Layers name="sb-aurora" layers={ribbons} still={still} />;
}
