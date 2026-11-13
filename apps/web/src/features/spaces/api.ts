import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

export type Space = components['schemas']['ProjectDto'];
export type SpaceMember = components['schemas']['ProjectMemberDto'];
export type TrashedSpace = components['schemas']['TrashedProjectDto'];

export const SpacePermission = {
  edit: 'Ticket.Edit',
  manage: 'Project.Manage',
  manageMembers: 'Project.Members.Manage',
} as const;

/** The global permission to delete archived spaces and restore them from the trash. */
export const DELETE_SPACES = 'SprintBoot.Projects.Delete';

/** Archived spaces stay out of switchers, pickers and dashboards; only the admin list asks for them. */
export const useSpaces = ({ includeArchived = false }: { includeArchived?: boolean } = {}) =>
  useQuery({
    queryKey: ['spaces', includeArchived ? 'with-archived' : 'active'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/project', { params: { query: { includeArchived } } })),
  });

export const useSpace = (key: string | undefined) =>
  useQuery({
    queryKey: ['spaces', key],
    enabled: Boolean(key),
    queryFn: () => unwrap(apiClient.GET('/api/v1/project/by-key', { params: { query: { key } } })),
  });

export const useMembers = (projectId: string | undefined) =>
  useQuery({
    queryKey: ['spaces', projectId, 'members'],
    enabled: Boolean(projectId),
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/project/{id}/members', { params: { path: { id: projectId! } } })),
  });

export const useTrashedSpaces = (enabled: boolean) =>
  useQuery({
    queryKey: ['spaces', 'trash'],
    enabled,
    queryFn: () => unwrap(apiClient.GET('/api/v1/project/trash')),
  });

/** Archiving changes the permissions every page of the space reads from its cached copy, so the copies start over. */
export function useSpaceLifecycle() {
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['spaces'] });
  const path = (id: string) => ({ params: { path: { id } } });

  return {
    archive: useMutation({
      mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/project/{id}/archive', path(id))),
      onSuccess: refresh,
    }),
    restore: useMutation({
      mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/project/{id}/restore', path(id))),
      onSuccess: refresh,
    }),
    remove: useMutation({
      mutationFn: ({ id, confirmKey }: { id: string; confirmKey: string }) =>
        unwrap(apiClient.DELETE('/api/v1/project/{id}', { params: { path: { id }, query: { confirmKey } } })),
      onSuccess: refresh,
    }),
    restoreDeleted: useMutation({
      mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/project/{id}/restore-deleted', path(id))),
      onSuccess: refresh,
    }),
  };
}
