import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import { X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { shadow } from '../../theme/tokens';
import { IssueContent } from '../issues/IssuePage';

/** Keeps the board in view while working on one issue. */
export function IssuePanel({ issueKey, onClose }: { issueKey: string | null; onClose: () => void }) {
  const { t } = useTranslation();

  return (
    <Drawer
      anchor="right"
      open={Boolean(issueKey)}
      onClose={onClose}
      slotProps={{ backdrop: { invisible: true }, paper: { sx: { width: 640, maxWidth: '100%', padding: 3, boxShadow: shadow.drawer } } }}
    >
      <div className="flex justify-end">
        <IconButton size="small" aria-label={t('common.close')} onClick={onClose}>
          <X size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      </div>
      {issueKey && <IssueContent key={issueKey} issueKey={issueKey} compact />}
    </Drawer>
  );
}
