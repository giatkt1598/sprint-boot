import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { SpaceLogo } from '../../components/SpaceLogo';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { Tabs } from '../../components/Tabs';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { sem } from '../../theme/tokens';
import { DELETE_SPACES, useSpaceLifecycle, useSpaces, useTrashedSpaces, type Space, type TrashedSpace } from './api';
import { DeleteSpaceDialog } from './SpaceLifecycle';

const KEY_PATTERN = /^[A-Z][A-Z0-9]{1,9}$/;

export function CreateSpaceDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [key, setKey] = useState('');
  const [type, setType] = useState('Scrum');
  const keyInvalid = key.length > 0 && !KEY_PATTERN.test(key);

  const create = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/project', { body: { name, key, type: type === 'Scrum' ? 0 : 1 } })),
    onSuccess: async (space) => {
      await queryClient.invalidateQueries({ queryKey: ['spaces'] });
      onClose();
      await navigate(`/spaces/${space.key}/tickets`);
    },
  });

  return (
    <Dialog
      open={open}
      title={t('spaces.create')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('spaces.create')}
          confirmDisabled={!name.trim() || !KEY_PATTERN.test(key) || create.isPending}
          onCancel={onClose}
          onConfirm={() => create.mutate()}
        />
      }
    >
      <TextField label={t('spaces.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <div className="flex flex-col gap-1">
        <TextField
          label={t('spaces.key')}
          value={key}
          error={keyInvalid ? t('spaces.keyInvalid') : undefined}
          onChange={(event) => setKey(event.target.value.toUpperCase())}
        />
        {!keyInvalid && <Typography variant="body2" color="textSecondary">{t('spaces.keyHint')}</Typography>}
      </div>
      <SelectField
        label={t('spaces.type')}
        value={type}
        onChange={(event) => setType(event.target.value)}
        options={[
          { value: 'Scrum', label: t('spaces.scrum') },
          { value: 'Kanban', label: t('spaces.kanban') },
        ]}
      />
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}

type SpacesTab = 'active' | 'archived' | 'trash';

type Matcher = (space: { name?: string | null; key?: string | null }) => boolean;

/** Admin › Spaces: active spaces, archived ones to restore or delete, and the trash for those who may delete. */
export function SpacesPage() {
  const { t } = useTranslation();
  const canDelete = useCan(DELETE_SPACES);
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab');
  const tab: SpacesTab = requested === 'archived' || (requested === 'trash' && canDelete) ? requested : 'active';
  const [filter, setFilter] = useState('');
  const [creating, setCreating] = useState(false);
  const matches: Matcher = (space) => `${space.name} ${space.key}`.toLowerCase().includes(filter.toLowerCase());

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center gap-2">
        <Typography variant="h1" className="flex-1 max-sm:basis-full">{t('spaces.title')}</Typography>
        <SearchInput label={t('spaces.filter')} value={filter} onChange={(event) => setFilter(event.target.value)} />
        <Button onClick={() => setCreating(true)}>{t('spaces.create')}</Button>
      </div>
      <Tabs
        label={t('spaces.title')}
        value={tab}
        onChange={(next) => setParams(next === 'active' ? {} : { tab: next }, { replace: true })}
        items={[
          { value: 'active', label: t('spaceArchive.tabActive') },
          { value: 'archived', label: t('spaceArchive.tabArchived') },
          ...(canDelete ? [{ value: 'trash', label: t('spaceArchive.tabTrash') }] : []),
        ]}
      />
      {tab === 'active' && <ActiveSpaces matches={matches} />}
      {tab === 'archived' && <ArchivedSpaces matches={matches} canDelete={canDelete} />}
      {tab === 'trash' && <TrashedSpaces matches={matches} />}
      {creating && <CreateSpaceDialog open onClose={() => setCreating(false)} />}
    </div>
  );
}

function SpaceName({ space }: { space: Pick<Space, 'key' | 'name' | 'logoUrl'> }) {
  return (
    <span className="flex items-center gap-2">
      <SpaceLogo spaceKey={space.key} logoUrl={space.logoUrl} />
      <Link component={RouterLink} to={`/spaces/${space.key}/tickets`} color="inherit">{space.name}</Link>
    </span>
  );
}

