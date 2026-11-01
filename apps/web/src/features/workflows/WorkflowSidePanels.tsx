import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery } from '@tanstack/react-query';
import { GripVertical } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime } from '../../lib/format';
import { radius, sem } from '../../theme/tokens';

type Detail = components['schemas']['WorkflowDetailDto'];
type Version = components['schemas']['WorkflowVersionDto'];

const DRAFT = 0;
const PUBLISHED = 1;

function StatusRow({ id, label, onSelect }: { id: string; label: string; onSelect: () => void }) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });

  return (
    <div
      ref={setNodeRef}
      data-testid={`status-row-${label}`}
      className="flex items-center gap-1"
      style={{ transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined, transition, opacity: isDragging ? 0.6 : 1 }}
    >
      <IconButton size="small" aria-label={t('designer.reorder', { name: label })} sx={{ cursor: 'grab' }} {...attributes} {...listeners}>
        <GripVertical size={16} strokeWidth={1.5} aria-hidden />
      </IconButton>
      <Link component="button" variant="body2" sx={{ minHeight: 24 }} onClick={onSelect}>{label}</Link>
    </div>
  );
}

/** The order here is the order every status menu of this workflow follows. */
export function StatusOrderList({ steps, onSelect, onChange }: {
  steps: { value: string; label: string }[];
  onSelect: (id: string) => void;
  onChange: (statusIds: string[]) => void;
}) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const ids = steps.map((step) => step.value);

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over && active.id !== over.id) {
      onChange(arrayMove(ids, ids.indexOf(String(active.id)), ids.indexOf(String(over.id))));
    }
  };

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <div className="flex flex-col gap-1">
          {steps.map((step) => <StatusRow key={step.value} id={step.value} label={step.label} onSelect={() => onSelect(step.value)} />)}
        </div>
      </SortableContext>
    </DndContext>
  );
}

export function WorkflowHistory({ workflowId, onRestored, onClose }: { workflowId: string; onRestored: (detail: Detail) => void; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const [restoring, setRestoring] = useState<Version | null>(null);

  const versions = useQuery({
    queryKey: ['admin', 'workflows', workflowId, 'versions'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow/{id}/versions', { params: { path: { id: workflowId } } })),
  });

  const restore = useMutation({
    mutationFn: (version: Version) =>
      unwrap(apiClient.POST('/api/v1/workflow/{id}/restore', { params: { path: { id: workflowId } }, body: { versionId: version.id! } })),
    onSuccess: (detail, version) => {
      toast('success', t('designer.restored', { version: version.versionNumber }));
      onRestored(detail);
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setRestoring(null),
  });

  return (
    <>
      <Typography variant="caption" color="textSecondary">{t('designer.history')}</Typography>
      {versions.isPending && <LoadingState />}
      {versions.isError && <ErrorState onRetry={() => void versions.refetch()} />}
      {versions.data?.map((version) => (
        <div key={version.id} data-testid={`version-${version.versionNumber}`} className="flex flex-col gap-1 p-2" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.control }}>
          <div className="flex items-center justify-between gap-2">
            <Typography variant="body2">{t('designer.published', { version: version.versionNumber })}</Typography>
            {version.state === DRAFT && <Badge tone="warning">{t('designer.draft')}</Badge>}
            {version.state === PUBLISHED && <Badge tone="success">{t('workflows.publishedState')}</Badge>}
          </div>
          <Typography variant="caption" color="textSecondary">
            {t('designer.versionSummary', { statuses: version.statusCount, transitions: version.transitionCount })}
          </Typography>
          {version.publishedAt && <Typography variant="caption" color="textSecondary">{formatDateTime(version.publishedAt, i18n.language)}</Typography>}
          {version.state !== DRAFT && (
            <Button kind="secondary" sx={{ alignSelf: 'flex-start' }} onClick={() => setRestoring(version)}>{t('designer.restore')}</Button>
          )}
        </div>
      ))}
      <Button kind="ghost" sx={{ alignSelf: 'flex-end' }} onClick={onClose}>{t('common.close')}</Button>
      <DialogConfirm
        open={restoring !== null}
        title={t('designer.restoreTitle', { version: restoring?.versionNumber })}
        description={t('designer.restoreBody')}
        confirmLabel={t('designer.restore')}
        onCancel={() => setRestoring(null)}
        onConfirm={() => restoring && !restore.isPending && restore.mutate(restoring)}
      />
    </>
  );
}
