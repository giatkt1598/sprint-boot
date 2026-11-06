import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox, Toggle } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { sem } from '../../theme/tokens';
import { assistantKeys, Provider, type AiConnection } from '../assistant/api';

const KEY = ['admin', 'assistant'];

const EFFORTS = ['low', 'medium', 'high', 'xhigh', 'max'];

/** Claude's current default model for chat. */
const ANTHROPIC_MODEL = 'claude-opus-5-5';

interface Draft {
  id?: string;
  name: string;
  provider: number;
  baseUrl: string;
  apiKey: string;
  hasApiKey: boolean;
  model: string;
  maxOutputTokens: string;
  effort: string;
  isDefault: boolean;
}

const emptyDraft: Draft = {
  name: '', provider: Provider.openAiCompatible, baseUrl: '', apiKey: '', hasApiKey: false, model: '', maxOutputTokens: '', effort: '', isDefault: false,
};

function ConnectionDialog({ draft, onClose }: { draft: Draft; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);
  const anthropic = value.provider === Provider.anthropic;

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: value.name,
        provider: value.provider as 0 | 1,
        baseUrl: value.baseUrl || null,
        apiKey: value.apiKey || null,
        model: value.model || (anthropic ? ANTHROPIC_MODEL : ''),
        maxOutputTokens: value.maxOutputTokens ? Number(value.maxOutputTokens) : null,
        effort: anthropic && value.effort ? value.effort : null,
        isDefault: value.isDefault,
      };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/assistant-admin/{id}/connection', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/assistant-admin/connection', { body }));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: KEY });
      await queryClient.invalidateQueries({ queryKey: assistantKeys.status });
      toast('success', t('account.saved'));
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const ready = value.name.trim() && (anthropic || (value.baseUrl.trim() && value.model.trim()));

  return (
    <Dialog
      open
      width={560}
      title={t(value.id ? 'assistantAdmin.editTitle' : 'assistantAdmin.addTitle')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('common.save')} confirmDisabled={!ready || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />}
    >
      <TextField autoComplete="off" label={t('assistantAdmin.name')} autoFocus value={value.name} onChange={(event) => setValue({ ...value, name: event.target.value })} />
      <SelectField
        label={t('assistantAdmin.provider')}
        value={String(value.provider)}
        onChange={(event) => setValue({ ...value, provider: Number(event.target.value) })}
        options={[
          { value: String(Provider.openAiCompatible), label: t('assistantAdmin.openAiCompatible') },
          { value: String(Provider.anthropic), label: 'Anthropic' },
        ]}
      />
      <TextField
        autoComplete="off"
        label={t(anthropic ? 'assistantAdmin.baseUrlOptional' : 'assistantAdmin.baseUrl')}
        placeholder={anthropic ? 'https://api.anthropic.com' : 'https://api.openai.com/v1'}
        value={value.baseUrl}
        onChange={(event) => setValue({ ...value, baseUrl: event.target.value })}
      />
      {!anthropic && <Typography variant="body2" color="textSecondary">{t('assistantAdmin.baseUrlHint')}</Typography>}
      <TextField
        label={t('assistantAdmin.apiKey')}
        type="password"
        autoComplete="new-password"
        placeholder={value.hasApiKey ? '••••••••••••' : anthropic ? undefined : t('assistantAdmin.apiKeyOptional')}
        value={value.apiKey}
        onChange={(event) => setValue({ ...value, apiKey: event.target.value })}
      />
      <TextField
        autoComplete="off"
        label={t('assistantAdmin.model')}
        placeholder={anthropic ? ANTHROPIC_MODEL : 'gpt-5.1, llama3.3, …'}
        value={value.model}
        onChange={(event) => setValue({ ...value, model: event.target.value })}
      />
      <div className="grid grid-cols-2 gap-4">
        <TextField
          autoComplete="off"
          label={t('assistantAdmin.maxOutputTokens')}
          type="number"
          placeholder={anthropic ? '16000' : undefined}
          value={value.maxOutputTokens}
          onChange={(event) => setValue({ ...value, maxOutputTokens: event.target.value })}
        />
        {anthropic && (
          <SelectField
            label={t('assistantAdmin.effort')}
            value={value.effort || 'low'}
            onChange={(event) => setValue({ ...value, effort: event.target.value })}
            options={EFFORTS.map((effort) => ({ value: effort, label: t(`assistantAdmin.efforts.${effort}`) }))}
          />
        )}
      </div>
      <Checkbox label={t('assistantAdmin.makeDefault')} checked={value.isDefault} onChange={(event) => setValue({ ...value, isDefault: event.target.checked })} />
    </Dialog>
  );
}

