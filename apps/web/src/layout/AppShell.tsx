import { Outlet } from 'react-router';
import { sem } from '../theme/tokens';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

export function AppShell() {
  return (
    <div className="flex h-screen flex-col">
      <TopBar />
      <div className="flex min-h-0 flex-1">
        <Sidebar />
        <main className="min-w-0 flex-1 overflow-auto p-8" style={{ backgroundColor: sem('bg-surface') }}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
