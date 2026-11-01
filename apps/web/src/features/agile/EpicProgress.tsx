import { useTranslation } from 'react-i18next';
import { font, sem } from '../../theme/tokens';

export function EpicProgress({ total, done, inProgress }: { total: number; done: number; inProgress: number }) {
  const { t } = useTranslation();
  const percent = total ? Math.round((done / total) * 100) : 0;
  const doneWidth = total ? (done / total) * 100 : 0;
  const progressWidth = total ? (inProgress / total) * 100 : 0;

  return (
    <div className="flex items-center gap-2">
      <div
        role="img"
        aria-label={t('agile.progress', { percent, done, total })}
        className="flex h-1.5 flex-1 overflow-hidden"
        style={{ minWidth: 120, borderRadius: 999, backgroundColor: sem('bg-subtle') }}
      >
        <div style={{ width: `${doneWidth}%`, backgroundColor: sem('status-done-text') }} />
        <div style={{ width: `${progressWidth}%`, backgroundColor: sem('status-progress-text') }} />
      </div>
      <span style={{ width: 36, fontSize: font.size.xs }}>{percent}%</span>
    </div>
  );
}
