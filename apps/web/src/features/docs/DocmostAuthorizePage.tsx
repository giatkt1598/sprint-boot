import Typography from '@mui/material/Typography';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { DocumentIcon } from '../../components/icons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { MANAGE_DOCS } from './api';
import { usePageTitle } from '../../lib/pageTitle';

const NONE = '';

/**
 * Where Docmost sends its admin to link Sprint Boot: pick a Sprint Boot space and the Docmost space it keeps its
 * documents in, or neither, then authorize. Sprint Boot and Docmost exchange what they need between their servers.
 */
export function DocmostAuthorizePage() {
  const { t } = useTranslation();
  usePageTitle(t('integrations.authorizeTitle'));
  const toMessage = useErrorMessage();
  const [params] = useSearchParams();
  const docmostUrl = (params.get('docmostUrl') ?? '').replace(/\/+$/, '');
  const state = params.get('state') ?? '';
  const allowed = useCan(MANAGE_DOCS);
  const [projectId, setProjectId] = useState(NONE);
  const [docmostSpaceId, setDocmostSpaceId] = useState(NONE);

  const request = useQuery({
    queryKey: ['docs', 'link-request', docmostUrl, state],
    enabled: allowed && Boolean(docmostUrl && state),
    retry: false,
    queryFn: () => unwrap(apiClient.GET('/api/v1/docs-link/request', { params: { query: { docmostUrl, state } } })),
  });
  const authorize = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/docs-link/authorize', {
        body: { docmostUrl, state, projectId: projectId || null, docmostSpaceId: docmostSpaceId || null },
      })),
    onSuccess: (done) => window.location.assign(done.returnUrl ?? docmostUrl),
  });
  const half = Boolean(projectId) !== Boolean(docmostSpaceId);

  return (
    // Centred on the screen, as a consent step.
    <main className="flex min-h-full flex-1 items-center justify-center p-4 md:p-12" style={{ minHeight: '100%' }}>
      <section
        aria-labelledby="authorize-title"
        className="flex w-full max-w-lg flex-col gap-5 p-6"
        style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card, backgroundColor: sem('bg-surface') }}
      >
        <div className="flex items-center gap-3">
          <span aria-hidden className="flex size-10 shrink-0 items-center justify-center" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-accent') }}>
            <DocumentIcon size={22} strokeWidth={1.5} />
          </span>
          <Typography id="authorize-title" variant="h1">{t('integrations.authorizeTitle')}</Typography>
        </div>
        {!allowed && <Typography role="alert">{t('errors.forbidden')}</Typography>}
        {allowed && !(docmostUrl && state) && <Typography role="alert">{t('errors.DocsLinkExpired')}</Typography>}
        {allowed && docmostUrl && state && (
          <>
            <Typography color="textSecondary">{t('integrations.authorizeHint', { url: docmostUrl })}</Typography>
            {request.isPending && <LoadingState />}
            {request.isError && <ErrorState onRetry={() => void request.refetch()} />}
            {request.isError && <Typography role="alert" variant="body2" color="error">{toMessage(request.error)}</Typography>}
            {request.data && (
              <>
                <SelectField
                  label={t('integrations.sprintBootSpace')}
                  value={projectId}
                  onChange={(event) => setProjectId(event.target.value)}
                  options={[
                    { value: NONE, label: t('integrations.linkLater') },
                    ...(request.data.spaces ?? []).map((space) => ({ value: space.id!, label: `${space.name} (${space.key})` })),
                  ]}
                />
                <SelectField
                  label={t('docs.docmostSpace')}
                  value={docmostSpaceId}
                  onChange={(event) => setDocmostSpaceId(event.target.value)}
                  options={[
                    { value: NONE, label: t('integrations.linkLater') },
                    ...(request.data.docmostSpaces ?? []).map((space) => ({ value: space.id!, label: space.name! })),
                  ]}
                />
                {half && <Typography variant="body2" color="textSecondary">{t('integrations.pickBoth')}</Typography>}
                {authorize.isError && <Typography role="alert" variant="body2" color="error">{toMessage(authorize.error)}</Typography>}
                <div className="flex justify-end gap-2">
                  <Button kind="ghost" onClick={() => window.location.assign(`${docmostUrl}/settings/integrations`)}>{t('common.cancel')}</Button>
                  <Button disabled={half || authorize.isPending} onClick={() => authorize.mutate()}>{t('integrations.authorize')}</Button>
                </div>
              </>
            )}
          </>
        )}
      </section>
    </main>
  );
}
