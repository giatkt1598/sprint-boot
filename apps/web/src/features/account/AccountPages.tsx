import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, Navigate, useSearchParams } from 'react-router';
import Link from '@mui/material/Link';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { SpaceLogo } from '../../components/SpaceLogo';
import { GitHubMark, GoogleMark } from '../../components/BrandSignInButton';
import { DialogConfirm } from '../../components/Dialog';
import { Button } from '../../components/Button';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { formatDate } from '../../lib/format';
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

const SPACE_ROLES: Record<string, string> = {
  ProjectAdmin: 'spaces.roleProjectAdmin',
  Member: 'spaces.roleMember',
  Viewer: 'spaces.roleViewer',
};

type LinkedLogin = components['schemas']['LinkedLoginDto'];

/** Sign-in providers this account can use, linked or not; linking leaves for the IdP and comes back here. */
function LinkedAccounts() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();
  const [unlinking, setUnlinking] = useState<LinkedLogin | null>(null);

  const logins = useQuery({
    queryKey: ['auth', 'me', 'logins'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/auth/logins')),
  });

  // The IdP round trip reports back through the address; say it once, then tidy the address.
  const linked = params.get('linked');
  const linkError = params.get('linkError');
  useEffect(() => {
    if (!linked && !linkError) {
      return;
    }
    if (linked) {
      toast('success', t('account.linkedNow'));
    } else {
      const key = `errors.${linkError}`;
      toast('error', t(key, { defaultValue: t('errors.generic') }));
    }
    setParams((current) => {
      current.delete('linked');
      current.delete('linkError');
      return current;
    }, { replace: true });
  }, [linked, linkError, setParams, t, toast]);

  const link = useMutation({
    mutationFn: (provider: string) =>
      unwrap(apiClient.POST('/api/v1/auth/logins/{provider}/link', { params: { path: { provider } } })),
    onSuccess: (start) => window.location.assign(start.url ?? '/account/profile'),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const unlink = useMutation({
    mutationFn: (provider: string) => unwrap(apiClient.DELETE('/api/v1/auth/logins/{provider}', { params: { path: { provider } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['auth', 'me', 'logins'] });
      toast('success', t('account.unlinked'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setUnlinking(null),
  });

  return (
    <section aria-labelledby="linked-accounts" className="flex flex-col gap-3">
      <div>
        <Typography id="linked-accounts" variant="h2">{t('account.linkedAccounts')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('account.linkedHint')}</Typography>
      </div>
      {logins.isPending && <LoadingState />}
      {logins.isError && <ErrorState onRetry={() => void logins.refetch()} />}
      {logins.data?.length === 0 && <EmptyState title={t('account.noProviders')} />}
      {(logins.data?.length ?? 0) > 0 && (
        <ul className="flex max-w-md flex-col gap-2">
          {logins.data!.map((login) => (
            <li key={login.provider} className="flex min-h-10 items-center gap-3">
              <span className="flex w-5 shrink-0 justify-center">
                {login.type === 1 ? <GoogleMark /> : login.type === 2 ? <GitHubMark /> : null}
              </span>
              <Typography variant="body1" className="flex-1">{login.displayName}</Typography>
              {login.isLinked ? <Badge tone="success">{t('account.linked')}</Badge> : <Badge>{t('account.notLinked')}</Badge>}
              {login.canLink && (
                <Button kind="secondary" disabled={link.isPending} onClick={() => link.mutate(login.provider ?? '')}>
                  {t('account.link')}
                </Button>
              )}
              {login.isLinked && (
                <Button
                  kind="ghost"
                  disabled={!login.canUnlink || unlink.isPending}
                  title={login.canUnlink ? undefined : t('account.lastMethod')}
                  aria-label={`${t('account.unlink')} ${login.displayName}`}
                  onClick={() => setUnlinking(login)}
                >
                  {t('account.unlink')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <DialogConfirm
        open={unlinking !== null}
        title={t('account.unlinkTitle', { provider: unlinking?.displayName })}
        description={t('account.unlinkBody', { provider: unlinking?.displayName })}
        confirmLabel={t('account.unlink')}
        onCancel={() => setUnlinking(null)}
        onConfirm={() => unlinking && unlink.mutate(unlinking.provider ?? '')}
      />
    </section>
  );
}

function MySpaces() {
  const { t, i18n } = useTranslation();
  const spaces = useQuery({
    queryKey: ['auth', 'me', 'spaces'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/project/my-spaces')),
  });

  return (
    <section aria-labelledby="my-spaces" className="flex flex-col gap-3">
      <Typography id="my-spaces" variant="h2">{t('account.mySpaces')}</Typography>
      {spaces.isPending && <LoadingState />}
      {spaces.isError && <ErrorState onRetry={() => void spaces.refetch()} />}
      {spaces.data?.length === 0 && <EmptyState title={t('admin.noSpaces')} />}
      {(spaces.data?.length ?? 0) > 0 && (
        <Table
          label={t('account.mySpaces')}
          rows={spaces.data!}
          rowKey={(space) => space.projectId!}
          columns={[
            {
              key: 'name',
              header: t('admin.spaceColumn'),
              render: (space) => (
                <span className="flex items-center gap-2">
                  <SpaceLogo spaceKey={space.key} logoUrl={space.logoUrl} />
                  <Link component={RouterLink} to={`/spaces/${space.key}`} variant="body2">{space.name}</Link>
                </span>
              ),
            },
            { key: 'key', header: t('spaces.key'), render: (space) => space.key },
            { key: 'role', header: t('spaces.role'), render: (space) => t(SPACE_ROLES[space.role ?? ''] ?? 'spaces.roleMember') },
            { key: 'added', header: t('spaces.joined'), render: (space) => formatDate(space.addedAt, i18n.language) },
          ]}
        />
      )}
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
    <div className="flex flex-col gap-8">
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
    <LinkedAccounts />
    <MySpaces />
    </div>
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
