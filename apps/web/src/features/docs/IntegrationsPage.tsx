import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { sem } from '../../theme/tokens';
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

/** Admin › Integrations: what Sprint Boot connects to, one row each, laid out as Docmost lists its own. */
export function IntegrationsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const connection = useDocsConnection();
  const [configuring, setConfiguring] = useState(false);
  const [uninstalling, setUninstalling] = useState(false);
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const uninstall = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-admin/uninstall')),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['docs'] });
      toast('success', t('integrations.uninstalled', { name: 'Docmost' }));
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setUninstalling(false),
  });

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
    <div className="flex flex-col gap-4">
      <Typography variant="h1" className="pb-3" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>{t('integrations.title')}</Typography>
      <div className="flex flex-col gap-2">
        <Typography variant="body2">{t('integrations.hint')}</Typography>
        <ol className="m-0 flex flex-col gap-1 pl-6">
          {(['step1', 'step2', 'step3'] as const).map((step) => (
            <Typography key={step} component="li" variant="body2" color="textSecondary">{t(`integrations.${step}`)}</Typography>
          ))}
        </ol>
      </div>
      {connection.isPending && <LoadingState />}
      {connection.isError && <ErrorState onRetry={() => void connection.refetch()} />}
      {data && (
        <ul className="m-0 list-none p-0">
          <li
            aria-labelledby="integration-docmost"
            className="flex items-center gap-4 py-3"
            style={{ borderBottom: `1px solid ${sem('border-default')}` }}
          >
            <img src="/integrations/docmost.png" alt="" aria-hidden className="size-7 shrink-0" />
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <Typography id="integration-docmost" variant="h3">Docmost</Typography>
                {!connected && <Badge tone="subtle">{t('integrations.notConfigured')}</Badge>}
                {connected && problem !== DocsProblem.none && (
                  <Badge tone="danger">{t(problem === DocsProblem.unauthorized ? 'docs.problemUnauthorized' : 'docs.problemUnavailable')}</Badge>
                )}
              </div>
              <Typography variant="body2" color="textSecondary">{t('integrations.docmostDescription')}</Typography>
            </div>
            <div className="flex shrink-0 gap-2">
              {connected ? (
                <>
                  <Button kind="secondary" component={RouterLink} to="docmost">{t('integrations.configure')}</Button>
                  <Button kind="secondary" disabled={uninstall.isPending} onClick={() => setUninstalling(true)}>{t('integrations.uninstall')}</Button>
                </>
              ) : (
                <>
                  <Button kind="secondary" onClick={() => setConfiguring(true)}>{t('integrations.configure')}</Button>
                  <Button kind="secondary" onClick={() => setConfiguring(true)}>{t('integrations.install')}</Button>
                </>
              )}
            </div>
          </li>
        </ul>
      )}
      {configuring && <ConnectDocmostDialog initialUrl={data?.baseUrl ?? ''} onClose={() => setConfiguring(false)} />}
      <DialogConfirm
        open={uninstalling}
        title={t('integrations.uninstallTitle', { name: 'Docmost' })}
        description={t('integrations.uninstallBody')}
        confirmLabel={t('integrations.uninstall')}
        onCancel={() => setUninstalling(false)}
        onConfirm={() => uninstall.mutate()}
      />
    </div>
  );
}
