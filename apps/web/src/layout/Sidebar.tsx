import { useState } from 'react';
import Menu from '@mui/material/Menu';
import { List, Settings, SquareCheck, SquareKanban, Zap } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { MenuItem } from '../components/Menu';
import { SidebarItem } from '../components/SidebarItem';
import { SpaceSwitcher } from '../components/SpaceSwitcher';
import { useSpace, useSpaces } from '../features/spaces/api';
import { CreateSpaceDialog } from '../features/spaces/SpacesPage';
import { sem, size } from '../theme/tokens';

const RECENT_SPACES = 5;

export function Sidebar() {
  const { t } = useTranslation();
  const { spaceKey: routeKey, issueKey } = useParams();
  const spaceKey = routeKey ?? issueKey?.replace(/-\d+$/, '');
  const navigate = useNavigate();
  const space = useSpace(spaceKey);
  const spaces = useSpaces();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [creating, setCreating] = useState(false);

  const open = (path: string) => {
    setAnchor(null);
    void navigate(path);
  };

  return (
    <aside
      className="flex shrink-0 flex-col gap-1 p-4"
      style={{
        width: size.sidebar,
        backgroundColor: sem('bg-surface'),
        borderRight: `1px solid ${sem('border-default')}`,
      }}
    >
      <SpaceSwitcher
        spaceKey={(space.data?.key ?? '').slice(0, 2)}
        name={space.data?.name ?? t('spaces.title')}
        label={t('nav.switchSpace')}
        onClick={(event) => setAnchor(event.currentTarget)}
      />
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        {(spaces.data ?? []).slice(0, RECENT_SPACES).map((item) => (
          <MenuItem key={item.id} onClick={() => open(`/spaces/${item.key}/issues`)}>{item.name}</MenuItem>
        ))}
        <MenuItem onClick={() => open('/spaces')}>{t('nav.allSpaces')}</MenuItem>
        <MenuItem onClick={() => { setAnchor(null); setCreating(true); }}>{t('spaces.create')}</MenuItem>
      </Menu>
      {creating && <CreateSpaceDialog open onClose={() => setCreating(false)} />}
      {spaceKey && (
        <nav aria-label={t('nav.label')} className="flex flex-col gap-1">
          <SidebarItem to={`/spaces/${spaceKey}/backlog`} label={t('nav.backlog')} icon={List} />
          <SidebarItem to={`/spaces/${spaceKey}/board`} label={t('nav.board')} icon={SquareKanban} />
          <SidebarItem to={`/spaces/${spaceKey}/epics`} label={t('nav.epics')} icon={Zap} />
          <SidebarItem to={`/spaces/${spaceKey}/issues`} label={t('nav.issues')} icon={SquareCheck} />
          <SidebarItem to={`/spaces/${spaceKey}/settings`} label={t('nav.settings')} icon={Settings} />
        </nav>
      )}
    </aside>
  );
}
