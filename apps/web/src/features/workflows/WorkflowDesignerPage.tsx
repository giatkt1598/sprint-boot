import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ConnectionMode, Controls, ReactFlow, useNodesState, type Connection, type Edge, type Node, type ReactFlowInstance } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { ArrowLeft, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { ApiError, apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Toggle } from '../../components/Checkbox';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatRelative } from '../../lib/format';
import { radius, sem } from '../../theme/tokens';
import { useStatuses, type Status } from '../issues/api';
import { StatusOrderList, WorkflowHistory } from './WorkflowSidePanels';
import { TransitionRules, type DraftRule } from './TransitionRules';
import { ANY, arrow, edgeTypes, facingSides, nodeTypes, parallelOffset } from './WorkflowCanvas';
import { arrangeStatuses, replaceStatus, unreachableStatuses, type DraftTransition } from './workflowGraph';

type Definition = components['schemas']['WorkflowDefinitionDto'];
type Point = { x: number; y: number };
type NewTransition = Omit<DraftTransition, 'uid'>;

interface Draft {
  initialStatusId: string;
  statusIds: string[];
  transitions: DraftTransition[];
}

type Panel =
  | { kind: 'status'; id: string }
  | { kind: 'transition'; uid: number }
  | { kind: 'new-status' }
  | { kind: 'history' }
  | { kind: 'new-transition'; value: NewTransition }
  | null;

const AUTOSAVE_DELAY = 800;
const NODE_GAP_X = 272;
const NODE_GAP_Y = 164;
const ANY_ABOVE = 164;
const GROUPS = [
  { value: 0, label: 'issue.status.todo' },
  { value: 1, label: 'issue.status.inProgress' },
  { value: 2, label: 'issue.status.done' },
];

let nextUid = 1;

const toDraft = (definition: Definition | null | undefined): Draft => ({
  initialStatusId: definition?.initialStatusId ?? '',
  statusIds: definition?.statusIds ?? [],
  transitions: (definition?.transitions ?? []).map((transition) => ({
    uid: nextUid++,
    name: transition.name ?? '',
    fromStatusId: transition.fromStatusId ?? null,
    toStatusId: transition.toStatusId ?? '',
    rules: (transition.rules ?? []) as DraftRule[],
  })),
});

const toDefinition = (draft: Draft, positions: Record<string, Point>): Definition => ({
  initialStatusId: draft.initialStatusId,
  statusIds: draft.statusIds,
  transitions: draft.transitions.map(({ name, fromStatusId, toStatusId, rules }) => ({ name, fromStatusId, toStatusId, rules })) as Definition['transitions'],
  layoutJson: JSON.stringify({ positions }),
});

const readPositions = (layoutJson: string | null | undefined): Record<string, Point> => {
  try {
    return (JSON.parse(layoutJson ?? '{}') as { positions?: Record<string, Point> }).positions ?? {};
  } catch {
    return {};
  }
};

const defaultPosition = (index: number): Point => ({ x: 80 + (index % 4) * NODE_GAP_X, y: 240 + Math.floor(index / 4) * NODE_GAP_Y });

const statusNodes = (draft: Draft, stored: Record<string, Point>): Node[] =>
  // The start status leads, so a workflow without a saved layout still reads left to right.
  [draft.initialStatusId, ...draft.statusIds.filter((id) => id !== draft.initialStatusId)]
    .filter((id) => draft.statusIds.includes(id))
    .map((id, index) => ({ id, type: 'status', position: stored[id] ?? defaultPosition(index), data: {} }));

