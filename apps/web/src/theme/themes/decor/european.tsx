import type { DecorProps } from '../types';
import { Layers, soft, type Layer } from './Layers';

/**
 * Five soft patches of ruby, sapphire, gold, emerald and amethyst light drift across the page on a 90s loop, as if the
 * sun moved behind a rose window. Placed as on the Penpot board.
 */
const light = (x: number, y: number, size: number, color: string, drift: [number, number], delay: number): Layer =>
  soft({
    x,
    y,
    w: size,
    h: size,
    blur: 70,
    color,
    style: { aspectRatio: '1', height: 'auto' },
    frames: `0%,100%{transform:translate(0,0)}50%{transform:translate(${drift[0]}vw,${drift[1]}vh)}`,
    timing: `90s ease-in-out ${delay}s infinite`,
  });

const layers = [
  light(300, 160, 260, 'rgb(185 28 28 / 0.35)', [8, 6], 0),
  light(520, 300, 300, 'rgb(29 78 216 / 0.31)', [6, -5], -18),
  light(760, 140, 240, 'rgb(212 160 23 / 0.4)', [-7, 8], -36),
  light(380, 520, 280, 'rgb(4 120 87 / 0.26)', [9, -6], -54),
  light(820, 520, 260, 'rgb(124 58 237 / 0.26)', [-8, -4], -72),
];

export default function EuropeanDecor({ still }: DecorProps) {
  return <Layers name="sb-european" layers={layers} still={still} />;
}
