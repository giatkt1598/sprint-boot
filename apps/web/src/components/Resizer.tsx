import Box from '@mui/material/Box';
import { useEffect, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { sem } from '../theme/tokens';

const STEP = 16;

interface WidthLimits {
  min: number;
  max: number;
  /** Used until the person picks a width, and again after a double click on the handle. */
  initial: number;
  /** Never wider than this share of the window, so the main content always keeps room. */
  maxShare?: number;
}

function useWindowWidth() {
  const [width, setWidth] = useState(() => window.innerWidth);
  useEffect(() => {
    const update = () => setWidth(window.innerWidth);
    window.addEventListener('resize', update);
    return () => window.removeEventListener('resize', update);
  }, []);
  return width;
}

/** A panel width the person chooses, kept in this browser under `storageKey`. */
export function usePersistentWidth(storageKey: string, { min, max, initial, maxShare }: WidthLimits) {
  const windowWidth = useWindowWidth();
  const upper = Math.max(min, maxShare ? Math.min(max, Math.floor(windowWidth * maxShare)) : max);
  const clamp = (value: number) => Math.min(upper, Math.max(min, value));
  const [stored, setStored] = useState(() => Number(localStorage.getItem(storageKey)) || initial);

  const change = (next: number) => {
    const value = Math.min(max, Math.max(min, Math.round(next)));
    setStored(value);
    localStorage.setItem(storageKey, String(value));
  };

  return { width: clamp(stored), min, max: upper, initial, change };
}

interface ResizerProps {
  label: string;
  width: number;
  min: number;
  max: number;
  initial: number;
  onChange: (width: number) => void;
  /** Which edge of the panel the handle sits on: `end` grows the panel when dragged right. */
  edge: 'start' | 'end';
}

/** A drag handle on a panel edge. The line only shows on hover or focus; arrow keys resize too. */
export function Resizer({ label, width, min, max, initial, onChange, edge }: ResizerProps) {
  const sign = edge === 'end' ? 1 : -1;

  const startDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = width;
    const handle = event.currentTarget;
    handle.setPointerCapture(event.pointerId);
    const move = (next: PointerEvent) => onChange(startWidth + sign * (next.clientX - startX));
    const stop = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', stop);
      handle.removeEventListener('pointercancel', stop);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', stop);
    handle.addEventListener('pointercancel', stop);
  };

  return (
    <Box
      role="separator"
      aria-orientation="vertical"
      aria-label={label}
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuenow={width}
      tabIndex={0}
      className="flex w-2 shrink-0 cursor-col-resize justify-center"
      onPointerDown={startDrag}
      onDoubleClick={() => onChange(initial)}
      onKeyDown={(event) => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault();
          onChange(width + sign * (event.key === 'ArrowRight' ? STEP : -STEP));
        } else if (event.key === 'Home' || event.key === 'End') {
          event.preventDefault();
          onChange(event.key === 'Home' ? min : max);
        }
      }}
      sx={{
        touchAction: 'none',
        '&::after': { content: '""', width: '2px', backgroundColor: 'transparent' },
        '&:hover::after, &:focus-visible::after, &:active::after': { backgroundColor: sem('border-focus') },
      }}
    />
  );
}