function ActiveSpaces({ matches }: { matches: Matcher }) {
  const { t, i18n } = useTranslation();
  const spaces = useSpaces();
  const rows = (spaces.data ?? []).filter(matches);

  return (
    <>
      {spaces.isPending && <LoadingState />}
      {spaces.isError && <ErrorState onRetry={() => void spaces.refetch()} />}
      {spaces.isSuccess && rows.length === 0 && <EmptyState title={t('spaces.empty')} />}
      {rows.length > 0 && (
        <Table<Space>
          label={t('spaces.title')}
          rows={rows}
          rowKey={(space) => space.id!}
          columns={[
            { key: 'name', header: t('spaces.name'), render: (space) => <SpaceName space={space} /> },
            { key: 'key', header: t('spaces.key'), render: (space) => space.key },
            { key: 'type', header: t('spaces.type'), render: (space) => t(space.type === 0 ? 'spaces.scrum' : 'spaces.kanban') },
            {
              key: 'lead',
              header: t('spaces.lead'),
              render: (space) =>
                space.lead && (
                  <span className="flex items-center gap-2">
                    <Avatar name={space.lead.name ?? ''} src={space.lead.avatarUrl} />
                    {space.lead.name}
                  </span>
                ),
            },
            { key: 'tickets', header: t('spaces.tickets'), render: (space) => space.ticketCount },
            { key: 'updated', header: t('spaces.updated'), render: (space) => formatDate(space.updatedAt, i18n.language) },
          ]}
        />
      )}
    </>
  );
}

function ArchivedSpaces({ matches, canDelete }: { matches: Matcher; canDelete: boolean }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const spaces = useSpaces({ includeArchived: true });
  const { restore } = useSpaceLifecycle();
  const [deleting, setDeleting] = useState<Space | null>(null);
  const rows = (spaces.data ?? []).filter((space) => space.archivedAt && matches(space));

  return (
    <>
      {spaces.isPending && <LoadingState />}
      {spaces.isError && <ErrorState onRetry={() => void spaces.refetch()} />}
      {spaces.isSuccess && rows.length === 0 && <EmptyState title={t('spaceArchive.noArchived')} />}
      {rows.length > 0 && (
        <Table<Space>
          label={t('spaceArchive.tabArchived')}
          rows={rows}
          rowKey={(space) => space.id!}
          columns={[
            { key: 'name', header: t('spaces.name'), render: (space) => <SpaceName space={space} /> },
            { key: 'key', header: t('spaces.key'), render: (space) => space.key },
            { key: 'tickets', header: t('spaces.tickets'), render: (space) => space.ticketCount },
            { key: 'archived', header: t('spaceArchive.archivedAt'), render: (space) => formatDate(space.archivedAt, i18n.language) },
            {
              key: 'actions',
              header: '',
              render: (space) => (
                <span className="flex justify-end gap-2">
                  {space.canArchive && (
                    <Button
                      kind="ghost"
                      aria-label={`${t('spaceArchive.restore')}: ${space.name}`}
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
                  {canDelete && (
                    <Button
                      kind="ghost"
                      aria-label={`${t('spaceArchive.delete')}: ${space.name}`}
                      sx={{ color: sem('feedback-danger-text') }}
                      onClick={() => setDeleting(space)}
                    >
                      {t('spaceArchive.delete')}
                    </Button>
                  )}
                </span>
              ),
            },
          ]}
        />
      )}
      {deleting && <DeleteSpaceDialog space={deleting} onClose={() => setDeleting(null)} />}
    </>
  );
}

function TrashedSpaces({ matches }: { matches: Matcher }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const trash = useTrashedSpaces(true);
  const { restoreDeleted } = useSpaceLifecycle();
  const rows = (trash.data ?? []).filter(matches);

  return (
    <>
      {trash.isPending && <LoadingState />}
      {trash.isError && <ErrorState onRetry={() => void trash.refetch()} />}
      {trash.isSuccess && rows.length === 0 && <EmptyState title={t('spaceArchive.trashEmpty')} />}
      {rows.length > 0 && (
        <Table<TrashedSpace>
          label={t('spaceArchive.tabTrash')}
          rows={rows}
          rowKey={(space) => space.id!}
          columns={[
            {
              key: 'name',
              header: t('spaces.name'),
              render: (space) => (
                <span className="flex items-center gap-2">
                  <SpaceLogo spaceKey={space.key} logoUrl={space.logoUrl} />
                  {space.name}
                </span>
              ),
            },
            { key: 'key', header: t('spaces.key'), render: (space) => space.key },
            { key: 'deleted', header: t('spaceArchive.deletedAt'), render: (space) => formatDate(space.deletedAt, i18n.language) },
            { key: 'deletedBy', header: t('spaceArchive.deletedBy'), render: (space) => space.deletedBy?.name },
            { key: 'purge', header: t('spaceArchive.purgeAt'), render: (space) => formatDate(space.purgeAt, i18n.language) },
            {
              key: 'actions',
              header: '',
              render: (space) => (
                <Button
                  kind="ghost"
                  aria-label={`${t('spaceArchive.restoreFromTrash')}: ${space.name}`}
                  disabled={restoreDeleted.isPending}
                  onClick={() =>
                    restoreDeleted.mutate(space.id!, {
                      onSuccess: () => toast('success', t('spaceArchive.restoredFromTrash')),
                      onError: (failure) => toast('error', toMessage(failure)),
                    })
                  }
                >
                  {t('spaceArchive.restoreFromTrash')}
                </Button>
              ),
            },
          ]}
        />
      )}
    </>
  );
}
