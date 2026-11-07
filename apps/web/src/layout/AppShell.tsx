import { useEffect } from 'react';
import { Outlet, useLocation } from 'react-router';
import { rememberAppLocation } from '../lib/returnTo';
import { sem } from '../theme/tokens';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

/** Pages that need the whole width: the full-page chat and search results. */
const WIDE = /^\/(assistant|search)(\/|$)/;

export function AppShell() {
  const location = useLocation();
  const wide = WIDE.test(location.pathname);
  // Settings pages live outside this shell, so this is where their back arrow returns to.
  useEffect(() => rememberAppLocation(location.pathname + location.search), [location.pathname, location.search]);

  return (
    <div className="flex h-screen flex-col">
      <TopBar />
      <div className="flex min-h-0 flex-1">
        {!wide && <Sidebar />}
        <main className="min-w-0 flex-1 overflow-auto p-8" style={{ backgroundColor: sem('bg-surface') }}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}

/** Settings areas replace the sidebar with their own menu. */
export function BareShell() {
  return (
    <div className="flex h-screen flex-col">
      <TopBar />
      <div className="flex min-h-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}
