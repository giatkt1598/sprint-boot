import Slider from '@mui/material/Slider';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { SelectField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { FONT_SCALES, setAppearance, useAppearance, type Appearance } from '../../theme/appearance';
import { font, radius, sem } from '../../theme/tokens';

type Preferences = components['schemas']['PreferencesDto'];

const COLOR_MODES = ['system', 'light', 'dark'] as const;
const ACCENTS = ['blue', 'green', 'violet'] as const;
const FONTS = ['poppins', 'system'] as const;
const MIN_OPACITY = 10;
const OPACITY_STEP = 5;

/** How the app looks for this person; every change applies at once and is saved with their account. */
export function PreferencesPage() {
  const { t } = useTranslation();
  const user = useCurrentUser();
  const appearance = useAppearance();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['auth', 'me'] });

  const save = useMutation({
    mutationFn: (next: Preferences) => unwrap(apiClient.PUT('/api/v1/auth/preferences', { body: next })),
    onSettled: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const change = (changes: Partial<Preferences>) => {
    const next: Preferences = {
      colorMode: appearance.colorMode,
      accent: appearance.accent,
      fontFamily: appearance.fontFamily,
      fontScale: appearance.fontScale,
      backgroundOpacity: appearance.backgroundOpacity,
      ...changes,
    };
    setAppearance(next as Partial<Appearance>);
    save.mutate(next);
  };

  const upload = useMutation({
    mutationFn: (file: File) =>
      unwrap(
        apiClient.POST('/api/v1/auth/background', {
          body: {} as never,
          bodySerializer: () => {
            const form = new FormData();
            form.append('file', file, file.name);
            return form;
          },
        }),
      ),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/auth/background')),
    onSuccess: refresh,
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
  const hasBackground = Boolean(user.backgroundUrl);

  return (
    <div className="flex max-w-md flex-col gap-4">
      <Typography variant="h1">{t('account.preferences.title')}</Typography>

      <SelectField
        label={t('account.preferences.colorMode')}
        value={appearance.colorMode}
        onChange={(event) => change({ colorMode: event.target.value as Preferences['colorMode'] })}
        options={COLOR_MODES.map((value) => ({ value, label: t(`account.preferences.mode.${value}`) }))}
      />
      <SelectField
        label={t('account.preferences.accent')}
        value={appearance.accent}
        onChange={(event) => change({ accent: event.target.value as Preferences['accent'] })}
        options={ACCENTS.map((value) => ({ value, label: t(`account.preferences.accentName.${value}`) }))}
      />
      <SelectField
        label={t('account.preferences.fontFamily')}
        value={appearance.fontFamily}
        onChange={(event) => change({ fontFamily: event.target.value as Preferences['fontFamily'] })}
        options={FONTS.map((value) => ({ value, label: t(`account.preferences.font.${value}`) }))}
      />
      <SelectField
        label={t('account.preferences.fontScale')}
        value={String(appearance.fontScale)}
        onChange={(event) => change({ fontScale: Number(event.target.value) })}
        options={FONT_SCALES.map((value) => ({ value: String(value), label: `${value}%` }))}
      />

      <section aria-label={t('account.preferences.background')} className="flex flex-col gap-3">
        <Typography variant="h3" component="h2">{t('account.preferences.background')}</Typography>
        <div className="flex items-center gap-4">
          <div
            aria-hidden
            className="size-16 shrink-0"
            style={{
              borderRadius: radius.card,
              border: `1px solid ${sem('border-default')}`,
              backgroundColor: sem('bg-subtle'),
              backgroundImage: user.backgroundUrl ? `url("${user.backgroundUrl}")` : undefined,
              backgroundSize: 'cover',
              backgroundPosition: 'center',
            }}
          />
          <div className="flex flex-col gap-2">
            <div className="flex gap-2">
              <Button kind="secondary" disabled={busy} onClick={() => input.current?.click()}>
                {t(hasBackground ? 'account.preferences.changeBackground' : 'account.preferences.uploadBackground')}
              </Button>
              {hasBackground && (
                <Button kind="ghost" disabled={busy} onClick={() => remove.mutate()}>{t('account.preferences.removeBackground')}</Button>
              )}
            </div>
            <Typography variant="caption" color="textSecondary">{t('account.preferences.backgroundHint')}</Typography>
          </div>
          <input ref={input} type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden aria-label={t('account.preferences.uploadBackground')} onChange={pick} />
        </div>
        {hasBackground && (
          <div className="flex flex-col gap-1">
            <span id="background-opacity" style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
              {t('account.preferences.backgroundOpacity')}: {appearance.backgroundOpacity}%
            </span>
            <Slider
              aria-labelledby="background-opacity"
              min={MIN_OPACITY}
              max={100}
              step={OPACITY_STEP}
              value={appearance.backgroundOpacity}
              // The picture follows the handle at once; the choice is saved when it is let go.
              onChange={(_, value) => setAppearance({ backgroundOpacity: value })}
              onChangeCommitted={(_, value) => change({ backgroundOpacity: value })}
            />
          </div>
        )}
      </section>
    </div>
  );
}
