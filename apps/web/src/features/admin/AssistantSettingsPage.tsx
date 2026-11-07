import Autocomplete from '@mui/material/Autocomplete';
import Link from '@mui/material/Link';
import MuiTextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox, Toggle } from '../../components/Checkbox';
import { Dialog, DialogConfirm } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, sem, size } from '../../theme/tokens';
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

interface ModelOption {
  id: string;
  name?: string | null;
}

/** Free text, with the models the provider offers once they are loaded. */
function ModelField({ label, placeholder, value, models, onChange }: {
  label: string;
  placeholder: string;
  value: string;
  models: ModelOption[];
  onChange: (model: string) => void;
}) {
  const id = useId();
  // Opening the list shows every model; typing narrows it.
  const [typed, setTyped] = useState(false);
  return (
    <div className="flex flex-1 flex-col gap-1">
      <label htmlFor={id} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
        {label}
      </label>
      <Autocomplete<ModelOption, false, false, true>
        id={id}
        size="small"
        freeSolo
        options={models}
        inputValue={value}
        onOpen={() => setTyped(false)}
        onInputChange={(_, text, reason) => {
          if (reason === 'input') {
            setTyped(true);
          }
          if (reason !== 'reset') {
            onChange(text);
          }
        }}
        onChange={(_, picked) => onChange(typeof picked === 'string' ? picked : (picked?.id ?? ''))}
        getOptionLabel={(option) => (typeof option === 'string' ? option : option.id)}
        filterOptions={(list, state) => {
          const text = state.inputValue.trim().toLowerCase();
          return typed && text
            ? list.filter((option) => option.id.toLowerCase().includes(text) || option.name?.toLowerCase().includes(text))
            : list;
        }}
        renderOption={({ key, ...props }, option) => (
          <li key={key} {...props}>
            <span className="flex flex-col">
              <span>{option.id}</span>
              {option.name && option.name !== option.id && (
                <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{option.name}</span>
              )}
            </span>
          </li>
        )}
        renderInput={(params) => (
          <MuiTextField
            {...params}
            placeholder={placeholder}
            autoComplete="off"
            sx={{ '& .MuiOutlinedInput-root': { height: size.input, paddingBlock: 0 } }}
          />
        )}
      />
    </div>
  );
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
  const [models, setModels] = useState<ModelOption[]>([]);
  const [result, setResult] = useState<{ succeeded: boolean; message: string } | null>(null);
  const anthropic = value.provider === Provider.anthropic;
  const update = (changes: Partial<Draft>) => {
    setValue({ ...value, ...changes });
    setResult(null);
  };

  /** The form as typed; a blank key on an existing connection means the stored one. */
  const settings = () => ({
    provider: value.provider as 0 | 1,
    baseUrl: value.baseUrl || null,
    apiKey: value.apiKey || null,
    connectionId: value.id ?? null,
  });

  const loadModels = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/assistant-admin/list-models', { body: settings() })),
    onSuccess: (list) => {
      const loaded = list.map((model) => ({ id: model.id ?? '', name: model.name })).filter((model) => model.id);
      setModels(loaded);
      toast(loaded.length ? 'success' : 'error', t(loaded.length ? 'assistantAdmin.modelsLoaded' : 'assistantAdmin.noModels', { count: loaded.length }));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const test = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/assistant-admin/test-settings', {
      body: {
        ...settings(),
        model: value.model.trim() || (anthropic ? ANTHROPIC_MODEL : ''),
        effort: anthropic && value.effort ? value.effort : null,
      },
    })),
    onSuccess: (outcome) => setResult({ succeeded: outcome.succeeded ?? false, message: outcome.message ?? '' }),
    onError: (failure) => setResult({ succeeded: false, message: toMessage(failure) }),
  });

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
  const reachable = anthropic || value.baseUrl.trim();
  const testable = reachable && (anthropic || value.model.trim());

  return (
    <Dialog
      open
      width={560}
      title={t(value.id ? 'assistantAdmin.editTitle' : 'assistantAdmin.addTitle')}
      onClose={onClose}
      footer={
        <div className="flex items-center gap-2">
          <Button kind="secondary" disabled={!testable || test.isPending} onClick={() => test.mutate()}>
            {t(test.isPending ? 'assistantAdmin.testing' : 'assistantAdmin.testConnection')}
          </Button>
          <span className="flex-1" />
          <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          <Button disabled={!ready || save.isPending} onClick={() => save.mutate()}>{t('common.save')}</Button>
        </div>
      }
    >
      <TextField autoComplete="off" label={t('assistantAdmin.name')} autoFocus value={value.name} onChange={(event) => update({ name: event.target.value })} />
      <SelectField
        label={t('assistantAdmin.provider')}
        value={String(value.provider)}
        onChange={(event) => {
          update({ provider: Number(event.target.value) });
          setModels([]);
        }}
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
        onChange={(event) => update({ baseUrl: event.target.value })}
      />
      {!anthropic && <Typography variant="body2" color="textSecondary">{t('assistantAdmin.baseUrlHint')}</Typography>}
      <TextField
        label={t('assistantAdmin.apiKey')}
        type="password"
        autoComplete="new-password"
        placeholder={value.hasApiKey ? '••••••••••••' : anthropic ? undefined : t('assistantAdmin.apiKeyOptional')}
        value={value.apiKey}
        onChange={(event) => update({ apiKey: event.target.value })}
      />
      <div className="flex items-end gap-2">
        <ModelField
          label={t('assistantAdmin.model')}
          placeholder={anthropic ? ANTHROPIC_MODEL : 'gpt-5.1, llama3.3, …'}
          value={value.model}
          models={models}
          onChange={(model) => update({ model })}
        />
        {/* As tall as the Model field beside it. */}
        <Button kind="secondary" sx={{ height: size.input, flexShrink: 0 }} disabled={!reachable || loadModels.isPending} onClick={() => loadModels.mutate()}>
          {t(loadModels.isPending ? 'assistantAdmin.loadingModels' : 'assistantAdmin.loadModels')}
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-4">
        <TextField
          autoComplete="off"
          label={t('assistantAdmin.maxOutputTokens')}
          type="number"
          placeholder={anthropic ? '16000' : undefined}
          value={value.maxOutputTokens}
          onChange={(event) => update({ maxOutputTokens: event.target.value })}
        />
        {anthropic && (
          <SelectField
            label={t('assistantAdmin.effort')}
            value={value.effort || 'low'}
            onChange={(event) => update({ effort: event.target.value })}
            options={EFFORTS.map((effort) => ({ value: effort, label: t(`assistantAdmin.efforts.${effort}`) }))}
          />
        )}
      </div>
      <Checkbox label={t('assistantAdmin.makeDefault')} checked={value.isDefault} onChange={(event) => update({ isDefault: event.target.checked })} />
      {result && (
        <Typography role="status" variant="body2" sx={{ color: sem(result.succeeded ? 'feedback-success-text' : 'feedback-danger-text') }}>
          {t(result.succeeded ? 'assistantAdmin.testPassed' : 'assistantAdmin.testFailed', { message: result.message })}
        </Typography>
      )}
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
