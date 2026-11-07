import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, GripVertical, Trash2, TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { DialogActions } from '../../components/Dialog';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { useStatuses } from '../issues/api';
import type { Space } from '../spaces/api';
import { boardKey, useBoard } from './api';

interface DraftColumn {
  key: number;
  name: string;
  minLimit: string;
  maxLimit: string;
  isHidden: boolean;
  statusIds: string[];
}

export const CARD_FIELDS = ['type', 'key', 'priority', 'assignee', 'epic', 'labels', 'points'];

let nextKey = 1;

interface ColumnRowProps {
  column: DraftColumn;
  canManage: boolean;
  free: { id?: string; name?: string | null }[];
  nameOf: (id: string) => string;
  onChange: (change: Partial<DraftColumn>) => void;
  onRemove: () => void;
}

function ColumnRow({ column, canManage, free, nameOf, onChange, onRemove }: ColumnRowProps) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: column.key, disabled: !canManage });
  const hideLabel = `${t(column.isHidden ? 'agile.showColumn' : 'agile.hideColumn')} ${column.name}`;

  return (
    <div
      ref={setNodeRef}
      data-testid={`column-row-${column.name}`}
      className="sb-card flex items-start gap-2 p-2"
      style={{
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        transition,
        opacity: isDragging ? 0.6 : column.isHidden ? 0.7 : 1,
        backgroundColor: 'var(--sb-card-bg)',
        border: `1px solid ${sem('border-default')}`,
        borderRadius: radius.control,
      }}
    >
      {canManage && (
        <IconButton size="small" aria-label={`${t('agile.moveColumn')} ${column.name}`} sx={{ cursor: 'grab' }} {...attributes} {...listeners}>
          <GripVertical size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      )}
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-center gap-2">
          <div className="min-w-0 flex-1">
            <TextField quiet label={`${t('agile.column')} ${column.name}`} disabled={!canManage} value={column.name} sx={{ width: '100%' }} onChange={(event) => onChange({ name: event.target.value })} />
          </div>
          <TextField label={`${t('agile.min')} ${column.name}`} quiet placeholder={t('agile.min')} type="number" disabled={!canManage} sx={{ width: 64 }} value={column.minLimit} onChange={(event) => onChange({ minLimit: event.target.value })} />
          <TextField label={`${t('agile.max')} ${column.name}`} quiet placeholder={t('agile.max')} type="number" disabled={!canManage} sx={{ width: 64 }} value={column.maxLimit} onChange={(event) => onChange({ maxLimit: event.target.value })} />
        </div>
        <div className="flex flex-wrap items-center gap-1 px-2">
          {column.statusIds.map((id) => (
            <button
              key={id}
              type="button"
              disabled={!canManage}
              aria-label={t('agile.removeStatus', { name: nameOf(id) })}
              onClick={() => onChange({ statusIds: column.statusIds.filter((item) => item !== id) })}
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
              onChange={(event) => onChange({ statusIds: [...column.statusIds, event.target.value] })}
              options={[{ value: '', label: t('agile.addStatus') }, ...free.map((status) => ({ value: status.id!, label: status.name! }))]}
            />
          )}
        </div>
      </div>
      {canManage && (
        <>
          <Tooltip title={hideLabel}>
            <IconButton size="small" aria-label={hideLabel} onClick={() => onChange({ isHidden: !column.isHidden })}>
              {column.isHidden ? <EyeOff size={16} strokeWidth={1.5} aria-hidden /> : <Eye size={16} strokeWidth={1.5} aria-hidden />}
            </IconButton>
          </Tooltip>
          <Tooltip title={`${t('spaces.remove')} ${column.name}`}>
            <IconButton size="small" aria-label={`${t('spaces.remove')} ${column.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={onRemove}>
              <Trash2 size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </Tooltip>
        </>
      )}
    </div>
  );
}

/** The whole board setup, shared by space settings and the board's own Configure Columns. */
export function ColumnsEditor({ space, canManage, onDone }: { space: Space; canManage: boolean; onDone?: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const board = useBoard(space.id);
  const statuses = useStatuses(space.id);
  const [columns, setColumns] = useState<DraftColumn[] | null>(null);
  const [fields, setFields] = useState<string[]>([]);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  useEffect(() => {
    if (board.data && !columns) {
      setFields(board.data.cardFields ?? []);
      setColumns(
        (board.data.columns ?? []).map((column) => ({
          key: nextKey++,
          name: column.name ?? '',
          minLimit: column.minLimit?.toString() ?? '',
          maxLimit: column.maxLimit?.toString() ?? '',
          isHidden: column.isHidden ?? false,
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
            cardFields: fields,
            columns: (columns ?? []).map((column) => ({
              name: column.name,
              minLimit: column.minLimit ? Number(column.minLimit) : null,
              maxLimit: column.maxLimit ? Number(column.maxLimit) : null,
              isHidden: column.isHidden,
              statusIds: column.statusIds,
            })),
          },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: boardKey(space.id) });
      setColumns(null);
      toast('success', t('account.saved'));
      onDone?.();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const patch = (key: number, change: Partial<DraftColumn>) =>
    setColumns((current) => current?.map((column) => (column.key === key ? { ...column, ...change } : column)) ?? null);

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over && active.id !== over.id) {
      setColumns((current) => {
        const list = current ?? [];
        return arrayMove(list, list.findIndex((item) => item.key === active.id), list.findIndex((item) => item.key === over.id));
      });
    }
  };

  const nameOf = (id: string) => statuses.data?.find((status) => status.id === id)?.name ?? id;
  const assigned = (columns ?? []).flatMap((column) => column.statusIds);
  const known = [...(board.data?.columns ?? []).flatMap((column) => column.statuses ?? []), ...(board.data?.unmappedStatuses ?? [])];
  const free = known.filter((status, index) => !assigned.includes(status.id!) && known.findIndex((item) => item.id === status.id) === index);

  return (
    <div className="flex flex-col gap-4">
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
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
          <SortableContext items={columns.map((column) => column.key)} strategy={verticalListSortingStrategy}>
            <div className="flex flex-col gap-2">
              {columns.map((column) => (
                <ColumnRow
                  key={column.key}
                  column={column}
                  canManage={canManage}
                  free={free}
                  nameOf={nameOf}
                  onChange={(change) => patch(column.key, change)}
                  onRemove={() => setColumns(columns.filter((item) => item.key !== column.key))}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
      {columns && canManage && (
        <div>
          <Button kind="secondary" onClick={() => setColumns([...columns, { key: nextKey++, name: t('agile.newColumn'), minLimit: '', maxLimit: '', isHidden: false, statusIds: [] }])}>
            {t('agile.addColumn')}
          </Button>
        </div>
      )}
      {columns && (
        <div className="flex flex-col gap-1">
          <Typography variant="h3" component="h3">{t('agile.cardFields')}</Typography>
          <div role="group" aria-label={t('agile.cardFields')} className="flex flex-wrap gap-x-4">
            {CARD_FIELDS.map((field) => (
              <Checkbox
                key={field}
                label={t(`agile.cardField.${field}`)}
                disabled={!canManage}
                checked={fields.includes(field)}
                onChange={(event) => setFields(event.target.checked ? [...fields, field] : fields.filter((item) => item !== field))}
              />
            ))}
          </div>
        </div>
      )}
      {columns && canManage && (
        <DialogActions>
          <div className="flex justify-end gap-2">
            {onDone && <Button kind="ghost" onClick={onDone}>{t('common.cancel')}</Button>}
            <Button disabled={save.isPending || columns.some((column) => !column.name.trim())} onClick={() => save.mutate()}>{t('agile.saveChanges')}</Button>
          </div>
        </DialogActions>
      )}
    </div>
  );
}
