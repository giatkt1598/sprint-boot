import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { Toggle } from '../../components/Checkbox';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';

type Settings = components['schemas']['EmailSettingsDto'];

const KEY = ['admin', 'email-settings'];

function EmailSettingsForm({ settings }: { settings: Settings }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [host, setHost] = useState(settings.host ?? '');
  const [port, setPort] = useState(String(settings.port ?? 587));
  const [enableSsl, setEnableSsl] = useState(settings.enableSsl ?? false);
  const [userName, setUserName] = useState(settings.userName ?? '');
  const [password, setPassword] = useState('');
  const [fromAddress, setFromAddress] = useState(settings.fromAddress ?? '');
  const [fromName, setFromName] = useState(settings.fromName ?? '');
  const [testAddress, setTestAddress] = useState('');

  const save = useMutation({
    mutationFn: () =>
      unwrap(apiClient.PUT('/api/v1/email-settings', {
        body: { host, port: Number(port), enableSsl, userName, password: password || null, fromAddress, fromName },
      })),
    onSuccess: async () => {
      setPassword('');
      await queryClient.invalidateQueries({ queryKey: KEY });
      toast('success', t('email.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const sendTest = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/email-settings/send-test', { body: { to: testAddress } })),
    onSuccess: () => toast('success', t('email.testSent', { address: testAddress })),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const portNumber = Number(port);
  const ready = host.trim() && fromAddress.trim() && Number.isInteger(portNumber) && portNumber >= 1 && portNumber <= 65535;

  return (
    <div className="flex max-w-xl flex-col gap-4">
      <div className="grid grid-cols-[1fr_120px] gap-4">
        <TextField label={t('email.host')} value={host} onChange={(event) => setHost(event.target.value)} />
        <TextField label={t('email.port')} type="number" value={port} onChange={(event) => setPort(event.target.value)} />
      </div>
      <div className="flex flex-col gap-1">
        <Toggle label={t('email.enableSsl')} checked={enableSsl} onChange={(event) => setEnableSsl(event.target.checked)} />
        <Typography variant="caption" color="textSecondary">{t('email.sslHint')}</Typography>
      </div>
      <div className="grid grid-cols-2 gap-4">
        <TextField label={t('email.userName')} autoComplete="off" value={userName} onChange={(event) => setUserName(event.target.value)} />
        <TextField
          label={t('email.password')}
          type="password"
          autoComplete="new-password"
          placeholder={settings.hasPassword ? t('email.passwordKept') : ''}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <TextField label={t('email.fromAddress')} type="email" value={fromAddress} onChange={(event) => setFromAddress(event.target.value)} />
        <TextField label={t('email.fromName')} value={fromName} onChange={(event) => setFromName(event.target.value)} />
      </div>
      <Button sx={{ alignSelf: 'flex-start' }} disabled={!ready || save.isPending} onClick={() => save.mutate()}>{t('common.save')}</Button>
      <div className="flex flex-col gap-2">
        <Typography variant="h2">{t('email.testTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('email.testHint')}</Typography>
        <div className="flex items-end gap-2">
          <div className="flex-1">
            <TextField label={t('email.testAddress')} type="email" value={testAddress} onChange={(event) => setTestAddress(event.target.value)} />
          </div>
          <Button kind="secondary" disabled={!testAddress.trim() || sendTest.isPending} onClick={() => sendTest.mutate()}>{t('email.sendTest')}</Button>
        </div>
      </div>
    </div>
  );
}

export function EmailSettingsPage() {
  const { t } = useTranslation();
  const settings = useQuery({ queryKey: KEY, queryFn: () => unwrap(apiClient.GET('/api/v1/email-settings')) });

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('email.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('email.subtitle')}</Typography>
      </div>
      {settings.isPending && <LoadingState />}
      {settings.isError && <ErrorState onRetry={() => void settings.refetch()} />}
      {settings.data && <EmailSettingsForm settings={settings.data} />}
    </div>
  );
}
