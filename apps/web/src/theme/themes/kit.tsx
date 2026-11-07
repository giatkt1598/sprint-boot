import type { ComponentType } from 'react';

/** A hex colour at an opacity, for the see-through surfaces of a theme. */
export const alpha = (hex: string, opacity: number) => {
  const [r, g, b] = [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16));
  return `rgb(${r} ${g} ${b} / ${opacity})`;
};

/** A theme picture drawn as a file, so its gradient ids never clash with another SVG on the page. */
export const picture = (src: string, width = 320, height = 200): ComponentType =>
  function ThemePicture() {
    return <img src={src} width={width} height={height} alt="" draggable={false} />;
  };
