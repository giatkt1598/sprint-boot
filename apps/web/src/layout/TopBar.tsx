import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import { AddIcon, AdminIcon, MenuIcon, SignOutIcon, UserIcon } from '../components/icons';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import logo from '../../../../assets/brand/sprint-boot-logo.png';
import { useCurrentUser } from '../auth/AuthProvider';
import { signOut } from '../auth/oidc';
import { Avatar } from '../components/Avatar';
import { Button } from '../components/Button';
import { MenuItem } from '../components/Menu';
import { AssistantButton } from '../features/assistant/AssistantPanel';
import { CreateTicketDialog } from '../features/tickets/CreateTicketDialog';
import { QuickSearch } from '../features/tickets/QuickSearch';
import { RecentsMenu } from '../features/tickets/RecentsMenu';
import { NotificationBell } from '../features/notifications/NotificationBell';
import { font, sem, size } from '../theme/tokens';
import { useCompact } from '../lib/responsive';
import { useShellNav } from './ShellNav';

export function TopBar() {
  const { t } = useTranslation();
  const user = useCurrentUser();
  const navigate = useNavigate();
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [creating, setCreating] = useState(false);
  const isAdmin = (user.permissions ?? []).length > 0;
  const compact = useCompact();
  const nav = useShellNav();

  const open = (path: string) => {
    setMenuAnchor(null);
    void navigate(path);
  };

  // 14px between the 30px icon buttons leaves each its own 44px touch area on small screens.
  return (
    <header
      className="sb-surface flex shrink-0 items-center gap-3.5 px-2 sm:px-3 md:gap-4 md:px-6"
      style={{
        height: size.topBar,
        backgroundColor: 'var(--sb-chrome-bg)',
        boxShadow: 'var(--sb-top-bar-shadow)',
        borderBottom: `1px solid ${sem('border-default')}`,
      }}
    >
      {compact && nav.available && (
        <IconButton size="small" aria-label={t('nav.openMenu')} aria-expanded={nav.open} aria-controls={nav.open ? 'sb-side-nav' : undefined} onClick={() => nav.setOpen(!nav.open)}>
          <MenuIcon size={20} strokeWidth={1.5} aria-hidden />
        </IconButton>
      )}
      {/* On phones the menu button takes the logo's place: a touch screen has room for one or the other. */}
      <Link
        to="/"
        className={`${compact && nav.available ? 'hidden sm:flex' : 'flex'} shrink-0 items-center gap-3`}
        aria-label={compact ? t('app.name') : undefined}
        style={{ fontSize: font.size.md, fontWeight: font.weight.semibold, color: sem('text-primary'), textDecoration: 'none' }}
      >
        <img src={logo} alt="" width={24} height={24} />
        {/* The name gives its room to the buttons on small screens. */}
        <span className="hidden sm:inline">{t('app.name')}</span>
      </Link>
      <div className="flex-1" />
      <QuickSearch compact={compact} />
      {compact ? (
        <IconButton size="small" aria-label={t('topBar.create')} onClick={() => setCreating(true)}>
          <AddIcon size={20} strokeWidth={1.5} aria-hidden />
        </IconButton>
      ) : (
        <Button onClick={() => setCreating(true)}>{t('topBar.create')}</Button>
      )}
      <RecentsMenu />
      <AssistantButton />
      <NotificationBell />
      <IconButton size="small" sx={{ padding: 0.5 }} onClick={(event) => setMenuAnchor(event.currentTarget)}>
        <Avatar name={user.name ?? ''} src={user.avatarUrl} />
        <span className="sr-only">{t('topBar.account')}</span>
      </IconButton>
      <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
        <MenuItem icon={UserIcon} onClick={() => open('/account/profile')}>{t('topBar.profile')}</MenuItem>
        {isAdmin && <MenuItem icon={AdminIcon} onClick={() => open('/admin')}>{t('topBar.administration')}</MenuItem>}
        <MenuItem icon={SignOutIcon} onClick={() => void signOut()}>{t('topBar.signOut')}</MenuItem>
      </Menu>
      {creating && <CreateTicketDialog onClose={() => setCreating(false)} />}
    </header>
  );
}
