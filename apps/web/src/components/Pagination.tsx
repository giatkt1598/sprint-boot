import Typography from '@mui/material/Typography';
import { useTranslation } from 'react-i18next';
import { Button } from './Button';

export interface PaginationProps {
  skip: number;
  pageSize: number;
  total: number;
  onChange: (skip: number) => void;
}

export function Pagination({ skip, pageSize, total, onChange }: PaginationProps) {
  const { t } = useTranslation();

  return (
    <nav className="flex items-center justify-end gap-2">
      <Typography variant="body2" color="textSecondary">
        {t('common.showing', { from: skip + 1, to: Math.min(skip + pageSize, total), total })}
      </Typography>
      <Button kind="ghost" disabled={skip === 0} onClick={() => onChange(Math.max(0, skip - pageSize))}>{t('common.previous')}</Button>
      <Button kind="ghost" disabled={skip + pageSize >= total} onClick={() => onChange(skip + pageSize)}>{t('common.next')}</Button>
    </nav>
  );
}
