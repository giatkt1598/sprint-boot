import Typography from '@mui/material/Typography';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useCan } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { LockedIcon } from '../../components/icons';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { DELETE_SPACES, useSpaceLifecycle, type Space } from './api';

/** Says why nothing on the page can be edited, and offers the way back to those who may take it. */
export function ArchivedBanner({ space }: { space: Pick<Space, 'id' | 'archivedAt' | 'canArchive'> | undefined }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { restore } = useSpaceLifecycle();

  if (!space?.archivedAt) {
    return null;
  }

  return (
    <div
      role="status"
      className="flex flex-wrap items-center gap-2 p-3"
      style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.card }}
    >
      <LockedIcon size={20} strokeWidth={1.5} aria-hidden />
      <Typography variant="body2" className="min-w-0 flex-1" sx={{ color: 'inherit' }}>{t('spaceArchive.banner')}</Typography>
      {space.canArchive && (
        <Button
          kind="secondary"
          disabled={restore.isPending}
          onClick={() =>
            restore.mutate(space.id!, {
              onSuccess: () => toast('success', t('spaceArchive.restored')),
              onError: (failure) => toast('error', toMessage(failure)),
            })
          }
        >
          {t('spaceArchive.restore')}
        </Button>
      )}
    </div>
  );
}

/** Deleting is the one step that ends in a purge, so the admin types the key to show they mean this space. */
export function DeleteSpaceDialog({ space, onClose, onDeleted }: {
  space: Pick<Space, 'id' | 'key' | 'name'>;
  onClose: () => void;
  onDeleted?: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { remove } = useSpaceLifecycle();
  const [typed, setTyped] = useState('');

  const confirm = () =>
    remove.mutate(
      { id: space.id!, confirmKey: typed.trim() },
      {
        onSuccess: () => {
          toast('success', t('spaceArchive.deleted'));
          onClose();
          onDeleted?.();
        },
      },
    );

  return (
    <Dialog
      open
      title={t('spaceArchive.deleteConfirmTitle', { name: space.name })}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('spaceArchive.delete')}
          confirmKind="danger"
          confirmDisabled={typed.trim().toUpperCase() !== space.key || remove.isPending}
          onCancel={onClose}
          onConfirm={confirm}
        />
      }
    >
      <Typography color="textSecondary">{t('spaceArchive.deleteHint')}</Typography>
      <TextField
        label={t('spaceArchive.deleteConfirm', { key: space.key })}
        autoFocus
        value={typed}
        onChange={(event) => setTyped(event.target.value.toUpperCase())}
      />
      {remove.isError && <Typography role="alert" color="error" variant="body2">{toMessage(remove.error)}</Typography>}
    </Dialog>
  );
}

function ArchiveDialog({ space, onClose }: { space: Space; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { archive } = useSpaceLifecycle();

  return (
    <Dialog
      open
      title={t('spaceArchive.archiveConfirmTitle', { name: space.name })}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('spaceArchive.archive')}
          confirmKind="danger"
          confirmDisabled={archive.isPending}
          onCancel={onClose}
          onConfirm={() =>
            archive.mutate(space.id!, {
              onSuccess: () => {
                toast('success', t('spaceArchive.archived'));
                onClose();
              },
            })
          }
        />
      }
    >
      <Typography color="textSecondary">{t('spaceArchive.archiveHint')}</Typography>
      {archive.isError && <Typography role="alert" color="error" variant="body2">{toMessage(archive.error)}</Typography>}
    </Dialog>
  );
}

function DangerRow({ title, hint, action }: { title: string; hint: string; action: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-4 p-4">
      <div className="min-w-0 flex-1 basis-64">
        <Typography variant="h3">{title}</Typography>
        <Typography variant="body2" color="textSecondary">{hint}</Typography>
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}

/** The end of the General settings: archive or restore for those who manage the space, delete for admins. */
export function SpaceDangerZone({ space }: { space: Space }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const navigate = useNavigate();
  const canDelete = useCan(DELETE_SPACES);
  const { restore } = useSpaceLifecycle();
  const [dialog, setDialog] = useState<'archive' | 'delete' | null>(null);

  if (!space.canArchive && !canDelete) {
    return null;
  }

  const archived = Boolean(space.archivedAt);

  return (
    <section aria-labelledby="danger-zone" className="flex flex-col gap-2 pt-4">
      <Typography id="danger-zone" variant="h2" sx={{ color: sem('feedback-danger-text') }}>{t('spaceArchive.dangerZone')}</Typography>
      <div
        className="flex flex-col"
        style={{ border: `1px solid ${sem('feedback-danger-text')}`, borderRadius: radius.card }}
      >
        {space.canArchive && (archived ? (
          <DangerRow
            title={t('spaceArchive.restoreTitle')}
            hint={t('spaceArchive.restoreHint')}
            action={
              <Button
                kind="secondary"
                disabled={restore.isPending}
                onClick={() =>
                  restore.mutate(space.id!, {
                    onSuccess: () => toast('success', t('spaceArchive.restored')),
                    onError: (failure) => toast('error', toMessage(failure)),
                  })
                }
              >
                {t('spaceArchive.restore')}
              </Button>
            }
          />
        ) : (
          <DangerRow
            title={t('spaceArchive.archiveTitle')}
            hint={t('spaceArchive.archiveHint')}
            action={<Button kind="danger" onClick={() => setDialog('archive')}>{t('spaceArchive.archive')}</Button>}
          />
        ))}
        {canDelete && (
          <div style={space.canArchive ? { borderTop: `1px solid ${sem('border-default')}` } : undefined}>
            <DangerRow
              title={t('spaceArchive.deleteTitle')}
              hint={archived ? t('spaceArchive.deleteHint') : t('spaceArchive.deleteNeedsArchive')}
              action={<Button kind="danger" disabled={!archived} onClick={() => setDialog('delete')}>{t('spaceArchive.delete')}</Button>}
            />
          </div>
        )}
      </div>
      {dialog === 'archive' && <ArchiveDialog space={space} onClose={() => setDialog(null)} />}
      {dialog === 'delete' && (
        <DeleteSpaceDialog space={space} onClose={() => setDialog(null)} onDeleted={() => void navigate('/admin/spaces?tab=trash')} />
      )}
    </section>
  );
}
