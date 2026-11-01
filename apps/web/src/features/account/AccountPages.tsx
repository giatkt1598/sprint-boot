import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { SettingsFrame, SettingsMenuItem } from '../spaces/SpaceSettings';

export function AccountLayout() {
  const { t } = useTranslation();
  return (
    <SettingsFrame
      title={t('account.title')}
      backTo="/spaces"
      menu={
        <>
          <SettingsMenuItem to="profile" label={t('account.profile')} />
          <SettingsMenuItem to="security" label={t('account.security')} />
        </>
      }
    />
  );
}

export const AccountIndex = () => <Navigate to="profile" replace />;

export function ProfilePage() {
  const { t, i18n } = useTranslation();
  const user = useCurrentUser();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [name, setName] = useState(user.name ?? '');
  const [language, setLanguage] = useState(user.language ?? 'en');

  const save = useMutation({
    mutationFn: () => unwrap(apiClient.PUT('/api/v1/auth/profile', { body: { name, language } })),
    onSuccess: async () => {
      await i18n.changeLanguage(language);
      await queryClient.invalidateQueries({ queryKey: ['auth', 'me'] });
      toast('success', t('account.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <form className="flex max-w-md flex-col gap-4" onSubmit={submit}>
      <Typography variant="h1">{t('account.profile')}</Typography>
      <TextField label={t('account.name')} required value={name} onChange={(event) => setName(event.target.value)} />
      <TextField label={t('auth.email')} value={user.email ?? ''} disabled />
      <SelectField
        label={t('account.language')}
        value={language}
        onChange={(event) => setLanguage(event.target.value)}
        options={[
          { value: 'en', label: t('account.english') },
          { value: 'vi', label: t('account.vietnamese') },
        ]}
      />
      <div>
        <Button type="submit" disabled={!name.trim() || save.isPending}>{t('common.save')}</Button>
      </div>
    </form>
  );
}

export function SecurityPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const [currentPassword, setCurrent] = useState('');
  const [newPassword, setNew] = useState('');
  const [confirm, setConfirm] = useState('');
  const mismatch = confirm.length > 0 && newPassword !== confirm;

  const change = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/auth/change-password', { body: { currentPassword, newPassword } })),
    onSuccess: () => {
      setCurrent('');
      setNew('');
      setConfirm('');
      toast('success', t('account.passwordChanged'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    change.mutate();
  };

  return (
    <form className="flex max-w-md flex-col gap-4" onSubmit={submit}>
      <Typography variant="h1">{t('account.security')}</Typography>
      <TextField label={t('account.currentPassword')} type="password" autoComplete="current-password" required value={currentPassword} onChange={(event) => setCurrent(event.target.value)} />
      <TextField label={t('auth.newPassword')} type="password" autoComplete="new-password" required value={newPassword} onChange={(event) => setNew(event.target.value)} />
      <TextField
        label={t('auth.confirmPassword')}
        type="password"
        autoComplete="new-password"
        required
        value={confirm}
        error={mismatch ? t('auth.passwordMismatch') : undefined}
        onChange={(event) => setConfirm(event.target.value)}
      />
      <div>
        <Button type="submit" disabled={!currentPassword || !newPassword || newPassword !== confirm || change.isPending}>
          {t('account.changePassword')}
        </Button>
      </div>
    </form>
  );
}
