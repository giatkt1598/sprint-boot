import Typography from '@mui/material/Typography';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { ColumnsEditor } from '../agile/ColumnsEditor';
import { SpacePermission, type Space } from './api';

export function SpaceBoardPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('agile.board')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('agile.boardHint')}</Typography>
      </div>
      <ColumnsEditor space={space} canManage={space.permissions?.includes(SpacePermission.manage) ?? false} />
    </div>
  );
}
