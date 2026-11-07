import lantern from '../art/chinese-lantern.svg';
import type { DecorProps } from '../types';
import { Layers, type Layer } from './Layers';

/**
 * Two red lanterns hang from the lacquer band and sway on a 6s pendulum, out of step, each with a soft glow that
 * breathes. Placed and sized as on the Penpot board.
 */
const hanging = (x: number, width: number, swing: number, delay: number): Layer[] => {
  const height = width * 2;
  return [
    {
      // The glow sits behind the lantern body, around its middle.
      x: x - width * 0.6,
      y: 54 + height * 0.1,
      w: width * 2.2,
      h: width * 2.2,
      style: { width: width * 2.2, height: width * 2.2, background: 'radial-gradient(closest-side, rgb(253 224 71 / 0.35), rgb(253 224 71 / 0))' },
      frames: '0%,100%{opacity:.55}50%{opacity:1}',
      timing: `3s ease-in-out ${delay}s infinite`,
    },
    {
      x,
      y: 54,
      w: width,
      h: height,
      style: { width, height, transformOrigin: '50% 0', transform: `rotate(${-swing}deg)` },
      frames: `0%,100%{transform:rotate(${-swing}deg)}50%{transform:rotate(${swing}deg)}`,
      timing: `6s ease-in-out ${delay}s infinite`,
      children: <img src={lantern} width={width} height={height} alt="" draggable={false} />,
    },
  ];
};

const layers = [...hanging(520, 43, 5, 0), ...hanging(610, 34, 6, -2.2)];

export default function ChineseDecor({ still }: DecorProps) {
  return <Layers name="sb-chinese" layers={layers} still={still} />;
}
