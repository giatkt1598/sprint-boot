import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { boardKey, useBoard } from '../agile/api';
import { useStatuses } from '../issues/api';
import { SpacePermission, type Space } from './api';

interface DraftColumn {
  key: number;
  name: string;
  minLimit: string;
  maxLimit: string;
  statusIds: string[];
}

let nextKey = 1;

export function SpaceBoardPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const board = useBoard(space.id);
  const statuses = useStatuses();
  const canManage = space.permissions?.includes(SpacePermission.manage) ?? false;
  const [columns, setColumns] = useState<DraftColumn[] | null>(null);

  useEffect(() => {
    if (board.data && !columns) {
      setColumns(
        (board.data.columns ?? []).map((column) => ({
          key: nextKey++,
          name: column.name ?? '',
          minLimit: column.minLimit?.toString() ?? '',
          maxLimit: column.maxLimit?.toString() ?? '',
          statusIds: (column.statuses ?? []).map((status) => status.id!),
        })),
      );
    }
  }, [board.data, columns]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.PUT('/api/v1/board/columns/{projectId}', {
          params: { path: { projectId: space.id! } },
          body: {
            columns: (columns ?? []).map((column) => ({
              name: column.name,
              minLimit: column.minLimit ? Number(column.minLimit) : null,
              maxLimit: column.maxLimit ? Number(column.maxLimit) : null,
              statusIds: column.statusIds,
            })),
          },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: boardKey(space.id) });
      setColumns(null);
      toast('success', t('account.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const patch = (key: number, change: Partial<DraftColumn>) =>
    setColumns((current) => current?.map((column) => (column.key === key ? { ...column, ...change } : column)) ?? null);

  const nameOf = (id: string) => statuses.data?.find((status) => status.id === id)?.name ?? id;
  const assigned = (columns ?? []).flatMap((column) => column.statusIds);
  const known = [...(board.data?.columns ?? []).flatMap((column) => column.statuses ?? []), ...(board.data?.unmappedStatuses ?? [])];
  const free = known.filter((status, index) => !assigned.includes(status.id!) && known.findIndex((item) => item.id === status.id) === index);

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('agile.board')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('agile.boardHint')}</Typography>
      </div>
      {board.isPending && <LoadingState />}
      {board.isError && <ErrorState onRetry={() => void board.refetch()} />}
      {[
        ...free.map((status) => t('agile.unmappedWarning', { name: status.name })),
        ...(board.data?.staleStatuses ?? []).map((status) => t('agile.staleWarning', { name: status.name })),
      ].map((message) => (
        <div
          key={message}
          role="alert"
          className="flex items-center gap-2 p-3"
          style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.control }}
        >
          <TriangleAlert size={16} strokeWidth={1.5} aria-hidden />
          <Typography variant="body2">{message}</Typography>
        </div>
      ))}
      {columns && (
        <Table<DraftColumn>
          label={t('agile.board')}
          rows={columns}
          rowKey={(column) => column.key}
          columns={[
            {
              key: 'name',
              header: t('agile.column'),
              render: (column) => <TextField quiet label={`${t('agile.column')} ${column.name}`} disabled={!canManage} value={column.name} onChange={(event) => patch(column.key, { name: event.target.value })} />,
            },
            {
              key: 'statuses',
              header: t('workflows.statuses'),
              render: (column) => (
                <div className="flex flex-wrap items-center gap-1">
                  {column.statusIds.map((id) => (
                    <button
                      key={id}
                      type="button"
                      disabled={!canManage}
                      aria-label={t('agile.removeStatus', { name: nameOf(id) })}
                      onClick={() => patch(column.key, { statusIds: column.statusIds.filter((item) => item !== id) })}
                      style={{ background: 'none', border: 0, padding: 0, cursor: canManage ? 'pointer' : 'default' }}
                    >
                      <Badge tone="neutral">{nameOf(id)}</Badge>
                    </button>
                  ))}
                  {canManage && free.length > 0 && (
                    <SelectField
                      quiet
                      label={t('agile.addStatusTo', { name: column.name })}
                      value=""
                      displayEmpty
                      onChange={(event) => patch(column.key, { statusIds: [...column.statusIds, event.target.value] })}
                      options={[{ value: '', label: t('agile.addStatus') }, ...free.map((status) => ({ value: status.id!, label: status.name! }))]}
                    />
                  )}
                </div>
              ),
            },
            {
              key: 'min',
              header: t('agile.min'),
              render: (column) => <TextField label={`${t('agile.min')} ${column.name}`} quiet type="number" disabled={!canManage} sx={{ width: 72 }} value={column.minLimit} onChange={(event) => patch(column.key, { minLimit: event.target.value })} />,
            },
            {
              key: 'max',
              header: t('agile.max'),
              render: (column) => <TextField label={`${t('agile.max')} ${column.name}`} quiet type="number" disabled={!canManage} sx={{ width: 72 }} value={column.maxLimit} onChange={(event) => patch(column.key, { maxLimit: event.target.value })} />,
            },
            {
              key: 'remove',
              header: '',
              render: (column) => canManage && (
                <Button kind="ghost" aria-label={`${t('spaces.remove')} ${column.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setColumns((columns ?? []).filter((item) => item.key !== column.key))}>
                  {t('spaces.remove')}
                </Button>
              ),
            },
          ]}
        />
      )}
      {columns && canManage && (
        <div className="flex justify-between">
          <Button kind="secondary" onClick={() => setColumns([...columns, { key: nextKey++, name: t('agile.newColumn'), minLimit: '', maxLimit: '', statusIds: [] }])}>{t('agile.addColumn')}</Button>
          <Button disabled={save.isPending || columns.some((column) => !column.name.trim())} onClick={() => save.mutate()}>{t('agile.saveChanges')}</Button>
        </div>
      )}
    </div>
  );
}
