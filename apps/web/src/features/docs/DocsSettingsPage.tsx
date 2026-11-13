import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { CopyField } from '../../components/CopyField';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime } from '../../lib/format';
import { docsKeys, DocsProblem, useDocsConnection, type DocsConnection } from './api';

/** Admin › Docmost: where Sprint Boot reads Docmost from, and the secret Docmost calls back with. */
export function DocsSettingsPage() {
  const { t } = useTranslation();
  const connection = useDocsConnection();

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('docs.adminTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('docs.adminHint')}</Typography>
      </div>
      {connection.isPending && <LoadingState />}
      {connection.isError && <ErrorState onRetry={() => void connection.refetch()} />}
      {connection.data && <ConnectionForm connection={connection.data} />}
      {connection.data?.configured && <BridgeSection connection={connection.data} />}
    </div>
  );
}

function ConnectionStatus({ connection }: { connection: DocsConnection }) {
  const { t, i18n } = useTranslation();
  if (!connection.configured) {
    return null;
  }
  const problem = connection.problem ?? DocsProblem.none;

  return (
    <div role="status" className="flex flex-wrap items-center gap-2">
      {problem === DocsProblem.none
        ? <Badge tone="success">{t('docs.connected')}</Badge>
        : <Badge tone="danger">{t(problem === DocsProblem.unauthorized ? 'docs.problemUnauthorized' : 'docs.problemUnavailable')}</Badge>}
      {problem === DocsProblem.none && connection.docmostUser && (
        <Typography variant="body2" color="textSecondary">{t('docs.actingAs', { name: connection.docmostUser })}</Typography>
      )}
      {problem !== DocsProblem.none && connection.problemDetail && (
        <Typography variant="body2" color="textSecondary" className="basis-full">{connection.problemDetail}</Typography>
      )}
      {connection.lastCheckedAt && (
        <Typography variant="body2" color="textSecondary">{t('docs.lastChecked', { time: formatDateTime(connection.lastCheckedAt, i18n.language) })}</Typography>
      )}
    </div>
  );
}

function ConnectionForm({ connection }: { connection: DocsConnection }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [baseUrl, setBaseUrl] = useState(connection.baseUrl ?? '');
  const [apiKey, setApiKey] = useState('');
  const refresh = (data: DocsConnection) => queryClient.setQueryData(docsKeys.connection, data);

  const save = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-admin/save', { body: { baseUrl: baseUrl.trim(), apiKey: apiKey.trim() || null } })),
    onSuccess: (data) => {
      refresh(data);
      setApiKey('');
      toast(data.problem === DocsProblem.none ? 'success' : 'error', t(data.problem === DocsProblem.none ? 'docs.saved' : 'docs.savedWithProblem'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const test = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-admin/test')),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      <Typography variant="h2">{t('docs.connection')}</Typography>
      <ConnectionStatus connection={connection} />
      <TextField label={t('docs.baseUrl')} placeholder="https://docs.example.com" value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} />
      <div className="flex flex-col gap-1">
        <TextField
          label={t('docs.apiKey')}
          type="password"
          autoComplete="off"
          placeholder={connection.configured ? t('docs.apiKeyKept') : undefined}
          value={apiKey}
          onChange={(event) => setApiKey(event.target.value)}
        />
        <Typography variant="body2" color="textSecondary">{t('docs.apiKeyHint')}</Typography>
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={!baseUrl.trim() || (!connection.configured && !apiKey.trim()) || save.isPending}>{t('common.save')}</Button>
        {connection.configured && (
          <Button kind="secondary" disabled={test.isPending} onClick={() => test.mutate()}>{t('docs.test')}</Button>
        )}
      </div>
    </form>
  );
}

function BridgeSection({ connection }: { connection: DocsConnection }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [secret, setSecret] = useState<string | null>(null);

  const rotate = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/docs-admin/rotate-secret')),
    onSuccess: async (data) => {
      setSecret(data.secret ?? null);
      await queryClient.invalidateQueries({ queryKey: docsKeys.connection });
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <section aria-labelledby="docs-bridge" className="flex flex-col gap-4">
      <div>
        <Typography id="docs-bridge" variant="h2">{t('docs.bridgeTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('docs.bridgeHint')}</Typography>
      </div>
      <CopyField label={t('docs.bridgeUrl')} value={connection.sprintBootUrl ?? ''} copyLabel={t('docs.copy')} copiedMessage={t('docs.copied')} />
      {secret ? (
        <div className="flex flex-col gap-1">
          <CopyField label={t('docs.bridgeSecret')} value={secret} copyLabel={t('docs.copy')} copiedMessage={t('docs.copied')} />
          <Typography variant="body2" color="textSecondary">{t('docs.secretOnce')}</Typography>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <Button kind={connection.hasBridgeSecret ? 'secondary' : 'primary'} disabled={rotate.isPending} onClick={() => rotate.mutate()}>
            {t(connection.hasBridgeSecret ? 'docs.rotateSecret' : 'docs.createSecret')}
          </Button>
          {connection.hasBridgeSecret && <Typography variant="body2" color="textSecondary">{t('docs.rotateHint')}</Typography>}
        </div>
      )}
    </section>
  );
}
