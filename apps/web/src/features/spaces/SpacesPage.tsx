import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { useSpaces, type Space } from './api';

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
      await navigate(`/spaces/${space.key}/issues`);
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

export function SpacesPage() {
  const { t, i18n } = useTranslation();
  const spaces = useSpaces();
  const [filter, setFilter] = useState('');
  const [creating, setCreating] = useState(false);

  const rows = (spaces.data ?? []).filter((space) =>
    `${space.name} ${space.key}`.toLowerCase().includes(filter.toLowerCase()),
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t('spaces.title')}</Typography>
        <SearchInput label={t('spaces.filter')} value={filter} onChange={(event) => setFilter(event.target.value)} />
        <Button onClick={() => setCreating(true)}>{t('spaces.create')}</Button>
      </div>
      {spaces.isPending && <LoadingState />}
      {spaces.isError && <ErrorState onRetry={() => void spaces.refetch()} />}
      {spaces.isSuccess && rows.length === 0 && <EmptyState title={t('spaces.empty')} />}
      {rows.length > 0 && (
        <Table<Space>
          label={t('spaces.title')}
          rows={rows}
          rowKey={(space) => space.id!}
          columns={[
            {
              key: 'name',
              header: t('spaces.name'),
              render: (space) => (
                <Link component={RouterLink} to={`/spaces/${space.key}/issues`} color="inherit">{space.name}</Link>
              ),
            },
            { key: 'key', header: t('spaces.key'), render: (space) => space.key },
            { key: 'type', header: t('spaces.type'), render: (space) => t(space.type === 0 ? 'spaces.scrum' : 'spaces.kanban') },
            {
              key: 'lead',
              header: t('spaces.lead'),
              render: (space) =>
                space.lead && (
                  <span className="flex items-center gap-2">
                    <Avatar name={space.lead.name ?? ''} />
                    {space.lead.name}
                  </span>
                ),
            },
            { key: 'issues', header: t('spaces.issues'), render: (space) => space.issueCount },
            { key: 'updated', header: t('spaces.updated'), render: (space) => formatDate(space.updatedAt, i18n.language) },
          ]}
        />
      )}
      {creating && <CreateSpaceDialog open onClose={() => setCreating(false)} />}
    </div>
  );
}
