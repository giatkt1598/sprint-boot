import { useEffect, useState } from 'react';
import Menu from '@mui/material/Menu';
import { LayoutDashboard, List, Settings, SquareCheck, SquareKanban, Users, Zap } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { useCan, useCurrentUser } from '../auth/AuthProvider';
import { MenuItem } from '../components/Menu';
import { Resizer, usePersistentWidth } from '../components/Resizer';
import { SidebarItem } from '../components/SidebarItem';
import { SpaceSwitcher } from '../components/SpaceSwitcher';
import { SpaceLogo } from '../components/SpaceLogo';
import { useSpace, useSpaces } from '../features/spaces/api';
import { rememberSpace } from '../features/spaces/HomePage';
import { CreateSpaceDialog } from '../features/spaces/SpacesPage';
import { sem, size } from '../theme/tokens';

const RECENT_SPACES = 5;

// Never narrower than the space switcher needs, never so wide the page loses its content.
const SIDEBAR_WIDTH = { min: 180, max: 400, initial: size.sidebar, maxShare: 0.4 };

export function Sidebar() {
  const { t } = useTranslation();
  const { spaceKey: routeKey, issueKey } = useParams();
  const spaceKey = routeKey ?? issueKey?.replace(/-\d+$/, '');
  const navigate = useNavigate();
  const space = useSpace(spaceKey);
  const spaces = useSpaces();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [creating, setCreating] = useState(false);
  const me = useCurrentUser();
  const canManageSpaces = useCan('SprintBoot.Users.Manage');
  const openedKey = space.data?.key;
  const sidebar = usePersistentWidth('sprint-boot.sidebar-width', SIDEBAR_WIDTH);

  useEffect(() => {
    if (openedKey) {
      rememberSpace(me.id, openedKey);
    }
  }, [me.id, openedKey]);

  const open = (path: string) => {
    setAnchor(null);
    void navigate(path);
  };

  return (
    <div className="sb-surface flex shrink-0" style={{ backgroundColor: 'var(--sb-chrome-bg)', borderRight: `1px solid ${sem('border-default')}` }}>
      <aside className="flex min-w-0 flex-col gap-1 p-4" style={{ width: sidebar.width }}>
        <SpaceSwitcher
          spaceKey={(space.data?.key ?? '').slice(0, 2)}
          logoUrl={space.data?.logoUrl}
          name={space.data?.name ?? t('spaces.title')}
          label={t('nav.switchSpace')}
          onClick={(event) => setAnchor(event.currentTarget)}
        />
        <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
          {/* Managing spaces is an admin screen, so everyone else needs the full list here. */}
          {(canManageSpaces ? (spaces.data ?? []).slice(0, RECENT_SPACES) : (spaces.data ?? [])).map((item) => (
            <MenuItem key={item.id} onClick={() => open(`/spaces/${item.key}/overview`)}>
              <span className="flex min-w-0 items-center gap-2">
                <SpaceLogo spaceKey={item.key} logoUrl={item.logoUrl} />
                <span className="truncate">{item.name}</span>
              </span>
            </MenuItem>
          ))}
          {canManageSpaces && <MenuItem onClick={() => open('/admin/spaces')}>{t('nav.allSpaces')}</MenuItem>}
          <MenuItem onClick={() => { setAnchor(null); setCreating(true); }}>{t('spaces.create')}</MenuItem>
        </Menu>
        {creating && <CreateSpaceDialog open onClose={() => setCreating(false)} />}
        {spaceKey && (
          <nav aria-label={t('nav.label')} className="flex flex-col gap-1">
            <SidebarItem to={`/spaces/${spaceKey}/overview`} label={t('nav.overview')} icon={LayoutDashboard} />
            <SidebarItem to={`/spaces/${spaceKey}/backlog`} label={t('nav.backlog')} icon={List} />
            <SidebarItem to={`/spaces/${spaceKey}/board`} label={t('nav.board')} icon={SquareKanban} />
            <SidebarItem to={`/spaces/${spaceKey}/epics`} label={t('nav.epics')} icon={Zap} />
            <SidebarItem to={`/spaces/${spaceKey}/issues`} label={t('nav.issues')} icon={SquareCheck} />
            <SidebarItem to={`/spaces/${spaceKey}/people`} label={t('nav.people')} icon={Users} />
            <SidebarItem to={`/spaces/${spaceKey}/settings`} label={t('nav.settings')} icon={Settings} />
          </nav>
        )}
      </aside>
      <Resizer edge="end" label={t('nav.resizeSidebar')} {...sidebar} onChange={sidebar.change} />
    </div>
  );
}
