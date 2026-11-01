import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

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

export const useStatuses = () =>
  useQuery({
    queryKey: ['statuses'],
    staleTime: Infinity,
    queryFn: () => unwrap(apiClient.GET('/api/v1/status')),
  });

export const useLabels = (projectId: string | undefined) =>
  useQuery({
    queryKey: ['labels', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/labels/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

/** Backend status categories mapped to badge categories. */
export const statusCategory = (status: Status | undefined) =>
  (['todo', 'inProgress', 'done'] as const)[status?.category ?? 0];

export const priorityLevel = (priority: Priority | undefined) => {
  const order = priority?.order ?? 2;
  return order <= 1 ? 'high' : order === 2 ? 'medium' : 'low';
};
