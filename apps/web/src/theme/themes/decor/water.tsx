import type { DecorProps } from '../types';
import { Layers, seeded, type Layer } from './Layers';

const random = seeded(3);

/** Caustic rings near the surface, split in two sets that shimmer out of step. */
const rings = Array.from({ length: 28 }, () => {
  const x = random() * 1440;
  const y = random() * 420;
  const rx = 40 + random() * 90;
  const ry = 14 + random() * 30;
  const opacity = 0.25 + random() * 0.3;
  const width = 1.5 + random() * 2;
  const angle = -20 + random() * 40;
  return { x, y, rx, ry, opacity, width, angle };
});

const caustics = (set: number, delay: number): Layer => ({
  x: 0,
  y: 0,
  w: 1440,
  h: 420,
  frames: '0%,100%{opacity:1;transform:scale(1)}50%{opacity:.35;transform:scale(1.03)}',
  timing: `7s ease-in-out ${delay}s infinite`,
  children: (
    <svg width="100%" height="100%" viewBox="0 0 1440 420" preserveAspectRatio="xMidYMid slice">
      {rings
        .filter((_, index) => index % 2 === set)
        .map((ring, index) => (
          <ellipse
            key={index}
            cx={ring.x}
            cy={ring.y}
            rx={ring.rx}
            ry={ring.ry}
            fill="none"
            stroke="#FFFFFF"
            strokeOpacity={ring.opacity}
            strokeWidth={ring.width}
            transform={`rotate(${ring.angle} ${ring.x} ${ring.y})`}
          />
        ))}
    </svg>
  ),
});

/** One wave of 240px repeats across a layer one wave wider than the window, which slides by exactly one wave. */
const wave = (crest: number, amplitude: number, color: string, opacity: number, seconds: number): Layer => {
  const top = crest - amplitude;
  const height = 900 - top;
  const tile = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="${height}" viewBox="0 0 240 ${height}" preserveAspectRatio="none"><path d="M0 ${amplitude} q 60 ${-amplitude} 120 0 t 120 0 L240 ${height} L0 ${height} Z" fill="${color}" fill-opacity="${opacity}"/></svg>`;
  return {
    x: 0,
    y: top,
    w: 1440,
    h: height,
    style: { width: 'calc(100% + 240px)', background: `url("data:image/svg+xml,${encodeURIComponent(tile)}") 0 0 / 240px 100% repeat-x` },
    frames: '0%{transform:translateX(0)}100%{transform:translateX(-240px)}',
    timing: `${seconds}s linear infinite`,
  };
};

/** Bubbles rise slowly from the deep part and fade before the surface. */
const bubbles: Layer[] = Array.from({ length: 18 }, () => {
  const x = 230 + random() * 1180;
  const y = 420 + random() * 460;
  const size = 2 * (3 + random() * 7);
  const seconds = 12 + random() * 10;
  return {
    x,
    y,
    w: size,
    h: size,
    style: { width: size, height: size, borderRadius: '50%', background: 'rgb(255 255 255 / 0.18)', border: '1px solid rgb(255 255 255 / 0.6)' },
    frames: '0%{transform:translateY(0);opacity:0}15%{opacity:1}100%{transform:translateY(-40vh);opacity:0}',
    timing: `${seconds.toFixed(1)}s ease-in ${(-random() * seconds).toFixed(1)}s infinite`,
  };
});

const layers = [
  caustics(0, 0),
  caustics(1, -3.5),
  wave(640, 26, '#38A9DB', 0.35, 22),
  wave(720, 30, '#1E88C7', 0.35, 15),
  wave(800, 22, '#0B6FA8', 0.4, 10),
  ...bubbles,
];

export default function WaterDecor({ still }: DecorProps) {
  return <Layers name="sb-water" layers={layers} still={still} />;
}
