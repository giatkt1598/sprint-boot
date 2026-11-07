import type { ReactNode } from 'react';
import { useAppearance } from './appearance';
import { themeById, type IllustrationKind } from './themes';

/** The theme's own picture for this place, or the fallback when the theme has none. Pictures are decoration only. */
export function ThemedIllustration({ kind, fallback = null }: { kind: IllustrationKind; fallback?: ReactNode }) {
  const { theme } = useAppearance();
  const Picture = themeById(theme).illustrations?.[kind];

  return Picture ? (
    <span aria-hidden className="sb-illustration flex justify-center">
      <Picture />
    </span>
  ) : (
    fallback
  );
}
