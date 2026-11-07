import drum from '../art/vietnamese-drum.svg';
import type { DecorProps } from '../types';
import { Layers, seeded, type Layer } from './Layers';

/** The face of the Đông Sơn drum, centred where the Penpot board has it, turns once in ten minutes. */
const face: Layer = {
  x: 180,
  y: 20,
  w: 1080,
  h: 1080,
  style: { aspectRatio: '1', height: 'auto', transform: 'rotate(0deg)' },
  frames: '0%{transform:rotate(0deg)}100%{transform:rotate(360deg)}',
  timing: '600s linear infinite',
  children: <img src={drum} alt="" draggable={false} style={{ display: 'block', width: '100%', height: '100%' }} />,
};

/** Flecks of gold leaf catch the light now and then. */
const random = seeded(17);
const flecks: Layer[] = Array.from({ length: 22 }, () => {
  const size = 2 + random() * 3;
  const seconds = 3 + random() * 4;
  return {
    x: 240 + random() * 1180,
    y: 60 + random() * 820,
    w: size,
    h: size,
    style: { width: size, height: size, borderRadius: '50%', background: 'radial-gradient(closest-side, #F5D77A, rgb(233 196 106 / 0))', opacity: 0 },
    frames: '0%,100%{opacity:0}50%{opacity:1}',
    timing: `${seconds.toFixed(1)}s ease-in-out ${(-random() * seconds).toFixed(1)}s infinite`,
  };
});

export default function VietnameseDecor({ still }: DecorProps) {
  // Reduced motion keeps the drum still and the gold leaf dark.
  return <Layers name="sb-vietnamese" layers={still ? [face] : [face, ...flecks]} still={still} />;
}
