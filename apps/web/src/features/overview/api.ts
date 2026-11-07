import { keepPreviousData, useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

export type OverviewSummary = components['schemas']['OverviewSummaryDto'];
export type ProjectIntroduction = components['schemas']['ProjectIntroductionDto'];

/** Matches the API's OverviewPeriod. */
export const OverviewPeriods = ['currentSprint', 'lastThreeSprints', 'thisMonth', 'thisQuarter', 'thisYear'] as const;
export type OverviewPeriod = (typeof OverviewPeriods)[number];

export const useOverviewSummary = (projectId: string | undefined, period: OverviewPeriod) =>
  useQuery({
    queryKey: ['issues', 'overview', projectId, period],
    enabled: Boolean(projectId),
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/v1/project-overview/summary/{projectId}', {
          params: { path: { projectId: projectId! }, query: { Period: OverviewPeriods.indexOf(period) as 0 | 1 | 2 | 3 | 4 } },
        }),
      ),
  });

export const introductionKey = (projectId: string | undefined) => ['project-introduction', projectId];

export const useIntroduction = (projectId: string | undefined) =>
  useQuery({
    queryKey: introductionKey(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/project-introduction', { params: { query: { projectId: projectId! } } })),
  });
