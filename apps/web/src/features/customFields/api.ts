import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import type { BadgeTone } from '../../components/Badge';

export type CustomField = components['schemas']['CustomFieldDto'];
export type CustomFieldOption = components['schemas']['CustomFieldOptionDto'];
export type SpaceCustomField = components['schemas']['SpaceCustomFieldDto'];
export type TicketCustomField = components['schemas']['TicketCustomFieldDto'];

/** Mirrors CustomFieldType of the API. */
export const FieldType = {
  text: 0,
  paragraph: 1,
  number: 2,
  date: 3,
  select: 4,
  multiSelect: 5,
  user: 6,
  checkbox: 7,
  url: 8,
  tags: 9,
} as const;

export const FIELD_TYPES = Object.values(FieldType);

export const OPTION_COLORS = ['gray', 'blue', 'green', 'amber', 'red', 'violet'] as const;

/** Option colours are names; the Badge tones turn them into the theme's colours. */
export const optionTone = (color: string | null | undefined): BadgeTone =>
  ({ gray: 'neutral', blue: 'info', green: 'success', amber: 'warning', red: 'danger', violet: 'epic' } as Record<string, BadgeTone>)[color ?? ''] ?? 'neutral';

export const hasOptions = (type: number | undefined) => type === FieldType.select || type === FieldType.multiSelect;

/** The ticket API's key of a field: "cf." and its key. */
export const fieldKey = (field: { key?: string | null }) => `cf.${field.key}`;

export const customFieldKeys = {
  all: ['custom-fields'] as const,
  list: (projectId: string | undefined) => ['custom-fields', 'list', projectId ?? 'workspace'] as const,
  space: (projectId: string | undefined) => ['custom-fields', 'space', projectId] as const,
  layout: (projectId: string | undefined) => ['custom-fields', 'layout', projectId] as const,
};

/** The built-in fields of a ticket's Details sidebar and the locale keys of their names, in their default order. */
export const BUILT_IN_FIELDS: Record<string, string> = {
  status: 'tickets.status',
  assignee: 'tickets.assignee',
  reporter: 'tickets.reporter',
  priority: 'tickets.priority',
  sprint: 'agile.sprint',
  epic: 'tickets.epic',
  labels: 'tickets.labels',
  storyPoints: 'tickets.points',
  startDate: 'tickets.startDate',
  dueDate: 'tickets.dueDate',
};

/** The status moves the ticket along its workflow, so it always shows. */
export const ALWAYS_SHOWN = ['status'];

/** The order of a space's Details sidebar, built-in and custom fields together, and the built-in fields it hides. */
export const useFieldLayout = (projectId: string | undefined) =>
  useQuery({
    queryKey: customFieldKeys.layout(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/custom-field/layout/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

/** The words a tags field already holds in a space, offered while typing. */
export const useFieldTags = (fieldId: string | undefined, projectId: string | undefined, enabled = true) =>
  useQuery({
    queryKey: ['custom-fields', 'tags', fieldId, projectId],
    enabled: enabled && Boolean(fieldId && projectId),
    staleTime: 30_000,
    queryFn: () => unwrap(apiClient.GET('/api/v1/custom-field/{id}/tags/{projectId}', { params: { path: { id: fieldId!, projectId: projectId! } } })),
  });

/** Workspace fields, or a space's own ones. */
export const useCustomFields = (projectId?: string) =>
  useQuery({
    queryKey: customFieldKeys.list(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/custom-field', { params: { query: { projectId } } })),
  });

/** Every field a space can use, on or off, in its order. */
export const useSpaceFields = (projectId: string | undefined) =>
  useQuery({
    queryKey: customFieldKeys.space(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/custom-field/space-fields/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

/** After a change to fields, the lists and every ticket that shows them read again. */
export function useRefreshFields() {
  const queryClient = useQueryClient();
  return async () => {
    await queryClient.invalidateQueries({ queryKey: customFieldKeys.all });
    await queryClient.invalidateQueries({ queryKey: ['tickets'] });
  };
}
