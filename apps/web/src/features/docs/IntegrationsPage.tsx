import Typography from '@mui/material/Typography';
import { useMutation } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { DocumentIcon } from '../../components/icons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { DocsProblem, useDocsConnection } from './api';

/** Enter Docmost's address and go there to authorize; Docmost hands back what both sides need. */
function ConnectDocmostDialog({ initialUrl, onClose }: { initialUrl: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [url, setUrl] = useState(initialUrl);

  const start = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-admin/start-link', { body: { docmostUrl: url.trim() } })),
    onSuccess: (data) => window.location.assign(data.authorizeUrl ?? ''),
  });

  return (
    <Dialog
      open
      title={t('integrations.connectDocmost')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('integrations.authorize')}
          confirmDisabled={!/^https?:\/\/\S+$/i.test(url.trim()) || start.isPending}
          onCancel={onClose}
          onConfirm={() => start.mutate()}
        />
      }
    >
      <Typography color="textSecondary">{t('integrations.connectDocmostHint')}</Typography>
      <TextField label={t('docs.baseUrl')} placeholder="https://docs.example.com" autoFocus value={url} onChange={(event) => setUrl(event.target.value)} />
      {start.isError && <Typography role="alert" color="error" variant="body2">{toMessage(start.error)}</Typography>}
    </Dialog>
  );
}

/** Admin › Integrations: what Sprint Boot connects to, one card each, as Docmost lists its own. */
export function IntegrationsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const connection = useDocsConnection();
  const [configuring, setConfiguring] = useState(false);

  // Back from Docmost after authorizing there.
  const linked = params.get('linked');
  useEffect(() => {
    if (linked === 'docmost') {
      toast('success', t('integrations.linked', { name: 'Docmost' }));
      setParams({}, { replace: true });
    }
  }, [linked, setParams, t, toast]);

  const data = connection.data;
  const connected = Boolean(data?.configured);
  const problem = data?.problem ?? DocsProblem.none;

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('integrations.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('integrations.hint')}</Typography>
      </div>
      {connection.isPending && <LoadingState />}
      {connection.isError && <ErrorState onRetry={() => void connection.refetch()} />}
      {data && (
        <div className="grid gap-4" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))' }}>
          <section
            aria-labelledby="integration-docmost"
            className="flex flex-col gap-3 p-4"
            style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card, backgroundColor: sem('bg-surface') }}
          >
            <div className="flex items-center gap-3">
              <span aria-hidden className="flex size-10 shrink-0 items-center justify-center" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-accent') }}>
                <DocumentIcon size={22} strokeWidth={1.5} />
              </span>
              <div className="min-w-0 flex-1">
                <Typography id="integration-docmost" variant="h3">Docmost</Typography>
                {connected && (problem === DocsProblem.none
                  ? <Badge tone="success">{t('docs.connected')}</Badge>
                  : <Badge tone="danger">{t(problem === DocsProblem.unauthorized ? 'docs.problemUnauthorized' : 'docs.problemUnavailable')}</Badge>)}
              </div>
            </div>
            <Typography variant="body2" color="textSecondary" className="flex-1">{t('integrations.docmostDescription')}</Typography>
            {connected && data.baseUrl && <Typography variant="caption" color="textSecondary" className="truncate">{data.baseUrl}</Typography>}
            <div className="flex flex-wrap gap-2">
              <Button kind={connected ? 'secondary' : 'primary'} onClick={() => setConfiguring(true)}>{t('integrations.configure')}</Button>
              {connected && <Button kind="ghost" component={RouterLink} to="docmost">{t('integrations.details')}</Button>}
            </div>
          </section>
        </div>
      )}
      {configuring && <ConnectDocmostDialog initialUrl={data?.baseUrl ?? ''} onClose={() => setConfiguring(false)} />}
    </div>
  );
}
