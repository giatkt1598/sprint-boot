import IconButton from '@mui/material/IconButton';
import { Bell } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import logo from '../../../../assets/brand/sprint-boot-logo.png';
import { Avatar } from '../components/Avatar';
import { Button } from '../components/Button';
import { SearchInput } from '../components/SearchInput';
import { font, sem, size } from '../theme/tokens';

export function TopBar() {
  const { t } = useTranslation();

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
      <SearchInput label={t('topBar.search')} />
      <Button>{t('topBar.create')}</Button>
      <IconButton aria-label={t('topBar.notifications')} size="small" sx={{ color: sem('text-secondary') }}>
        <Bell size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
      <IconButton aria-label={`${t('app.initials')}, ${t('topBar.account')}`} size="small" sx={{ padding: 0.5 }}>
        <Avatar name="Sprint Boot" />
      </IconButton>
    </header>
  );
}
