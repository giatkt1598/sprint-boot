import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { uploadFile, type UploadProgress } from './upload';

export type Ticket = components['schemas']['TicketDto'];
export type TicketSummary = components['schemas']['TicketSummaryDto'];
export type TicketType = components['schemas']['TicketTypeDto'];
export type Priority = components['schemas']['PriorityDto'];
export type Status = components['schemas']['StatusDto'];
export type Comment = components['schemas']['CommentDto'];
export type Activity = components['schemas']['TicketActivityDto'];

export const useTicketTypes = (projectId: string | undefined) =>
  useQuery({
    queryKey: ['ticket-types', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/types/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

export const usePriorities = () =>
  useQuery({
    queryKey: ['priorities'],
    staleTime: Infinity,
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/priorities')),
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
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/labels/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

export type TicketTemplate = components['schemas']['TicketTemplateDto'];

export const ticketTemplatesKey = (projectId: string | undefined) => ['ticket-templates', projectId];

export const useTicketTemplates = (projectId: string | undefined) =>
  useQuery({
    queryKey: ticketTemplatesKey(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket-template', { params: { query: { projectId: projectId! } } })),
  });

export type GlobalTicketTemplate = components['schemas']['GlobalTicketTemplateDto'];
export type TemplateModeValue = NonNullable<TicketTemplate['mode']>;

/** How the create form shows a template, as the API numbers it. */
export const TemplateMode = { fill: 0, placeholder: 1 } as const satisfies Record<string, TemplateModeValue>;

/** The ticket types workspace templates are for, matched to each space's type by its icon; subtasks take none. */
export const TemplateTypeKeys = ['epic', 'story', 'task', 'bug'] as const;
export type TemplateTypeKey = (typeof TemplateTypeKeys)[number];

/** As long as the API takes a template name. */
export const TEMPLATE_NAME_MAX = 128;

export const globalTicketTemplatesKey = ['global-ticket-templates'];

export const useGlobalTicketTemplates = (enabled = true) =>
  useQuery({
    queryKey: globalTicketTemplatesKey,
    enabled,
    queryFn: () => unwrap(apiClient.GET('/api/v1/global-ticket-template')),
  });

/** Large files go up in parts, past proxies that cap each request; see uploadFile. */
export const uploadRichTextFile = async (projectId: string, file: File, onProgress?: UploadProgress) =>
  (await uploadFile({ purpose: 1, projectId }, file, onProgress)).richTextFile!;

export const uploadAttachment = async (ticketId: string, file: File, onProgress?: UploadProgress) =>
  (await uploadFile({ purpose: 0, ticketId }, file, onProgress)).attachment!;

/** Epics of a project, for the parent pickers. */
export const useEpics = (projectId: string | undefined) => {
  const types = useTicketTypes(projectId);
  const epicTypeId = types.data?.find((type) => type.hierarchy === 2)?.id;
  return useQuery({
    queryKey: ['tickets', 'epics', projectId],
    enabled: Boolean(epicTypeId),
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/v1/ticket/search', {
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
