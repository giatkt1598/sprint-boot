import type { DecorProps } from '../types';
import { Layers, seeded, type Layer } from './Layers';

/** Four blurred flame tongues rise from the bottom edge and flicker out of step. The wide glow is the page itself. */
const flame = (x: number, y: number, w: number, seconds: number, delay: number): Layer => ({
  // Grown by its 60px blur, which the gradient draws as a soft edge. The gradient is centred on the bottom edge, so
  // each flame is a tongue standing on the bottom of the window: hot at the base, fading out towards the tip.
  x: x - 60,
  y: y - 60,
  w: w + 120,
  h: 900 - (y - 60),
  style: {
    background:
      'radial-gradient(farthest-side at 50% 100%, rgb(253 186 116 / 0.85) 0%, rgb(251 146 60 / 0.7) 30%, rgb(234 88 12 / 0.35) 65%, transparent 100%)',
    transformOrigin: '50% 100%',
  },
  frames: '0%,100%{transform:scale(1,1);opacity:1}30%{transform:scale(1.04,1.12);opacity:.8}60%{transform:scale(.97,.94);opacity:.95}80%{transform:scale(1.02,1.06);opacity:.75}',
  timing: `${seconds}s ease-in-out ${delay}s infinite`,
});

// The tops of the four flames on the Penpot board; each one stands on the bottom edge.
const flames = [flame(180, 540, 260, 2.6, 0), flame(560, 480, 320, 3.4, -1.2), flame(980, 520, 280, 2.2, -0.7), flame(1260, 560, 220, 3.9, -2.1)];

/** Embers float upward and fade. */
const random = seeded(9);
const embers: Layer[] = Array.from({ length: 26 }, () => {
  const size = (2 + random() * 4) * 1.4;
  const x = 230 + random() * 1200;
  const y = 120 + random() * 760;
  const color = random() > 0.5 ? '#FDBA74' : '#FDE047';
  const seconds = 6 + random() * 8;
  return {
    x,
    y,
    w: size,
    h: size,
    style: { width: size, height: size, borderRadius: '50%', background: `radial-gradient(closest-side, ${color} 60%, transparent)`, opacity: 0 },
    frames: `0%{transform:translate(0,0);opacity:0}10%{opacity:1}100%{transform:translate(${(random() * 60 - 30).toFixed(0)}px,-45vh);opacity:0}`,
    timing: `${seconds.toFixed(1)}s ease-out ${(-random() * seconds).toFixed(1)}s infinite`,
  };
});

export default function FireDecor({ still }: DecorProps) {
  // Reduced motion keeps a steady glow with no embers.
  return <Layers name="sb-fire" layers={still ? flames : [...flames, ...embers]} still={still} />;
}
