import type { ReactNode } from 'react';
import { svgAttributes, type IconComponent, type IconName, type IconProps, type IconSet } from '../../components/icons';
import { genericShapes } from './generic';

/** How a culture strokes its icons. */
export interface Stroke {
  width: number;
  /** Square ends and mitred corners read as brush and carving; round ones as a softer pen. */
  cap?: 'round' | 'square';
}

/**
 * An icon drawn for a theme: SVG shapes on Lucide's 24px grid, stroked in the current colour, so it sits beside other
 * icons at the same size. The stroke stays the theme's own whatever strokeWidth a caller passes.
 */
export const drawnIcon = (name: string, shapes: ReactNode, stroke: Stroke = { width: 1.5 }): IconComponent => {
  const square = stroke.cap === 'square';
  function DrawnIcon({ size = 24, ...props }: IconProps) {
    return (
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={stroke.width}
        strokeLinecap={square ? 'square' : 'round'}
        strokeLinejoin={square ? 'miter' : 'round'}
        {...svgAttributes(props)}
      >
        {shapes}
      </svg>
    );
  }
  DrawnIcon.displayName = `${name}DrawnIcon`;
  return DrawnIcon;
};

/**
 * A whole culture set: its own pictures, and the plain interface glyphs drawn in the same stroke, so no icon of the
 * theme comes from a library.
 */
export const drawnSet = (own: Partial<Record<IconName, ReactNode>>, stroke: Stroke): IconSet =>
  Object.fromEntries(
    Object.entries({ ...genericShapes, ...own }).map(([name, shapes]) => [name, drawnIcon(name, shapes, stroke)]),
  ) as IconSet;
