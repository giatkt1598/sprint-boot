import type { DecorProps } from '../types';
import { Layers, seeded, type Layer } from './Layers';

/** A diagonal sunbeam over the desk, as on the Penpot board. */
const sunbeam: Layer = {
  // Its 60px blur is drawn as soft edges of the gradient, so the beam costs nothing to keep on screen.
  x: 440,
  y: -200,
  w: 500,
  h: 1400,
  style: {
    background: 'linear-gradient(90deg, transparent 0%, rgb(255 243 214 / 0.22) 24%, rgb(255 243 214 / 0.22) 76%, transparent 100%)',
    transform: 'rotate(28deg)',
  },
};

/** Dust motes float slowly inside the beam. */
const random = seeded(5);
const motes: Layer[] = Array.from({ length: 14 }, (_, index) => {
  const size = 2 + random() * 4;
  const x = 420 + random() * 520;
  const y = 80 + random() * 760;
  const opacity = 0.5 + random() * 0.4;
  const seconds = 14 + random() * 12;
  return {
    x,
    y,
    w: size,
    h: size,
    style: { width: size, height: size, borderRadius: '50%', background: '#FFF3D6', opacity },
    frames: `0%,100%{transform:translate(0,0);opacity:${opacity}}50%{transform:translate(${index % 2 ? 24 : -18}px,-36px);opacity:${(opacity * 0.4).toFixed(2)}}`,
    timing: `${seconds.toFixed(1)}s ease-in-out ${(-random() * seconds).toFixed(1)}s infinite`,
  };
});

/** Now and then a leaf drifts down across the board: it falls in the first part of a long loop and waits out the rest. */
const leaf = (x: number, color: string, seconds: number, delay: number, sway: number): Layer => ({
  x,
  y: -40,
  w: 20,
  h: 16,
  style: { width: 20, height: 16, opacity: 0 },
  frames: `0%{transform:translate(0,0) rotate(0deg);opacity:0}2%{opacity:.9}20%{transform:translate(${sway}px,55vh) rotate(200deg)}38%{transform:translate(${-sway / 2}px,110vh) rotate(420deg);opacity:.9}40%,100%{transform:translate(0,110vh);opacity:0}`,
  timing: `${seconds}s linear ${delay}s infinite`,
  children: (
    <svg width="20" height="16" viewBox="0 -8 20 16">
      <path d="M0 0 C 6 -8, 16 -8, 20 0 C 16 8, 6 8, 0 0 Z" fill={color} />
    </svg>
  ),
});

const leaves = [leaf(760, '#C2410C', 34, -4, 120), leaf(1120, '#D97706', 46, -26, -90), leaf(420, '#8B4513', 58, -41, 80)];

export default function WoodDecor({ still }: DecorProps) {
  // Reduced motion shows the sunbeam only.
  return <Layers name="sb-wood" layers={still ? [sunbeam] : [sunbeam, ...motes, ...leaves]} still={still} />;
}
