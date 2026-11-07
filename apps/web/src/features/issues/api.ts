import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { uploadFile, type UploadProgress } from './upload';

export type Issue = components['schemas']['IssueDto'];
export type IssueSummary = components['schemas']['IssueSummaryDto'];
export type IssueType = components['schemas']['IssueTypeDto'];
export type Priority = components['schemas']['PriorityDto'];
export type Status = components['schemas']['StatusDto'];
export type Comment = components['schemas']['CommentDto'];
export type Activity = components['schemas']['IssueActivityDto'];

export const useIssueTypes = (projectId: string | undefined) =>
  useQuery({
    queryKey: ['issue-types', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/types/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

export const usePriorities = () =>
  useQuery({
    queryKey: ['priorities'],
    staleTime: Infinity,
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/priorities')),
  });

/** Global statuses, plus the space's own when one is given. */
export const useStatuses = (projectId?: string) =>
  useQuery({
    queryKey: ['statuses', projectId],
    staleTime: Infinity,
    queryFn: () => unwrap(apiClient.GET('/api/v1/status', { params: { query: { projectId } } })),
  });

export const useLabels = (projectId: string | undefined) =>
  useQuery({
    queryKey: ['labels', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/labels/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

export type IssueTemplate = components['schemas']['IssueTemplateDto'];

export const issueTemplatesKey = (projectId: string | undefined) => ['issue-templates', projectId];

export const useIssueTemplates = (projectId: string | undefined) =>
  useQuery({
    queryKey: issueTemplatesKey(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue-template', { params: { query: { projectId: projectId! } } })),
  });

export type GlobalIssueTemplate = components['schemas']['GlobalIssueTemplateDto'];
export type TemplateModeValue = NonNullable<IssueTemplate['mode']>;

/** How the create form shows a template, as the API numbers it. */
export const TemplateMode = { fill: 0, placeholder: 1 } as const satisfies Record<string, TemplateModeValue>;

/** The issue types workspace templates are for, matched to each space's type by its icon; subtasks take none. */
export const TemplateTypeKeys = ['epic', 'story', 'task', 'bug'] as const;
export type TemplateTypeKey = (typeof TemplateTypeKeys)[number];

export const globalIssueTemplatesKey = ['global-issue-templates'];

export const useGlobalIssueTemplates = (enabled = true) =>
  useQuery({
    queryKey: globalIssueTemplatesKey,
    enabled,
    queryFn: () => unwrap(apiClient.GET('/api/v1/global-issue-template')),
  });

/** Large files go up in parts, past proxies that cap each request; see uploadFile. */
export const uploadRichTextFile = async (projectId: string, file: File, onProgress?: UploadProgress) =>
  (await uploadFile({ purpose: 1, projectId }, file, onProgress)).richTextFile!;

export const uploadAttachment = async (issueId: string, file: File, onProgress?: UploadProgress) =>
  (await uploadFile({ purpose: 0, issueId }, file, onProgress)).attachment!;

/** Epics of a project, for the parent pickers. */
export const useEpics = (projectId: string | undefined) => {
  const types = useIssueTypes(projectId);
  const epicTypeId = types.data?.find((type) => type.hierarchy === 2)?.id;
  return useQuery({
    queryKey: ['issues', 'epics', projectId],
    enabled: Boolean(epicTypeId),
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/v1/issue/search', {
          params: { query: { ProjectId: projectId, TypeIds: [epicTypeId!], MaxResultCount: 100 } },
        }),
      ),
  });
};

/** Backend status categories mapped to badge categories. */
export const statusCategory = (status: Status | undefined) =>
  (['todo', 'inProgress', 'done'] as const)[status?.category ?? 0];

const PRIORITY_LEVELS = ['highest', 'high', 'medium', 'low', 'lowest'] as const;

export const priorityLevel = (priority: Priority | undefined) => PRIORITY_LEVELS[priority?.order ?? 2] ?? 'medium';
