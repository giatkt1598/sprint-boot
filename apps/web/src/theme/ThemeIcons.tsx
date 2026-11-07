import { useEffect } from 'react';
import { setIconSet } from '../components/icons';
import { useAppearance } from './appearance';
import { themeById } from './themes';

/**
 * Puts the icons of the theme in use in place. A theme's set loads only when the theme is chosen; until it arrives, and
 * for themes without a set, Classic draws every icon.
 */
export function ThemeIcons() {
  const { theme } = useAppearance();

  useEffect(() => {
    const load = themeById(theme).icons;
    if (!load) {
      setIconSet({});
      return;
    }
    let current = true;
    void load().then((set) => {
      if (current) {
        setIconSet(set);
      }
    });
    return () => {
      current = false;
    };
  }, [theme]);

  return null;
}
