import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import { LogOut, Shield, User } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import logo from '../../../../assets/brand/sprint-boot-logo.png';
import { useCurrentUser } from '../auth/AuthProvider';
import { signOut } from '../auth/oidc';
import { Avatar } from '../components/Avatar';
import { Button } from '../components/Button';
import { MenuItem } from '../components/Menu';
import { CreateIssueDialog } from '../features/issues/CreateIssueDialog';
import { QuickSearch } from '../features/issues/QuickSearch';
import { font, sem, size } from '../theme/tokens';

export function TopBar() {
  const { t } = useTranslation();
  const user = useCurrentUser();
  const navigate = useNavigate();
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [creating, setCreating] = useState(false);
  const isAdmin = (user.permissions ?? []).length > 0;

  const open = (path: string) => {
    setMenuAnchor(null);
    void navigate(path);
  };

  return (
    <header
      className="flex shrink-0 items-center gap-4 px-6"
      style={{
        height: size.topBar,
        backgroundColor: sem('bg-surface'),
        borderBottom: `1px solid ${sem('border-default')}`,
      }}
    >
      <Link
        to="/"
        className="flex items-center gap-3"
        style={{ fontSize: font.size.md, fontWeight: font.weight.semibold, color: sem('text-primary'), textDecoration: 'none' }}
      >
        <img src={logo} alt="" width={24} height={24} />
        {t('app.name')}
      </Link>
      <div className="flex-1" />
      <QuickSearch />
      <Button onClick={() => setCreating(true)}>{t('topBar.create')}</Button>
      <IconButton aria-label={t('topBar.account')} size="small" sx={{ padding: 0.5 }} onClick={(event) => setMenuAnchor(event.currentTarget)}>
        <Avatar name={user.name ?? ''} />
      </IconButton>
      <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
        <MenuItem icon={User} onClick={() => open('/account/profile')}>{t('topBar.profile')}</MenuItem>
        {isAdmin && <MenuItem icon={Shield} onClick={() => open('/admin')}>{t('topBar.administration')}</MenuItem>}
        <MenuItem icon={LogOut} onClick={() => void signOut()}>{t('topBar.signOut')}</MenuItem>
      </Menu>
      {creating && <CreateIssueDialog onClose={() => setCreating(false)} />}
    </header>
  );
}
