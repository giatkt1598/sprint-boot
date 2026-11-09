import type { ReactNode } from 'react';
import { svgAttributes, type IconComponent, type IconProps } from '../../components/icons';

/**
 * An icon drawn for a theme: SVG shapes on Lucide's 24px grid, stroked in the current colour, so it sits beside the
 * library icons of the same set at the same size and weight. `strokeWidth` stays the theme's own.
 */
export const drawnIcon = (name: string, shapes: ReactNode, strokeWidth = 1.5): IconComponent => {
  function DrawnIcon({ size = 24, ...props }: IconProps) {
    return (
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
        {...svgAttributes(props)}
      >
        {shapes}
      </svg>
    );
  }
  DrawnIcon.displayName = `${name}DrawnIcon`;
  return DrawnIcon;
};