/** Connections to AI providers, the default one the assistant uses, and the switch for the whole feature. */
export function AssistantSettingsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: KEY, queryFn: () => unwrap(apiClient.GET('/api/v1/assistant-admin')) });
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<AiConnection | null>(null);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: KEY });
    await queryClient.invalidateQueries({ queryKey: assistantKeys.status });
  };

  const toggle = useMutation({
    mutationFn: (enabled: boolean) => unwrap(apiClient.PUT('/api/v1/assistant-admin', { body: { enabled } })),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const test = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/assistant-admin/{id}/test-connection', { params: { path: { id } } })),
    onSuccess: (result) => toast(result.succeeded ? 'success' : 'error', t(result.succeeded ? 'assistantAdmin.testPassed' : 'assistantAdmin.testFailed', { message: result.message })),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/assistant-admin/{id}/connection', { params: { path: { id } } })),
    onSuccess: async () => {
      setRemoving(null);
      await refresh();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('assistantAdmin.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('assistantAdmin.subtitle')}</Typography>
        </div>
        <Button onClick={() => setDraft(emptyDraft)}>{t('assistantAdmin.add')}</Button>
      </div>
      {settings.isPending && <LoadingState />}
      {settings.isError && <ErrorState onRetry={() => void settings.refetch()} />}
      {settings.data && (
        <>
          <div className="flex flex-col gap-1">
            <Toggle
              label={t('assistantAdmin.enabled')}
              checked={settings.data.enabled ?? false}
              disabled={toggle.isPending}
              onChange={(event) => toggle.mutate(event.target.checked)}
            />
            <Typography variant="body2" color="textSecondary">{t('assistantAdmin.enabledHint')}</Typography>
            {settings.data.enabled && (settings.data.connections ?? []).length === 0 && (
              <Typography variant="body2" sx={{ color: sem('feedback-warning-text') }}>{t('assistantAdmin.noConnection')}</Typography>
            )}
          </div>
          {(settings.data.connections ?? []).length > 0 && (
            <Table<AiConnection>
              label={t('assistantAdmin.connections')}
              rows={settings.data.connections ?? []}
              rowKey={(item) => item.id!}
              columns={[
                {
                  key: 'name',
                  header: t('assistantAdmin.name'),
                  render: (item) => (
                    <span className="flex items-center gap-2">
                      {item.name}
                      {item.isDefault && <Badge tone="info">{t('assistantAdmin.default')}</Badge>}
                    </span>
                  ),
                },
                { key: 'provider', header: t('assistantAdmin.provider'), render: (item) => (item.provider === Provider.anthropic ? 'Anthropic' : t('assistantAdmin.openAiCompatible')) },
                { key: 'model', header: t('assistantAdmin.model'), render: (item) => item.model },
                {
                  key: 'actions',
                  header: <span className="sr-only">{t('invitations.actions')}</span>,
                  render: (item) => (
                    <span className="flex gap-3 whitespace-nowrap">
                      <Link component="button" variant="body2" disabled={test.isPending} onClick={() => test.mutate(item.id!)}>
                        {t('assistantAdmin.test')}
                      </Link>
                      <Link
                        component="button"
                        variant="body2"
                        aria-label={`${t('common.edit')} ${item.name}`}
                        onClick={() => setDraft({
                          id: item.id!,
                          name: item.name ?? '',
                          provider: item.provider ?? Provider.openAiCompatible,
                          baseUrl: item.baseUrl ?? '',
                          apiKey: '',
                          hasApiKey: item.hasApiKey ?? false,
                          model: item.model ?? '',
                          maxOutputTokens: item.maxOutputTokens ? String(item.maxOutputTokens) : '',
                          effort: item.effort ?? '',
                          isDefault: item.isDefault ?? false,
                        })}
                      >
                        {t('common.edit')}
                      </Link>
                      <Link
                        component="button"
                        variant="body2"
                        aria-label={`${t('common.delete')} ${item.name}`}
                        sx={{ color: sem('feedback-danger-text') }}
                        onClick={() => setRemoving(item)}
                      >
                        {t('common.delete')}
                      </Link>
                    </span>
                  ),
                },
              ]}
            />
          )}
          <Typography variant="body2" color="textSecondary">{t('assistantAdmin.privacy')}</Typography>
        </>
      )}
      {draft && <ConnectionDialog draft={draft} onClose={() => setDraft(null)} />}
      <DialogConfirm
        open={removing !== null}
        title={t('assistantAdmin.deleteTitle', { name: removing?.name })}
        description={t('assistantAdmin.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id!)}
      />
    </div>
  );
}
