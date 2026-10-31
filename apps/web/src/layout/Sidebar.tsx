import { List, Settings, SquareCheck, SquareKanban, Zap } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { SidebarItem } from '../components/SidebarItem';
import { SpaceSwitcher } from '../components/SpaceSwitcher';
import { sem, size } from '../theme/tokens';

export function Sidebar() {
  const { t } = useTranslation();

  return (
    <aside
      className="flex shrink-0 flex-col gap-1 p-4"
      style={{
        width: size.sidebar,
        backgroundColor: sem('bg-surface'),
        borderRight: `1px solid ${sem('border-default')}`,
      }}
    >
      <SpaceSwitcher spaceKey="SB" name={t('app.name')} label={t('nav.switchSpace')} />
      <nav aria-label={t('nav.label')} className="flex flex-col gap-1">
        <SidebarItem to="/backlog" label={t('nav.backlog')} icon={List} />
        <SidebarItem to="/board" label={t('nav.board')} icon={SquareKanban} />
        <SidebarItem to="/epics" label={t('nav.epics')} icon={Zap} />
        <SidebarItem to="/issues" label={t('nav.issues')} icon={SquareCheck} />
        <SidebarItem to="/settings" label={t('nav.settings')} icon={Settings} />
      </nav>
    </aside>
  );
}
