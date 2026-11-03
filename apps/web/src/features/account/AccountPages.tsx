import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
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
      backTo="/"
      menu={
        <>
          <SettingsMenuItem to="profile" label={t('account.profile')} />
          <SettingsMenuItem to="security" label={t('account.security')} />
          <SettingsMenuItem to="tokens" label={t('tokens.title')} />
          <SettingsMenuItem to="notifications" label={t('topBar.notifications')} />
        </>
      }
    />
  );
}

export const AccountIndex = () => <Navigate to="profile" replace />;

const AVATAR_SIZE = 256;

/** Center-crops to a square and scales down, so uploads stay small whatever the camera wrote. */
async function squareImage(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  const side = Math.min(bitmap.width, bitmap.height);
  const size = Math.min(AVATAR_SIZE, side);
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  canvas.getContext('2d')!.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, size, size);
  bitmap.close();
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/webp', 0.9));
  if (!blob) {
    throw new Error('Canvas export failed');
  }
  return blob;
}

function AvatarSection() {
  const { t } = useTranslation();
  const user = useCurrentUser();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);

  // Every list that shows people carries their picture, so all of them reload.
  const refresh = () => queryClient.invalidateQueries();

  const upload = useMutation({
    mutationFn: async (file: File) => {
      let image: Blob;
      try {
        image = await squareImage(file);
      } catch {
        throw new Error('unreadable');
      }
      return unwrap(
        apiClient.POST('/api/v1/auth/avatar', {
          body: {} as never,
          bodySerializer: () => {
            const form = new FormData();
            form.append('file', image, image.type === 'image/webp' ? 'avatar.webp' : 'avatar.png');
            return form;
          },
        }),
      );
    },
    onSuccess: async () => {
      await refresh();
      toast('success', t('account.avatarSaved'));
    },
    onError: (failure) => toast('error', failure instanceof Error && failure.message === 'unreadable' ? t('account.avatarUnreadable') : toMessage(failure)),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/auth/avatar')),
    onSuccess: async () => {
      await refresh();
      toast('success', t('account.avatarRemoved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const pick = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      upload.mutate(file);
    }
  };

  const busy = upload.isPending || remove.isPending;
  return (
    <section aria-label={t('account.avatar')} className="flex items-center gap-4">
      <Avatar name={user.name ?? ''} src={user.avatarUrl} size={64} />
      <div className="flex flex-col gap-2">
        <div className="flex gap-2">
          <Button kind="secondary" disabled={busy} onClick={() => input.current?.click()}>
            {t(user.avatarUrl ? 'account.changeAvatar' : 'account.uploadAvatar')}
          </Button>
          {user.avatarUrl && (
            <Button kind="ghost" disabled={busy} onClick={() => remove.mutate()}>{t('account.removeAvatar')}</Button>
          )}
        </div>
        <Typography variant="caption" color="textSecondary">{t('account.avatarHint')}</Typography>
      </div>
      <input ref={input} type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden aria-label={t('account.uploadAvatar')} onChange={pick} />
    </section>
  );
}

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
      <AvatarSection />
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
