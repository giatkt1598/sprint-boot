import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import { X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { shadow } from '../../theme/tokens';
import { IssueContent } from '../issues/IssuePage';

/** Keeps the board in view while working on one issue. */
export function IssuePanel({ issueKey, onClose }: { issueKey: string | null; onClose: () => void }) {
  const { t } = useTranslation();
  const [scrolled, setScrolled] = useState(false);

  const close = (
    <div className="flex justify-end">
      <IconButton size="small" aria-label={t('common.close')} onClick={onClose}>
        <X size={16} strokeWidth={1.5} aria-hidden />
      </IconButton>
    </div>
  );

  return (
    <Drawer
      anchor="right"
      open={Boolean(issueKey)}
      onClose={onClose}
      slotProps={{
        backdrop: { invisible: true },
        // No top padding: the sticky header brings its own, so it pins flush to the top while the rest scrolls.
        paper: {
          sx: { width: 640, maxWidth: '100%', paddingX: 3, paddingBottom: 3, boxShadow: shadow.drawer },
          onScroll: (event: React.UIEvent<HTMLDivElement>) => setScrolled(event.currentTarget.scrollTop > 0),
        },
      }}
    >
      {issueKey ? (
        <IssueContent key={issueKey} issueKey={issueKey} compact panelHeader={{ close, scrolled }} />
      ) : (
        <div className="pt-6">{close}</div>
      )}
    </Drawer>
  );
}
