import petal from '../art/japanese-petal.svg';
import type { DecorProps } from '../types';
import { Layers, seeded, type Layer } from './Layers';

const image = (size: number) => <img src={petal} width={size} height={size} alt="" draggable={false} />;

/**
 * Sakura petals fall and turn slowly across the page: a new one about every two seconds, never more than ten on screen.
 * Each one drifts sideways as it falls and fades out near the bottom.
 */
const random = seeded(21);
const falling: Layer[] = Array.from({ length: 10 }, (_, index) => {
  const size = 14 + random() * 10;
  const drift = (random() - 0.5) * 240;
  const spin = 240 + random() * 360;
  return {
    x: 80 + random() * 1300,
    y: -30,
    w: size,
    h: size,
    style: { width: size, height: size, opacity: 0 },
    frames: `0%{transform:translate(0,0) rotate(0deg);opacity:0}5%{opacity:.9}50%{transform:translate(${(drift * 0.6).toFixed(0)}px,55vh) rotate(${(spin / 2).toFixed(0)}deg)}90%{opacity:.8}100%{transform:translate(${drift.toFixed(0)}px,105vh) rotate(${spin.toFixed(0)}deg);opacity:0}`,
    timing: `20s linear ${-index * 2}s infinite`,
    children: image(size),
  };
});

/** Reduced motion shows a still scatter of petals, as on the Penpot board. */
const scattered: Layer[] = Array.from({ length: 12 }, () => {
  const size = 14 + random() * 10;
  return {
    x: random() * 1400,
    y: 60 + random() * 820,
    w: size,
    h: size,
    style: { width: size, height: size, opacity: 0.6 + random() * 0.4, transform: `rotate(${(random() * 360).toFixed(0)}deg)` },
    children: image(size),
  };
});

export default function JapaneseDecor({ still }: DecorProps) {
  return <Layers name="sb-japanese" layers={still ? scattered : falling} still={still} />;
}