export function WorkflowDesignerPage() {
  const { t, i18n } = useTranslation();
  const { workflowId = '' } = useParams();
  const [params] = useSearchParams();
  const spaceKey = params.get('space');
  const backTo = spaceKey ? `/spaces/${spaceKey}/settings/workflows` : '/admin/workflows';
  const navigate = useNavigate();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();

  const detail = useQuery({
    queryKey: ['admin', 'workflows', workflowId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow/{id}', { params: { path: { id: workflowId } } })),
  });
  const projectId = detail.data?.projectId ?? undefined;
  const statuses = useStatuses(projectId);
  const summaries = useQuery({
    queryKey: ['admin', 'workflows', 'list', projectId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow', { params: { query: { projectId } } })),
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [panel, setPanel] = useState<Panel>(null);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [saved, setSaved] = useState('');
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [orphaned, setOrphaned] = useState<string[]>([]);
  const [mappings, setMappings] = useState<Record<string, string>>({});
  const [deleting, setDeleting] = useState<Status | null>(null);
  const importInput = useRef<HTMLInputElement>(null);
  const flow = useRef<ReactFlowInstance | null>(null);
  const statusCount = draft?.statusIds.length ?? 0;

  // A status that just arrived may sit beyond the visible canvas.
  useEffect(() => {
    const timer = setTimeout(() => void flow.current?.fitView({ maxZoom: 1, duration: 200 }), 50);
    return () => clearTimeout(timer);
  }, [statusCount]);

  useEffect(() => {
    if (detail.data && !draft) {
      const definition = detail.data.draft ?? detail.data.published;
      const loaded = toDraft(definition);
      const placed = statusNodes(loaded, readPositions(definition?.layoutJson));
      setDraft(loaded);
      setNodes(placed);
      setSaved(JSON.stringify({ draft: loaded, positions: Object.fromEntries(placed.map((node) => [node.id, node.position])) }));
    }
  }, [detail.data, draft, setNodes]);

  const statusOf = (id: string) => statuses.data?.find((status) => status.id === id);
  const defaultName = (from: string | null, to: string) =>
    t('designer.defaultName', { from: from ? (statusOf(from)?.name ?? '') : t('designer.anyStatus'), to: statusOf(to)?.name ?? '' });

  const broken = useMemo(() => (draft ? unreachableStatuses(draft) : []), [draft]);
  const hasDone = draft?.statusIds.some((id) => statusOf(id)?.category === 2) ?? false;
  const problems = [
    ...broken.map((id) => t('designer.unreachable', { name: statusOf(id)?.name ?? id })),
    ...(draft && statuses.data && !hasDone ? [t('errors.WorkflowNeedsDoneStatus')] : []),
  ];
  const activeTransition = panel?.kind === 'transition' ? draft?.transitions.find((item) => item.uid === panel.uid) : undefined;

  // Status nodes keep the positions the canvas owns; only their content follows the draft.
  useEffect(() => {
    if (!draft) {
      return;
    }
    const anyTargets = [...new Set(draft.transitions.filter((item) => item.fromStatusId === null).map((item) => item.toStatusId))];
    setNodes((current) => {
      const steps = draft.statusIds.map((id, index) => {
        const status = statuses.data?.find((item) => item.id === id);
        return {
          ...(current.find((node) => node.id === id) ?? { id, position: defaultPosition(index) }),
          type: 'status',
          selected: panel?.kind === 'status' && panel.id === id,
          data: {
            name: status?.name ?? '',
            category: status?.category ?? 0,
            isStart: draft.initialStatusId === id,
            isBroken: broken.includes(id),
            startLabel: t('designer.start'),
          },
        };
      });
      const anyNodes = anyTargets.map((target) => {
        const id = `${ANY}:${target}`;
        const anchor = steps.find((node) => node.id === target)?.position ?? { x: 0, y: 0 };
        return {
          ...(current.find((node) => node.id === id) ?? { id, position: { x: anchor.x, y: anchor.y - ANY_ABOVE } }),
          type: 'any',
          selectable: false,
          data: { label: t('designer.anyStatus'), active: activeTransition?.fromStatusId === null && activeTransition.toStatusId === target },
        };
      });
      return [...steps, ...anyNodes] as Node[];
    });
  }, [draft, panel, activeTransition, broken, statuses.data, setNodes, t]);

  const edges = useMemo<Edge[]>(() => {
    const items = draft?.transitions ?? [];
    const place = (id: string) => nodes.find((node) => node.id === id)?.position ?? { x: 0, y: 0 };
    const pairOf = (from: string, to: string) => [from, to].sort().join('|');
    const sourceOf = (item: DraftTransition) => item.fromStatusId ?? `${ANY}:${item.toStatusId}`;

    return items.map((transition) => {
      const source = sourceOf(transition);
      const twins = items.filter((item) => pairOf(sourceOf(item), item.toStatusId) === pairOf(source, transition.toStatusId));
      return {
        id: String(transition.uid),
        type: 'transition',
        source,
        target: transition.toStatusId,
        ...facingSides(place(source), place(transition.toStatusId)),
        label: transition.name || defaultName(transition.fromStatusId, transition.toStatusId),
        selected: activeTransition?.uid === transition.uid,
        markerEnd: arrow,
        data: {
          offset: parallelOffset(twins.indexOf(transition), twins.length, source > transition.toStatusId),
          dashed: transition.fromStatusId === null,
          onSelect: () => setPanel({ kind: 'transition', uid: transition.uid }),
        },
      };
    });
  }, [draft, nodes, activeTransition, statuses.data, t]);

  const placeOf = (statusIds: string[]) =>
    Object.fromEntries(statusIds.flatMap((id) => {
      const node = nodes.find((item) => item.id === id);
      return node ? [[id, node.position]] : [];
    })) as Record<string, Point>;

  const snapshot = JSON.stringify({ draft, positions: placeOf(draft?.statusIds ?? []) });
  const dirty = Boolean(draft) && snapshot !== saved;

  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => {
      if (dirty) {
        event.preventDefault();
      }
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const writeDraft = async (next: Draft) => {
    const positions = placeOf(next.statusIds);
    await unwrap(apiClient.POST('/api/v1/workflow/{id}/save-draft', { params: { path: { id: workflowId } }, body: toDefinition(next, positions) }));
    setSaved(JSON.stringify({ draft: next, positions }));
    setSavedAt(new Date().toISOString());
    await queryClient.invalidateQueries({ queryKey: ['admin', 'workflows'] });
  };

  // Scoped so a slow save cannot land after a newer one.
  const save = useMutation({
    scope: { id: `workflow-draft-${workflowId}` },
    mutationFn: writeDraft,
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const { mutate: autosave } = save;

  useEffect(() => {
    if (!dirty || !draft?.initialStatusId) {
      return;
    }
    const timer = setTimeout(() => autosave(draft), AUTOSAVE_DELAY);
    return () => clearTimeout(timer);
  }, [snapshot, autosave]);

  const goBack = async () => {
    try {
      if (dirty) {
        await save.mutateAsync(draft!);
      }
      await navigate(backTo);
    } catch {
      // The failed save already said why, and staying keeps the work.
    }
  };

  const closePublish = () => {
    setPublishing(false);
    setOrphaned([]);
    setMappings({});
  };

  const publish = useMutation({
    scope: { id: `workflow-draft-${workflowId}` },
    mutationFn: async () => {
      await writeDraft(draft!);
      return unwrap(apiClient.POST('/api/v1/workflow/{id}/publish', { params: { path: { id: workflowId } }, body: { statusMappings: mappings } }));
    },
    onSuccess: async (result) => {
      closePublish();
      await queryClient.invalidateQueries({ queryKey: ['admin', 'workflows'] });
      toast('success', t(result.migrationQueued ? 'workflows.publishedQueued' : 'workflows.published'));
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.code?.endsWith('StatusMappingRequired')) {
        setOrphaned((failure.data.statuses ?? '').split(',').filter(Boolean));
      } else {
        closePublish();
        toast('error', toMessage(failure));
      }
    },
  });

  const patchTransition = (uid: number, change: Partial<DraftTransition>) =>
    setDraft((current) => current && { ...current, transitions: current.transitions.map((item) => (item.uid === uid ? { ...item, ...change } : item)) });

  const openNewTransition = (fromStatusId: string | null, toStatusId: string) =>
    setPanel({ kind: 'new-transition', value: { name: '', fromStatusId, toStatusId, rules: [] } });

  const addTransition = (value: NewTransition) => {
    const uid = nextUid++;
    setDraft((current) => current && { ...current, transitions: [...current.transitions, { ...value, uid }] });
    setPanel({ kind: 'transition', uid });
  };

  const onConnect = ({ source, target }: Connection) => {
    if (source && target && source !== target && !target.startsWith(`${ANY}:`)) {
      openNewTransition(source.startsWith(`${ANY}:`) ? null : source, target);
    }
  };

  const addStatus = (id: string) => {
    // Reachable from the start, so a new status never blocks publishing.
    const transition = { uid: nextUid++, name: '', fromStatusId: null, toStatusId: id, rules: [] };
    setDraft((current) => current && {
      ...current,
      statusIds: [...current.statusIds, id],
      initialStatusId: current.initialStatusId || id,
      transitions: [...current.transitions, transition],
    });
    setPanel({ kind: 'status', id });
  };

  const withoutStatus = (current: Draft, id: string): Draft => ({
    ...current,
    statusIds: current.statusIds.filter((item) => item !== id),
    initialStatusId: current.initialStatusId === id ? (current.statusIds.find((item) => item !== id) ?? '') : current.initialStatusId,
    transitions: current.transitions.filter((item) => item.toStatusId !== id && item.fromStatusId !== id),
  });

  const onStatusDeleted = async (status: Status, replacementId: string | null) => {
    setDraft((current) => current && replaceStatus(current, status.id!, replacementId));
    if (replacementId) {
      // The replacement takes the deleted status's place on the canvas unless it is already there.
      setNodes((current) =>
        current.some((node) => node.id === replacementId)
          ? current
          : current.map((node) => (node.id === status.id ? { ...node, id: replacementId } : node)),
      );
    }
    setDeleting(null);
    setPanel(null);
    await Promise.all([queryClient.invalidateQueries({ queryKey: ['statuses'] }), queryClient.invalidateQueries({ queryKey: ['admin', 'workflows'] })]);
  };

  const beautify = () => {
    const order = [...draft!.statusIds].sort((first, second) =>
      Number(second === draft!.initialStatusId) - Number(first === draft!.initialStatusId)
      || (statusOf(first)?.category ?? 0) - (statusOf(second)?.category ?? 0));
    const places = arrangeStatuses(order, draft!.transitions);
    // Any status nodes sit above their target, so rows leave room for them.
    const rowGap = draft!.transitions.some((item) => item.fromStatusId === null) ? NODE_GAP_Y * 2 : NODE_GAP_Y;
    const point = (id: string): Point => {
      const place = places.get(id);
      return { x: 80 + (place?.column ?? 0) * NODE_GAP_X, y: 240 + (place?.row ?? 0) * rowGap };
    };
    setNodes((current) =>
      current.map((node) => {
        if (node.type !== 'any') {
          return { ...node, position: point(node.id) };
        }
        const target = point(node.id.slice(ANY.length + 1));
        return { ...node, position: { x: target.x, y: target.y - ANY_ABOVE } };
      }),
    );
    setTimeout(() => void flow.current?.fitView({ maxZoom: 1, duration: 200 }), 50);
  };

  const exportJson = () => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([JSON.stringify(toDefinition(draft!, placeOf(draft!.statusIds)), null, 2)], { type: 'application/json' }));
    link.download = `${detail.data?.name ?? 'workflow'}.json`;
    link.click();
    URL.revokeObjectURL(link.href);
  };

  const importJson = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    try {
      const definition = JSON.parse((await file?.text()) ?? '') as Definition;
      if (!definition.initialStatusId || !Array.isArray(definition.statusIds) || !Array.isArray(definition.transitions)) {
        throw new Error('Not a workflow definition');
      }
      const known = (statuses.data ?? []).map((status) => status.id);
      if (!definition.statusIds.every((id) => known.includes(id))) {
        throw new Error('Unknown status');
      }
      const imported = toDraft(definition);
      setDraft(imported);
      setPanel(null);
      setNodes(statusNodes(imported, readPositions(definition.layoutJson)));
    } catch {
      toast('error', t('designer.importFailed'));
    }
  };

  const workflow = detail.data;
  if (!workflow || !draft) {
    return <main className="flex-1 p-8">{detail.isError ? <ErrorState onRetry={() => void detail.refetch()} /> : <LoadingState />}</main>;
  }

  const selectedStatus = panel?.kind === 'status' ? statusOf(panel.id) : undefined;
  const steps = draft.statusIds.map((id) => ({ value: id, label: statusOf(id)?.name ?? id }));
  const spare = (statuses.data ?? []).filter((status) => !draft.statusIds.includes(status.id!));
  const nextVersion = (workflow.version ?? 0) + 1;
  const spaceCount = summaries.data?.find((item) => item.id === workflowId)?.spaceCount ?? 0;
  const saveState = dirty || save.isPending ? t('designer.saving') : savedAt ? t('designer.saved', { time: formatRelative(savedAt, i18n.language) }) : '';

  return (
    <div className="flex min-h-0 min-w-0 flex-1 flex-col">
      <header className="flex items-center gap-2 px-4 py-2" style={{ backgroundColor: sem('bg-surface'), borderBottom: `1px solid ${sem('border-default')}` }}>
        <IconButton size="small" aria-label={t('designer.back')} onClick={() => void goBack()}>
          <ArrowLeft size={20} strokeWidth={1.5} aria-hidden />
        </IconButton>
        <Typography variant="h1" className="truncate">{workflow.name}</Typography>
        {workflow.draft || dirty ? (
          <Badge tone="warning">{t('designer.draftOf', { version: nextVersion })}</Badge>
        ) : (
          <Badge tone="success">{t('designer.published', { version: workflow.version })}</Badge>
        )}
        <span className="flex-1" />
        <Typography variant="body2" color="textSecondary" role="status">{saveState}</Typography>
        <Button kind="ghost" onClick={() => importInput.current?.click()}>{t('designer.import')}</Button>
        <input ref={importInput} type="file" accept="application/json" hidden aria-label={t('designer.import')} onChange={(event) => void importJson(event)} />
        <Button kind="ghost" onClick={exportJson}>{t('designer.export')}</Button>
        <Button kind="ghost" onClick={() => setPanel({ kind: 'history' })}>{t('designer.history')}</Button>
        <Button kind="ghost" onClick={beautify}>{t('designer.beautify')}</Button>
        <Button kind="secondary" onClick={() => setPanel({ kind: 'new-status' })}>{t('workflows.addStatus')}</Button>
        <Button kind="secondary" disabled={steps.length === 0} onClick={() => openNewTransition(steps[0]?.value ?? null, steps[1]?.value ?? steps[0]?.value ?? '')}>
          {t('designer.addTransition')}
        </Button>
        <Button disabled={problems.length > 0 || (!workflow.draft && !dirty)} onClick={() => setPublishing(true)}>{t('workflows.publish')}</Button>
      </header>
      <div className="flex min-h-0 flex-1">
        <Box
          component="main"
          className="relative min-w-0 flex-1"
          aria-label={t('designer.canvas')}
          sx={{ backgroundColor: sem('bg-page'), '& .react-flow__attribution a': { color: `${sem('text-secondary')} !important` } }}
        >
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            connectionMode={ConnectionMode.Loose}
            connectionRadius={80}
            onInit={(instance) => {
              flow.current = instance as unknown as ReactFlowInstance;
            }}
            onNodesChange={onNodesChange}
            onConnect={onConnect}
            onNodeClick={(_, node) => {
              if (node.type === 'status') {
                setPanel({ kind: 'status', id: node.id });
              } else {
                const target = node.id.slice(ANY.length + 1);
                const transition = draft.transitions.find((item) => item.fromStatusId === null && item.toStatusId === target);
                setPanel(transition ? { kind: 'transition', uid: transition.uid } : null);
              }
            }}
            onEdgeClick={(_, edge) => setPanel({ kind: 'transition', uid: Number(edge.id) })}
            onPaneClick={() => setPanel(null)}
            fitView
            fitViewOptions={{ maxZoom: 1 }}
          >
            <Controls showInteractive={false} />
          </ReactFlow>
          {problems.length > 0 && (
            <div className="absolute left-6 top-6 z-10 flex flex-col gap-2" style={{ width: 520 }}>
              {problems.map((problem) => (
                <div
                  key={problem}
                  role="alert"
                  className="flex items-start gap-2 p-3"
                  style={{ backgroundColor: sem('feedback-danger-bg'), color: sem('feedback-danger-text'), borderRadius: radius.control }}
                >
                  <TriangleAlert size={16} strokeWidth={1.5} aria-hidden className="mt-0.5 shrink-0" />
                  <Typography variant="body2" sx={{ color: 'inherit' }}>{problem}</Typography>
                </div>
              ))}
            </div>
          )}
        </Box>
        <aside
          aria-label={t('designer.details')}
          className="flex shrink-0 flex-col gap-3 overflow-y-auto p-4"
          style={{ width: 320, backgroundColor: sem('bg-surface'), borderLeft: `1px solid ${sem('border-default')}` }}
        >
          {activeTransition && (
            <TransitionForm
              key={activeTransition.uid}
              heading={t('designer.transition')}
              value={activeTransition}
              steps={steps}
              placeholder={defaultName(activeTransition.fromStatusId, activeTransition.toStatusId)}
              onChange={(change) => patchTransition(activeTransition.uid, change)}
            >
              <Button
                kind="danger"
                sx={{ alignSelf: 'flex-start' }}
                onClick={() => {
                  setDraft({ ...draft, transitions: draft.transitions.filter((item) => item.uid !== activeTransition.uid) });
                  setPanel(null);
                }}
              >
                {t('designer.deleteTransition')}
              </Button>
            </TransitionForm>
          )}
          {panel?.kind === 'new-transition' && (
            <TransitionForm
              heading={t('designer.newTransition')}
              value={panel.value}
              steps={steps}
              placeholder={defaultName(panel.value.fromStatusId, panel.value.toStatusId)}
              onChange={(change) => setPanel({ ...panel, value: { ...panel.value, ...change } })}
            >
              <div className="flex justify-end gap-2">
                <Button kind="ghost" onClick={() => setPanel(null)}>{t('common.cancel')}</Button>
                <Button onClick={() => addTransition(panel.value)}>{t('designer.addTransition')}</Button>
              </div>
            </TransitionForm>
          )}
          {selectedStatus && (
            <StatusPanel
              key={selectedStatus.id}
              status={selectedStatus}
              editable={(selectedStatus.projectId ?? null) === (projectId ?? null)}
              isStart={draft.initialStatusId === selectedStatus.id}
              onStart={() => setDraft({ ...draft, initialStatusId: selectedStatus.id! })}
              onRemove={() => {
                setDraft(withoutStatus(draft, selectedStatus.id!));
                setPanel(null);
              }}
              onDelete={() => setDeleting(selectedStatus)}
            />
          )}
          {panel?.kind === 'history' && (
            <WorkflowHistory
              workflowId={workflowId}
              onClose={() => setPanel(null)}
              onRestored={(restored) => {
                queryClient.setQueryData(['admin', 'workflows', workflowId], restored);
                setPanel(null);
                // Empty, so the load above takes the restored draft and its layout.
                setDraft(null);
                void queryClient.invalidateQueries({ queryKey: ['admin', 'workflows', workflowId, 'versions'] });
              }}
            />
          )}
          {panel?.kind === 'new-status' && (
            <NewStatusPanel projectId={projectId} spare={spare} onAdd={addStatus} onCancel={() => setPanel(null)} />
          )}
          {!panel && (
            <>
              <Typography variant="body2" color="textSecondary">{t('designer.hint')}</Typography>
              <Typography variant="h3" component="h2">{t('designer.transitions')}</Typography>
              <div className="flex flex-col items-start gap-1">
                {draft.transitions.map((transition) => (
                  <Link key={transition.uid} component="button" variant="body2" sx={{ minHeight: 24 }} onClick={() => setPanel({ kind: 'transition', uid: transition.uid })}>
                    {transition.name || defaultName(transition.fromStatusId, transition.toStatusId)}
                  </Link>
                ))}
              </div>
              <Typography variant="h3" component="h2">{t('workflows.statuses')}</Typography>
              <StatusOrderList
                steps={steps}
                onSelect={(id) => setPanel({ kind: 'status', id })}
                onChange={(statusIds) => setDraft({ ...draft, statusIds })}
              />
            </>
          )}
        </aside>
      </div>
      {deleting && (
        <DeleteStatusDialog
          status={deleting}
          isStart={draft.initialStatusId === deleting.id}
          statuses={statuses.data ?? []}
          beforeDelete={() => (dirty ? writeDraft(draft) : Promise.resolve())}
          onCancel={() => setDeleting(null)}
          onDeleted={(replacementId) => void onStatusDeleted(deleting, replacementId)}
        />
      )}
      {publishing && (
        <Dialog
          open
          title={t('designer.publishTitle', { version: nextVersion })}
          onClose={closePublish}
          footer={
            <DialogFooter
              confirmLabel={t('workflows.publish')}
              confirmDisabled={orphaned.some((id) => !mappings[id]) || publish.isPending}
              onCancel={closePublish}
              onConfirm={() => publish.mutate()}
            />
          }
        >
          <Typography variant="body1" color="textSecondary">
            {spaceCount > 0 ? t('designer.publishUsed', { count: spaceCount }) : t('designer.publishUnused')}
          </Typography>
          {orphaned.length > 0 && (
            <div
              role="alert"
              className="flex items-start gap-2 p-3"
              style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.control }}
            >
              <TriangleAlert size={16} strokeWidth={1.5} aria-hidden className="mt-0.5 shrink-0" />
              <Typography variant="body2" sx={{ color: 'inherit' }}>{t('workflows.mappingHint')}</Typography>
            </div>
          )}
          {orphaned.map((id) => (
            <div key={id} className="grid grid-cols-2 gap-4">
              <TextField label={t('designer.removedStatus')} disabled value={statusOf(id)?.name ?? id} />
              <SelectField
                label={t('designer.moveIssuesTo')}
                value={mappings[id] ?? ''}
                onChange={(event) => setMappings({ ...mappings, [id]: event.target.value })}
                options={steps}
              />
            </div>
          ))}
        </Dialog>
      )}
    </div>
  );
}

function TransitionForm({ heading, value, steps, placeholder, onChange, children }: {
  heading: string;
  placeholder: string;
  value: NewTransition;
  steps: { value: string; label: string }[];
  onChange: (change: Partial<NewTransition>) => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  const target = steps.find((step) => step.value === value.toStatusId)?.label;

  return (
    <>
      <Typography variant="caption" color="textSecondary">{heading}</Typography>
      <TextField label={t('account.name')} placeholder={placeholder} value={value.name} onChange={(event) => onChange({ name: event.target.value })} />
      <div className="grid grid-cols-2 gap-3">
        <SelectField
          label={t('designer.from')}
          value={value.fromStatusId ?? ANY}
          onChange={(event) => onChange({ fromStatusId: event.target.value === ANY ? null : event.target.value })}
          options={[{ value: ANY, label: t('designer.anyStatus') }, ...steps]}
        />
        <SelectField label={t('designer.to')} value={value.toStatusId} onChange={(event) => onChange({ toStatusId: event.target.value })} options={steps} />
      </div>
      {value.fromStatusId === null && <Typography variant="caption" color="textSecondary">{t('designer.anyHint', { name: target })}</Typography>}
      <TransitionRules rules={value.rules} onChange={(rules) => onChange({ rules })} />
      {children}
    </>
  );
}

function StatusPanel({ status, editable, isStart, onStart, onRemove, onDelete }: {
  status: Status;
  editable: boolean;
  isStart: boolean;
  onStart: () => void;
  onRemove: () => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState(status.name ?? '');
  const category = String(status.category ?? 0);

  const save = useMutation({
    mutationFn: (change: { name: string; category: number }) =>
      unwrap(apiClient.PUT('/api/v1/status/{id}', { params: { path: { id: status.id! } }, body: { name: change.name, category: change.category as 0 | 1 | 2 } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['statuses'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <>
      <Typography variant="caption" color="textSecondary">{t('designer.status')}</Typography>
      <TextField
        label={t('account.name')}
        disabled={!editable}
        value={name}
        onChange={(event) => setName(event.target.value)}
        onBlur={() => name.trim() && name !== status.name && save.mutate({ name, category: Number(category) })}
      />
      <SelectField
        label={t('workflows.group')}
        disabled={!editable}
        value={category}
        onChange={(event) => save.mutate({ name, category: Number(event.target.value) })}
        options={GROUPS.map((group) => ({ value: String(group.value), label: t(group.label) }))}
      />
      <Typography variant="caption" color="textSecondary">{t(editable ? 'designer.groupHint' : 'designer.statusGlobal')}</Typography>
      <div className="flex items-center justify-between gap-3">
        <div className="flex flex-col">
          <Typography variant="body2">{t('designer.startStatus')}</Typography>
          <Typography variant="caption" color="textSecondary">{t('designer.startHint')}</Typography>
        </div>
        <Toggle label="" slotProps={{ input: { 'aria-label': t('designer.startStatus') } }} checked={isStart} disabled={isStart} onChange={onStart} />
      </div>
      <Button kind="danger" sx={{ alignSelf: 'flex-start' }} disabled={isStart} onClick={onRemove}>{t('designer.removeStatus')}</Button>
      {editable && <Button kind="ghost" sx={{ alignSelf: 'flex-start' }} onClick={onDelete}>{t('designer.deleteStatus')}</Button>}
    </>
  );
}

function DeleteStatusDialog({ status, isStart, statuses, beforeDelete, onCancel, onDeleted }: {
  status: Status;
  isStart: boolean;
  statuses: Status[];
  beforeDelete: () => Promise<void>;
  onCancel: () => void;
  onDeleted: (replacementId: string | null) => void;
}) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [replacementId, setReplacementId] = useState('');

  const usage = useQuery({
    queryKey: ['statuses', 'usage', status.id],
    queryFn: () => unwrap(apiClient.GET('/api/v1/status/{id}/usage', { params: { path: { id: status.id! } } })),
    gcTime: 0,
  });

  const remove = useMutation({
    mutationFn: async () => {
      await beforeDelete();
      await unwrap(apiClient.DELETE('/api/v1/status/{id}', { params: { path: { id: status.id! }, query: { ReplacementId: replacementId || undefined } } }));
    },
    onSuccess: () => onDeleted(replacementId || null),
  });

  const needed = isStart || usage.data?.replacementRequired === true;
  // A global status reaches every space, so only another global one can stand in for it.
  const options = statuses.filter((item) => item.id !== status.id && (item.projectId ? item.projectId === status.projectId : true));

  return (
    <Dialog
      open
      title={t('designer.deleteStatusTitle', { name: status.name })}
      onClose={onCancel}
      footer={
        <DialogFooter
          confirmLabel={t('common.delete')}
          confirmDisabled={!usage.data || (needed && !replacementId) || remove.isPending}
          onCancel={onCancel}
          onConfirm={() => remove.mutate()}
        />
      }
    >
      {usage.isError ? (
        <Typography role="alert" color="error" variant="body2">{toMessage(usage.error)}</Typography>
      ) : usage.data ? (
        <Typography variant="body1" color="textSecondary">
          {needed
            ? t('designer.deleteStatusUsed', { issues: usage.data.issueCount, workflows: usage.data.workflowCount })
            : t('designer.deleteStatusBody')}
        </Typography>
      ) : (
        <LoadingState />
      )}
      {needed && (
        <SelectField
          label={t('designer.replacementStatus')}
          value={replacementId}
          onChange={(event) => setReplacementId(event.target.value)}
          options={options.map((item) => ({ value: item.id!, label: item.name! }))}
        />
      )}
      {remove.isError && <Typography role="alert" color="error" variant="body2">{toMessage(remove.error)}</Typography>}
    </Dialog>
  );
}

function NewStatusPanel({ projectId, spare, onAdd, onCancel }: {
  projectId?: string;
  spare: Status[];
  onAdd: (id: string) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [category, setCategory] = useState('1');

  const create = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/status', { body: { name, category: Number(category) as 0 | 1 | 2, projectId } })),
    onSuccess: async (status) => {
      await queryClient.invalidateQueries({ queryKey: ['statuses'] });
      onAdd(status.id!);
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <>
      <Typography variant="caption" color="textSecondary">{t('designer.newStatus')}</Typography>
      {spare.length > 0 && (
        <SelectField
          label={t('designer.existingStatus')}
          value=""
          displayEmpty
          onChange={(event) => event.target.value && onAdd(event.target.value)}
          options={[{ value: '', label: t('designer.pickStatus') }, ...spare.map((status) => ({ value: status.id!, label: status.name! }))]}
        />
      )}
      <TextField label={t('account.name')} value={name} onChange={(event) => setName(event.target.value)} />
      <SelectField
        label={t('workflows.group')}
        value={category}
        onChange={(event) => setCategory(event.target.value)}
        options={GROUPS.map((group) => ({ value: String(group.value), label: t(group.label) }))}
      />
      <div className="flex justify-end gap-2">
        <Button kind="ghost" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>{t('workflows.addStatus')}</Button>
      </div>
    </>
  );
}
