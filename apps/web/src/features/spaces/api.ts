import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

export type Space = components['schemas']['ProjectDto'];
export type SpaceMember = components['schemas']['ProjectMemberDto'];

export const SpacePermission = {
  edit: 'Issue.Edit',
  manage: 'Project.Manage',
  manageMembers: 'Project.Members.Manage',
} as const;

export const useSpaces = () =>
  useQuery({ queryKey: ['spaces'], queryFn: () => unwrap(apiClient.GET('/api/v1/project')) });

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
