import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { ApiError, apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { StatusMappingFields } from '../admin/WorkflowPages';
import { useIssueTypes, useStatuses, type IssueType } from '../issues/api';
import { SpacePermission, type Space } from './api';

interface PendingChange {
  issueTypeId: string;
  workflowId: string;
  orphaned: string[];
}

export function SpaceWorkflowsPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canManage = space.permissions?.includes(SpacePermission.manage);
  const canPickWorkflows = useCan('SprintBoot.Workflows.Manage');
  const statuses = useStatuses();
  const types = useIssueTypes(space.id);
  const [pending, setPending] = useState<PendingChange | null>(null);
  const [mappings, setMappings] = useState<Record<string, string>>({});

  const assigned = useQuery({
    queryKey: ['spaces', space.id, 'workflows'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/project-workflow', { params: { query: { projectId: space.id } } })),
  });
  const workflows = useQuery({
    queryKey: ['admin', 'workflows'],
    enabled: canPickWorkflows,
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow')),
  });
  const target = useQuery({
    queryKey: ['admin', 'workflows', pending?.workflowId],
    enabled: Boolean(pending),
    queryFn: () => unwrap(apiClient.GET('/api/v1/workflow/{id}', { params: { path: { id: pending!.workflowId } } })),
  });

  const save = useMutation({
    mutationFn: (change: { issueTypeId: string; workflowId: string; statusMappings: Record<string, string> }) =>
      unwrap(apiClient.POST('/api/v1/project-workflow/set', { body: { projectId: space.id!, ...change } })),
    onSuccess: async (result) => {
      setPending(null);
      setMappings({});
      await queryClient.invalidateQueries({ queryKey: ['spaces', space.id, 'workflows'] });
      toast('success', t(result.migrationQueued ? 'workflows.publishedQueued' : 'account.saved'));
    },
    onError: (failure, change) => {
      if (failure instanceof ApiError && failure.code?.endsWith('StatusMappingRequired')) {
        setPending({ ...change, orphaned: (failure.data.statuses ?? '').split(',').filter(Boolean) });
      } else {
        toast('error', toMessage(failure));
      }
    },
  });

  const workflowOf = (type: IssueType) => assigned.data?.find((item) => item.issueTypeId === type.id);
  const defaultName = workflows.data?.find((item) => item.name === 'Default');
  const options = (workflows.data ?? []).filter((item) => item.version).map((item) => ({ value: item.id!, label: item.name! }));

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('workflows.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('workflows.spaceHint')}</Typography>
      </div>
      {(types.isPending || assigned.isPending) && <LoadingState />}
      {assigned.isError && <ErrorState onRetry={() => void assigned.refetch()} />}
      {types.data && assigned.data && (
        <Table<IssueType>
          label={t('workflows.title')}
          rows={types.data}
          rowKey={(type) => type.id!}
          columns={[
            { key: 'type', header: t('issues.issueType'), render: (type) => t(`issue.type.${type.icon}`) },
            {
              key: 'workflow',
              header: t('workflows.title'),
              render: (type) =>
                canManage && canPickWorkflows ? (
                  <SelectField
                    quiet
                    label={`${t('workflows.title')}: ${t(`issue.type.${type.icon}`)}`}
                    value={workflowOf(type)?.workflowId ?? defaultName?.id ?? ''}
                    onChange={(event) => save.mutate({ issueTypeId: type.id!, workflowId: event.target.value, statusMappings: {} })}
                    options={options}
                  />
                ) : (
                  workflowOf(type)?.workflowName ?? t('workflows.defaultName')
                ),
            },
          ]}
        />
      )}
      {pending && (
        <Dialog
          open
          title={t('workflows.changeTitle')}
          onClose={() => setPending(null)}
          footer={
            <DialogFooter
              confirmLabel={t('common.confirm')}
              confirmDisabled={pending.orphaned.some((id) => !mappings[id]) || save.isPending}
              onCancel={() => setPending(null)}
              onConfirm={() => save.mutate({ issueTypeId: pending.issueTypeId, workflowId: pending.workflowId, statusMappings: mappings })}
            />
          }
        >
          <StatusMappingFields
            orphaned={pending.orphaned}
            targets={target.data?.published?.statusIds ?? []}
            statuses={statuses.data ?? []}
            mappings={mappings}
            onChange={setMappings}
          />
        </Dialog>
      )}
    </div>
  );
}
