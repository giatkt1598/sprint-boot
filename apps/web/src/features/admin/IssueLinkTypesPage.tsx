import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { sem } from '../../theme/tokens';
import { useLinkTypes } from '../issues/LinkedIssues';

type LinkType = components['schemas']['IssueLinkTypeDto'];

interface Draft {
  id?: string;
  name: string;
  outward: string;
  inward: string;
}

function LinkTypeDialog({ draft, onClose }: { draft: Draft; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);

  const save = useMutation({
    mutationFn: () => {
      const body = { name: value.name, outward: value.outward, inward: value.inward };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/issue-link-type/{id}', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/issue-link-type', { body }));
    },
    onSuccess: async () => {
      // Renaming changes how existing links read, so issue pages refresh too.
      await queryClient.invalidateQueries({ queryKey: ['issue-link-types'] });
      await queryClient.invalidateQueries({ queryKey: ['issues', 'links'] });
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const ready = Boolean(value.name.trim() && value.outward.trim() && value.inward.trim()) && !save.isPending;

  return (
    <Dialog
      open
      title={t(value.id ? 'linkTypes.editTitle' : 'linkTypes.createTitle')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('common.save')} confirmDisabled={!ready} onCancel={onClose} onConfirm={() => save.mutate()} />}
    >
      <TextField label={t('linkTypes.name')} value={value.name} autoFocus onChange={(event) => setValue({ ...value, name: event.target.value })} />
      <TextField label={t('linkTypes.outward')} placeholder="blocks" value={value.outward} onChange={(event) => setValue({ ...value, outward: event.target.value })} />
      <TextField label={t('linkTypes.inward')} placeholder="is blocked by" value={value.inward} onChange={(event) => setValue({ ...value, inward: event.target.value })} />
      <Typography variant="body2" color="textSecondary">{t('linkTypes.preview', { outward: value.outward || '…', inward: value.inward || '…' })}</Typography>
    </Dialog>
  );
}

/** Workspace-wide: every space links issues with the same relationships. */
export function IssueLinkTypesPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useLinkTypes();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<LinkType | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/issue-link-type/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issue-link-types'] });
      setRemoving(null);
    },
    onError: (failure) => {
      setRemoving(null);
      toast('error', toMessage(failure));
    },
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('linkTypes.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('linkTypes.subtitle')}</Typography>
        </div>
        <Button onClick={() => setDraft({ name: '', outward: '', inward: '' })}>{t('linkTypes.add')}</Button>
      </div>
      {types.isPending && <LoadingState />}
      {types.isError && <ErrorState onRetry={() => void types.refetch()} />}
      {types.data && (
        <Table<LinkType>
          label={t('linkTypes.title')}
          rows={types.data}
          rowKey={(item) => item.id!}
          columns={[
            { key: 'name', header: t('linkTypes.name'), render: (item) => item.name },
            { key: 'outward', header: t('linkTypes.outward'), render: (item) => item.outward },
            { key: 'inward', header: t('linkTypes.inward'), render: (item) => item.inward },
            { key: 'count', header: t('linkTypes.used'), render: (item) => t('linkTypes.linkCount', { count: item.linkCount ?? 0 }) },
            {
              key: 'actions',
              header: <span className="sr-only">{t('invitations.actions')}</span>,
              render: (item) => (
                <span className="flex gap-3 whitespace-nowrap">
                  <Link
                    component="button"
                    variant="body2"
                    aria-label={`${t('common.edit')} ${item.name}`}
                    onClick={() => setDraft({ id: item.id!, name: item.name ?? '', outward: item.outward ?? '', inward: item.inward ?? '' })}
                  >
                    {t('common.edit')}
                  </Link>
                  {/* A type in use stays: removing it would silently drop every link of that kind. */}
                  {(item.linkCount ?? 0) === 0 && (
                    <Link component="button" variant="body2" aria-label={`${t('common.delete')} ${item.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(item)}>
                      {t('common.delete')}
                    </Link>
                  )}
                </span>
              ),
            },
          ]}
        />
      )}
      {draft && <LinkTypeDialog draft={draft} onClose={() => setDraft(null)} />}
      <DialogConfirm
        open={removing !== null}
        title={t('linkTypes.deleteTitle', { name: removing?.name })}
        description={t('linkTypes.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id!)}
      />
    </div>
  );
}
