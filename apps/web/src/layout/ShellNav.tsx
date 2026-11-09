import Drawer from '@mui/material/Drawer';
import useMediaQuery from '@mui/material/useMediaQuery';
import type { Theme } from '@mui/material/styles';
import { createContext, useContext, useEffect, useMemo, useState, type MouseEvent, type ReactNode } from 'react';
import { useLocation } from 'react-router';
import { sem, size } from '../theme/tokens';

/** Below md the layout is for phones and tablets: the side menus become drawers. */
export const useCompact = () => useMediaQuery((theme: Theme) => theme.breakpoints.down('md'), { noSsr: true });

interface ShellNavState {
  /** A side menu is on the page, so the top bar offers a button to open it. */
  available: boolean;
  setAvailable: (available: boolean) => void;
  open: boolean;
  setOpen: (open: boolean) => void;
}

const ShellNavContext = createContext<ShellNavState>({ available: false, setAvailable: () => undefined, open: false, setOpen: () => undefined });

export const useShellNav = () => useContext(ShellNavContext);

/** Holds the side menu's drawer state for a shell, between the top bar's button and the menu itself. */
export function ShellNavProvider({ children }: { children: ReactNode }) {
  const [available, setAvailable] = useState(false);
  const { pathname } = useLocation();
  // The page the drawer was opened on: choosing another page closes it.
  const [openOn, setOpenOn] = useState<string | null>(null);
  const open = openOn === pathname;

  const value = useMemo(() => ({ available, setAvailable, open, setOpen: (next: boolean) => setOpenOn(next ? pathname : null) }), [available, open, pathname]);
  return <ShellNavContext.Provider value={value}>{children}</ShellNavContext.Provider>;
}

/**
 * A side menu: in place on wide screens, in a drawer the top bar opens on small ones. `children` gets whether it is
 * in the drawer, to drop what only works in place, such as the resize handle.
 */
export function SideNav({ label, children }: { label: string; children: (compact: boolean) => ReactNode }) {
  const compact = useCompact();
  const { open, setOpen, setAvailable } = useShellNav();

  useEffect(() => {
    setAvailable(true);
    return () => setAvailable(false);
  }, [setAvailable]);

  if (!compact) {
    return children(false);
  }

  return (
    <Drawer
      open={open}
      onClose={() => setOpen(false)}
      aria-label={label}
      slotProps={{
        paper: {
          id: 'sb-side-nav',
          // Choosing the page already open closes the drawer too.
          onClick: (event: MouseEvent) => (event.target as Element).closest('a') && setOpen(false),
          className: 'sb-surface',
          sx: { width: size.drawer, maxWidth: '85vw', backgroundColor: 'var(--sb-chrome-bg)', backgroundImage: 'none', borderRight: `1px solid ${sem('border-default')}` },
        },
      }}
    >
      {children(true)}
    </Drawer>
  );
}
