import { lazy, Suspense, useSyncExternalStore, type ComponentType, type LazyExoticComponent } from 'react';
import { useAppearance, type MotionChoice } from './appearance';
import { themeById, type DecorProps, type ThemeDefinition } from './themes';

const loaded = new Map<string, LazyExoticComponent<ComponentType<DecorProps>>>();

/** One lazy component per theme, so switching back and forth never loads a decor twice. */
const decorOf = (theme: ThemeDefinition) => {
  if (!theme.decor) {
    return null;
  }
  let component = loaded.get(theme.id);
  if (!component) {
    component = lazy(theme.decor);
    loaded.set(theme.id, component);
  }
  return component;
};

const subscribeTo = (target: EventTarget, event: string) => (listener: () => void) => {
  target.addEventListener(event, listener);
  return () => target.removeEventListener(event, listener);
};

const reducedQuery = () => window.matchMedia('(prefers-reduced-motion: reduce)');
const subscribeReduced = (listener: () => void) => subscribeTo(reducedQuery(), 'change')(listener);
const subscribeVisibility = subscribeTo(document, 'visibilitychange');

/**
 * The theme's animated layer, behind everything else. It stays out when the person turned motion off, holds still
 * when they asked for less (or their device did), and pauses while the tab is hidden so it costs nothing.
 */
export function DecorLayer({ theme, motion, osReduced, hidden }: { theme: ThemeDefinition; motion: MotionChoice; osReduced: boolean; hidden: boolean }) {
  const Decor = decorOf(theme);
  if (!Decor || motion === 'off') {
    return null;
  }
  const still = motion === 'reduced' || osReduced;

  return (
    <div
      aria-hidden
      className="sb-decor"
      data-testid="theme-decor"
      data-paused={hidden || still ? '' : undefined}
      style={{ position: 'fixed', inset: 0, zIndex: -1, overflow: 'hidden', pointerEvents: 'none' }}
    >
      <Suspense fallback={null}>
        <Decor still={still} paused={hidden} />
      </Suspense>
    </div>
  );
}

export function ThemeDecor() {
  const appearance = useAppearance();
  const osReduced = useSyncExternalStore(subscribeReduced, () => reducedQuery().matches);
  const hidden = useSyncExternalStore(subscribeVisibility, () => document.hidden);

  return <DecorLayer theme={themeById(appearance.theme)} motion={appearance.motion} osReduced={osReduced} hidden={hidden} />;
}
