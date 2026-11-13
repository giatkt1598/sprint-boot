import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

export type DocsConnection = components['schemas']['DocsConnectionDto'];
export type DocsPageNode = components['schemas']['DocsPageNodeDto'];
export type TicketDocPage = components['schemas']['TicketDocPageDto'];
export type DocmostSpace = components['schemas']['DocmostSpaceDto'];

/** As the API numbers DocsProblem and DocLinkSource. */
export const DocsProblem = { none: 0, unauthorized: 1, unavailable: 2 } as const;
export const DocLinkSource = { mention: 0, manual: 1 } as const;

export const MANAGE_DOCS = 'SprintBoot.Docs.Manage';

export const docsKeys = {
  connection: ['docs', 'connection'],
  project: (projectId: string) => ['docs', 'project', projectId],
  docmostSpaces: (projectId: string) => ['docs', 'project', projectId, 'docmost-spaces'],
  pages: (projectId: string, parentPageId?: string) => ['docs', 'project', projectId, 'pages', parentPageId ?? 'root'],
  ticket: (ticketId: string) => ['docs', 'ticket', ticketId],
};

export const useDocsConnection = () =>
  useQuery({ queryKey: docsKeys.connection, queryFn: () => unwrap(apiClient.GET('/api/v1/docs-admin')) });

export const useProjectDocs = (projectId: string | undefined) =>
  useQuery({
    queryKey: docsKeys.project(projectId ?? ''),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/docs-project', { params: { query: { projectId: projectId! } } })),
  });

export const useDocmostSpaces = (projectId: string, enabled: boolean) =>
  useQuery({
    queryKey: docsKeys.docmostSpaces(projectId),
    enabled,
    retry: false,
    queryFn: () => unwrap(apiClient.GET('/api/v1/docs-project/docmost-spaces/{projectId}', { params: { path: { projectId } } })),
  });

/** One level of the linked Docmost space's tree; a level loads when it is opened. */
export const useDocsPages = (projectId: string, parentPageId: string | undefined, enabled = true) =>
  useQuery({
    queryKey: docsKeys.pages(projectId, parentPageId),
    enabled,
    retry: false,
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/docs-project/pages', { params: { query: { projectId, parentPageId } } })),
  });

export const useTicketDocs = (ticketId: string) =>
  useQuery({
    queryKey: docsKeys.ticket(ticketId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/docs-ticket', { params: { query: { ticketId } } })),
  });

export function useTicketDocsActions(ticketId: string) {
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: docsKeys.ticket(ticketId) });
  return {
    link: useMutation({
      mutationFn: (url: string) =>
        unwrap(apiClient.POST('/api/v1/docs-ticket/link/{ticketId}', { params: { path: { ticketId } }, body: { url } })),
      onSuccess: refresh,
    }),
    unlink: useMutation({
      mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/docs-ticket/{id}', { params: { path: { id } } })),
      onSuccess: refresh,
    }),
    createPage: useMutation({
      mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-ticket/page/{ticketId}', { params: { path: { ticketId } } })),
      onSuccess: refresh,
    }),
  };
}
