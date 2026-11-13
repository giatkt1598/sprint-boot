import IconButton from '@mui/material/IconButton';
import MuiLink from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useOutletContext } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { ChevronDownIcon, ChevronRightIcon, DocumentIcon } from '../../components/icons';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import type { Space } from '../spaces/api';
import {
  docsKeys,
  MANAGE_DOCS,
  useDocmostSpaces,
  useDocsPages,
  useProjectDocs,
  type DocsPageNode,
} from './api';

function DocsNotice({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return (
    <section className="flex min-h-[40vh] flex-col items-center justify-center gap-2 px-4 text-center">
      <span aria-hidden className="mb-2 flex size-12 items-center justify-center" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-secondary') }}>
        <DocumentIcon size={24} strokeWidth={1.5} />
      </span>
      <Typography variant="h2">{title}</Typography>
      <Typography color="textSecondary" className="max-w-sm">{description}</Typography>
      {action}
    </section>
  );
}

/** Docmost pages open in Docmost, in a new tab, so the work in Sprint Boot stays where it was. */
function PageLink({ url, title }: { url: string; title: string }) {
  return (
    <MuiLink href={url} target="_blank" rel="noopener noreferrer" color="inherit" className="min-w-0 truncate">
      {title}
    </MuiLink>
  );
}

function PageNode({ projectId, page, depth }: { projectId: string; page: DocsPageNode; depth: number }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const children = useDocsPages(projectId, page.id ?? undefined, open);
  const Chevron = open ? ChevronDownIcon : ChevronRightIcon;

  return (
    <li>
      <div className="flex min-h-8 items-center gap-1" style={{ paddingLeft: depth * 20 }}>
        {page.hasChildren ? (
          <IconButton
            size="small"
            aria-expanded={open}
            aria-label={t(open ? 'docs.collapse' : 'docs.expand', { title: page.title })}
            onClick={() => setOpen(!open)}
          >
            <Chevron size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        ) : (
          <span aria-hidden className="inline-block w-[34px]" />
        )}
        <span aria-hidden className="shrink-0">{page.icon ?? <DocumentIcon size={16} strokeWidth={1.5} />}</span>
        <PageLink url={page.url ?? ''} title={page.title ?? ''} />
      </div>
      {open && children.isPending && <div style={{ paddingLeft: (depth + 1) * 20 + 34 }}><LoadingState /></div>}
      {open && children.data && (
        <ul className="m-0 list-none p-0">
          {children.data.map((child) => <PageNode key={child.id} projectId={projectId} page={child} depth={depth + 1} />)}
        </ul>
      )}
    </li>
  );
}

/** Overview › Docs: the page tree of the Docmost space this space keeps its documents in. */
export function DocsTab({ space }: { space: Space }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const canManageDocs = useCan(MANAGE_DOCS);
  const docs = useProjectDocs(space.id!);
  const linked = Boolean(docs.data?.space);
  const pages = useDocsPages(space.id!, undefined, linked);

  if (docs.isPending) {
    return <LoadingState />;
  }
  if (docs.isError) {
    return <ErrorState onRetry={() => void docs.refetch()} />;
  }
  if (!docs.data.configured) {
    return (
      <DocsNotice
        title={t('docs.notConfiguredTitle')}
        description={t(canManageDocs ? 'docs.notConfiguredAdmin' : 'docs.notConfigured')}
        action={canManageDocs && <Button component={RouterLink} to="/admin/docmost">{t('docs.adminTitle')}</Button>}
      />
    );
  }
  if (!linked) {
    return (
      <DocsNotice
        title={t('docs.noSpaceTitle')}
        description={t(docs.data.canManage ? 'docs.noSpaceManager' : 'docs.noSpace')}
        action={docs.data.canManage && <Button component={RouterLink} to={`/spaces/${space.key}/settings/docs`}>{t('docs.pickSpace')}</Button>}
      />
    );
  }

  return (
    <section aria-labelledby="docs-tree" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <Typography id="docs-tree" variant="h2" className="flex-1">{docs.data.space!.name}</Typography>
        {docs.data.spaceUrl && (
          <MuiLink href={docs.data.spaceUrl} target="_blank" rel="noopener noreferrer" variant="body2">{t('docs.openInDocmost')}</MuiLink>
        )}
      </div>
      {pages.isPending && <LoadingState />}
      {pages.isError && (
        <div role="alert" className="flex flex-wrap items-center gap-2 p-3" style={{ backgroundColor: sem('feedback-danger-bg'), color: sem('feedback-danger-text'), borderRadius: radius.card }}>
          <Typography variant="body2" className="flex-1" sx={{ color: 'inherit' }}>{toMessage(pages.error)}</Typography>
          <Button kind="secondary" onClick={() => void pages.refetch()}>{t('common.retry')}</Button>
        </div>
      )}
      {pages.data && pages.data.length === 0 && <EmptyState title={t('docs.noPages')} />}
      {pages.data && pages.data.length > 0 && (
        <ul className="m-0 list-none p-0">
          {pages.data.map((page) => <PageNode key={page.id} projectId={space.id!} page={page} depth={0} />)}
        </ul>
      )}
    </section>
  );
}

/** Space settings › Docs: which Docmost space holds this space's documents. */
export function SpaceDocsPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const docs = useProjectDocs(space.id);
  const canManage = Boolean(docs.data?.canManage);
  const choices = useDocmostSpaces(space.id!, Boolean(docs.data?.configured) && canManage);
  const [picked, setPicked] = useState<string | null>(null);
  const value = picked ?? docs.data?.space?.id ?? '';
  const refresh = () => queryClient.invalidateQueries({ queryKey: docsKeys.project(space.id!) });

  const link = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/docs-project/link/{projectId}', { params: { path: { projectId: space.id! } }, body: { docmostSpaceId: value } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['docs', 'project', space.id] });
      setPicked(null);
      toast('success', t('docs.spaceLinked'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const unlink = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-project/unlink/{projectId}', { params: { path: { projectId: space.id! } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['docs', 'project', space.id] });
      setPicked(null);
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Typography variant="h1">{t('docs.spaceSettingsTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('docs.spaceSettingsHint')}</Typography>
      </div>
      {docs.isPending && <LoadingState />}
      {docs.isError && <ErrorState onRetry={() => void refresh()} />}
      {docs.data && !docs.data.configured && <DocsNotice title={t('docs.notConfiguredTitle')} description={t('docs.notConfigured')} />}
      {docs.data?.configured && (
        <>
          {choices.isError && <Typography role="alert" variant="body2" color="error">{toMessage(choices.error)}</Typography>}
          <SelectField
            label={t('docs.docmostSpace')}
            value={value}
            disabled={!canManage || choices.isPending}
            onChange={(event) => setPicked(event.target.value)}
            options={[
              ...(choices.data ?? (docs.data.space ? [docs.data.space] : [])).map((item) => ({ value: item.id!, label: item.name! })),
            ]}
          />
          {canManage && (
            <div className="flex gap-2">
              <Button disabled={!value || value === docs.data.space?.id || link.isPending} onClick={() => link.mutate()}>{t('common.save')}</Button>
              {docs.data.space && (
                <Button kind="ghost" disabled={unlink.isPending} onClick={() => unlink.mutate()}>{t('docs.unlinkSpace')}</Button>
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
