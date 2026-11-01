import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { ApiError, apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { useStatuses, type Status } from '../issues/api';

type StatusRow = components['schemas']['StatusAdminDto'];
type WorkflowRow = components['schemas']['WorkflowSummaryDto'];
type Definition = components['schemas']['WorkflowDefinitionDto'];

const GROUPS = [
  { value: 0, label: 'issue.status.todo' },
  { value: 1, label: 'issue.status.inProgress' },
  { value: 2, label: 'issue.status.done' },
];

function PageHeader({ title, hint, children }: { title: string; hint: string; children?: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1">
        <Typography variant="h1">{title}</Typography>
        <Typography variant="body2" color="textSecondary">{hint}</Typography>
      </div>
      {children}
    </div>
  );
}

function ScopeBadge({ projectId }: { projectId?: string | null }) {
  const { t } = useTranslation();
  return <Badge tone={projectId ? 'info' : 'neutral'}>{t(projectId ? 'workflows.scopeSpace' : 'workflows.scopeGlobal')}</Badge>;
}

function StatusDialog({ status, onClose }: { status?: StatusRow; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState(status?.name ?? '');
  const [category, setCategory] = useState(String(status?.category ?? 1));

  const save = useMutation({
    mutationFn: () => {
      const body = { name, category: Number(category) as 0 | 1 | 2 };
      return unwrap<unknown>(
        status
          ? apiClient.PUT('/api/v1/status/{id}', { params: { path: { id: status.id! } }, body })
          : apiClient.POST('/api/v1/status', { body }),
      );
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['statuses'] });
      await queryClient.invalidateQueries({ queryKey: ['admin', 'statuses'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t(status ? 'workflows.editStatus' : 'workflows.addStatus')}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t('common.save')} confirmDisabled={!name.trim() || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />
      }
    >
      <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <SelectField
        label={t('workflows.group')}
        value={category}
        onChange={(event) => setCategory(event.target.value)}
        options={GROUPS.map((group) => ({ value: String(group.value), label: t(group.label) }))}
      />
      {save.isError && <Typography role="alert" color="error" variant="body2">{toMessage(save.error)}</Typography>}
    </Dialog>
  );
}

export function StatusesPage() {
  const { t, i18n } = useTranslation();
  const [editing, setEditing] = useState<StatusRow | 'new' | null>(null);

  const statuses = useQuery({
    queryKey: ['admin', 'statuses'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/status/admin-list')),
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('workflows.statuses')} hint={t('workflows.statusesHint')}>
        <Button onClick={() => setEditing('new')}>{t('workflows.addStatus')}</Button>
      </PageHeader>
      {statuses.isPending && <LoadingState />}
      {statuses.isError && <ErrorState onRetry={() => void statuses.refetch()} />}
      {statuses.data && (
        <Table<StatusRow>
          label={t('workflows.statuses')}
          rows={statuses.data}
          rowKey={(status) => status.id!}
          columns={[
            { key: 'name', header: t('account.name'), render: (status) => status.name },
            { key: 'group', header: t('workflows.group'), render: (status) => t(GROUPS[status.category ?? 0]!.label) },
            { key: 'workflows', header: t('workflows.title'), render: (status) => status.workflowCount },
            { key: 'issues', header: t('spaces.issues'), render: (status) => status.issueCount },
            {
              key: 'state',
              header: t('admin.status'),
              render: (status) => <Badge>{t((status.workflowCount ?? 0) > 0 ? 'workflows.inUse' : 'workflows.notUsed')}</Badge>,
            },
            { key: 'updated', header: t('issues.updatedAt'), render: (status) => formatDate(status.updatedAt, i18n.language) },
            {
              key: 'edit',
              header: '',
              render: (status) => (
                <Button kind="link" aria-label={`${t('common.edit')} ${status.name}`} onClick={() => setEditing(status)}>{t('common.edit')}</Button>
              ),
            },
          ]}
        />
      )}
      {editing && <StatusDialog status={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function CreateWorkflowDialog({ workflows, projectId, onClose }: { workflows: WorkflowRow[]; projectId?: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const templates = workflows.filter((workflow) => workflow.version);
  const [name, setName] = useState('');
  const [copyFromId, setCopyFromId] = useState(templates[0]?.id ?? '');

  const create = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/workflow', { body: { name, copyFromId, projectId } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'workflows'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('workflows.create')}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t('workflows.create')} confirmDisabled={!name.trim() || !copyFromId || create.isPending} onCancel={onClose} onConfirm={() => create.mutate()} />
      }
    >
      <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <SelectField
        label={t('workflows.template')}
        value={copyFromId}
        onChange={(event) => setCopyFromId(event.target.value)}
        options={templates.map((workflow) => ({ value: workflow.id!, label: workflow.name! }))}
      />
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}

/** Asks where issues on dropped statuses should go. */
export function StatusMappingFields({
  orphaned,
  targets,
  statuses,
  mappings,
  onChange,
}: {
  orphaned: string[];
  targets: string[];
  statuses: Status[];
  mappings: Record<string, string>;
  onChange: (mappings: Record<string, string>) => void;
}) {
  const { t } = useTranslation();
  const nameOf = (id: string) => statuses.find((status) => status.id === id)?.name ?? id;

  return (
    <div className="flex flex-col gap-2">
      <Typography variant="body2" role="alert">{t('workflows.mappingHint')}</Typography>
      {orphaned.map((id) => (
        <SelectField
          key={id}
          label={nameOf(id)}
          value={mappings[id] ?? ''}
          onChange={(event) => onChange({ ...mappings, [id]: event.target.value })}
          options={targets.map((target) => ({ value: target, label: nameOf(target) }))}
        />
      ))}
    </div>
  );
}

function EditWorkflowDialog({ workflowId, projectId, onClose }: { workflowId: string; projectId?: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const statuses = useStatuses(projectId);
  const [text, setText] = useState<string | null>(null);
  const [parseError, setParseError] = useState(false);
  const [orphaned, setOrphaned] = useState<string[]>([]);
  const [mappings, setMappings] = useState<Record<string, string>>({});

  const detail = useQuery({
    queryKey: ['admin', 'workflows', workflowId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow/{id}', { params: { path: { id: workflowId } } })),
  });

  const current = text ?? JSON.stringify(detail.data?.draft ?? detail.data?.published ?? {}, null, 2);

  const parse = () => {
    try {
      const definition = JSON.parse(current) as Definition;
      setParseError(false);
      return definition;
    } catch {
      setParseError(true);
      return null;
    }
  };

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin', 'workflows'] });

  const publish = useMutation({
    mutationFn: async (definition: Definition) => {
      await unwrap(apiClient.POST('/api/v1/workflow/{id}/save-draft', { params: { path: { id: workflowId } }, body: definition }));
      return unwrap(
        apiClient.POST('/api/v1/workflow/{id}/publish', {
          params: { path: { id: workflowId } },
          body: { statusMappings: mappings },
        }),
      );
    },
    onSuccess: async (result) => {
      await refresh();
      toast('success', t(result.migrationQueued ? 'workflows.publishedQueued' : 'workflows.published'));
      onClose();
    },
    onError: async (failure) => {
      await refresh();
      if (failure instanceof ApiError && failure.code?.endsWith('StatusMappingRequired')) {
        setOrphaned((failure.data.statuses ?? '').split(',').filter(Boolean));
      } else {
        toast('error', toMessage(failure));
      }
    },
  });

  const saveDraft = useMutation({
    mutationFn: (definition: Definition) =>
      unwrap(apiClient.POST('/api/v1/workflow/{id}/save-draft', { params: { path: { id: workflowId } }, body: definition })),
    onSuccess: async () => {
      await refresh();
      toast('success', t('workflows.draftSaved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const run = (action: (definition: Definition) => void) => {
    const definition = parse();
    if (definition) {
      action(definition);
    }
  };

  const steps = (() => {
    try {
      return (JSON.parse(current) as Definition).statusIds ?? [];
    } catch {
      return [];
    }
  })();

  return (
    <Dialog
      open
      width={640}
      title={detail.data?.name ?? t('workflows.title')}
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          <Button kind="secondary" disabled={saveDraft.isPending} onClick={() => run((definition) => saveDraft.mutate(definition))}>{t('workflows.saveDraft')}</Button>
          <Button disabled={publish.isPending} onClick={() => run((definition) => publish.mutate(definition))}>{t('workflows.publish')}</Button>
        </div>
      }
    >
      <Typography variant="body2" color="textSecondary">{t('workflows.jsonHint')}</Typography>
      <textarea
        aria-label={t('workflows.definition')}
        rows={16}
        spellCheck={false}
        value={current}
        onChange={(event) => setText(event.target.value)}
        style={{
          width: '100%',
          padding: 8,
          fontFamily: font.mono,
          fontSize: font.size.xs,
          lineHeight: font.lineHeight,
          border: `1px solid ${sem('border-strong')}`,
          borderRadius: radius.control,
        }}
      />
      {parseError && <Typography role="alert" color="error" variant="body2">{t('workflows.invalidJson')}</Typography>}
      {orphaned.length > 0 && (
        <StatusMappingFields orphaned={orphaned} targets={steps} statuses={statuses.data ?? []} mappings={mappings} onChange={setMappings} />
      )}
    </Dialog>
  );
}

/** Administration passes no space. A space lists global workflows too, which it can use but not edit. */
export function WorkflowsPage({ projectId }: { projectId?: string }) {
  const { t, i18n } = useTranslation();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);

  const workflows = useQuery({
    queryKey: ['admin', 'workflows', 'list', projectId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow', { params: { query: { projectId } } })),
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t(projectId ? 'workflows.library' : 'workflows.title')} hint={t(projectId ? 'workflows.libraryHint' : 'workflows.hint')}>
        {!projectId && <Button kind="secondary" component={RouterLink} to="/admin/statuses">{t('workflows.statuses')}</Button>}
        <Button onClick={() => setCreating(true)}>{t('workflows.create')}</Button>
      </PageHeader>
      {workflows.isPending && <LoadingState />}
      {workflows.isError && <ErrorState onRetry={() => void workflows.refetch()} />}
      {workflows.data?.length === 0 && <EmptyState title={t('workflows.empty')} />}
      {(workflows.data?.length ?? 0) > 0 && (
        <Table<WorkflowRow>
          label={t('workflows.title')}
          rows={workflows.data!}
          rowKey={(workflow) => workflow.id!}
          columns={[
            { key: 'name', header: t('account.name'), render: (workflow) => workflow.name },
            { key: 'scope', header: t('workflows.scope'), render: (workflow) => <ScopeBadge projectId={workflow.projectId} /> },
            { key: 'statuses', header: t('workflows.statuses'), render: (workflow) => workflow.statusCount },
            { key: 'spaces', header: t('spaces.title'), render: (workflow) => workflow.spaceCount },
            { key: 'version', header: t('workflows.version'), render: (workflow) => (workflow.version ? `v${workflow.version}` : '') },
            {
              key: 'state',
              header: t('admin.status'),
              render: (workflow) =>
                workflow.hasDraft ? (
                  <Badge tone="warning">{t('workflows.draftChanges')}</Badge>
                ) : (
                  <Badge tone="success">{t('workflows.publishedState')}</Badge>
                ),
            },
            { key: 'updated', header: t('issues.updatedAt'), render: (workflow) => formatDate(workflow.updatedAt, i18n.language) },
            {
              key: 'edit',
              header: '',
              render: (workflow) => (!projectId || workflow.projectId) && (
                <Link component="button" variant="body2" aria-label={`${t('common.edit')} ${workflow.name}`} onClick={() => setEditing(workflow.id!)}>
                  {t('common.edit')}
                </Link>
              ),
            },
          ]}
        />
      )}
      {creating && <CreateWorkflowDialog workflows={workflows.data ?? []} projectId={projectId} onClose={() => setCreating(false)} />}
      {editing && <EditWorkflowDialog workflowId={editing} projectId={projectId} onClose={() => setEditing(null)} />}
    </div>
  );
}
