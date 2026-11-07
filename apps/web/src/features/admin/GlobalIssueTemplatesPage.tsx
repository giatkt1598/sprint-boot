import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { IssueTypeIcon } from '../../components/IssueIcons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import {
  globalIssueTemplatesKey,
  TEMPLATE_NAME_MAX,
  TemplateMode,
  TemplateTypeKeys,
  useGlobalIssueTemplates,
  type GlobalIssueTemplate,
  type TemplateModeValue,
  type TemplateTypeKey,
} from '../issues/api';
import { RichTextEditor } from '../issues/RichTextEditor';
import { TemplateModeField } from '../issues/TemplateModeField';

interface Draft {
  id?: string;
  issueTypeKey: TemplateTypeKey;
  name: string;
  content: string;
  mode: TemplateModeValue;
  /** Spaces use it, so its type stays. */
  inUse: boolean;
}

function GlobalTemplateDialog({ draft, onClose }: { draft: Draft; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);

  const save = useMutation({
    mutationFn: () => {
      const body = { issueTypeKey: value.issueTypeKey, name: value.name, content: value.content || null, mode: value.mode };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/global-issue-template/{id}', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/global-issue-template', { body }));
    },
    onSuccess: async () => {
      // Spaces that reuse it read the new text too.
      await queryClient.invalidateQueries({ queryKey: globalIssueTemplatesKey });
      await queryClient.invalidateQueries({ queryKey: ['issue-templates'] });
      toast('success', t('templates.saved'));
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <Dialog
      open
      width={640}
      title={t(value.id ? 'globalTemplates.editTitle' : 'globalTemplates.createTitle', { type: t(`issue.type.${value.issueTypeKey}`) })}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t('common.save')} confirmDisabled={!value.name.trim() || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />
      }
    >
      <TextField label={t('templates.name')} value={value.name} autoFocus inputProps={{ maxLength: TEMPLATE_NAME_MAX }} onChange={(event) => setValue({ ...value, name: event.target.value })} />
      <div className="flex flex-col gap-1">
        <SelectField
          label={t('globalTemplates.issueType')}
          value={value.issueTypeKey}
          disabled={value.inUse}
          onChange={(event) => setValue({ ...value, issueTypeKey: event.target.value as TemplateTypeKey })}
          options={TemplateTypeKeys.map((key) => ({ value: key, label: t(`issue.type.${key}`), icon: <IssueTypeIcon type={key} /> }))}
        />
        {value.inUse && <Typography variant="body2" color="textSecondary">{t('globalTemplates.typeLocked')}</Typography>}
      </div>
      {/* No space is given, so a template holds no uploaded files an issue could later lose. */}
      <RichTextEditor
        label={t('templates.content')}
        showLabel
        people={[]}
        initialValue={draft.content}
        onChange={(content) => setValue((current) => ({ ...current, content }))}
      />
      <TemplateModeField value={value.mode} onChange={(mode) => setValue({ ...value, mode })} />
    </Dialog>
  );
}

/** Workspace-wide templates by issue type; every space can use them for its type of the same kind. */
export function GlobalIssueTemplatesPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const templates = useGlobalIssueTemplates();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<GlobalIssueTemplate | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/global-issue-template/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: globalIssueTemplatesKey });
      await queryClient.invalidateQueries({ queryKey: ['issue-templates'] });
      setRemoving(null);
    },
    onError: (failure) => {
      setRemoving(null);
      toast('error', toMessage(failure));
    },
  });

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('globalTemplates.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('globalTemplates.subtitle')}</Typography>
      </div>
      {templates.isPending && <LoadingState />}
      {templates.isError && <ErrorState onRetry={() => void templates.refetch()} />}
      {templates.data && TemplateTypeKeys.map((key) => {
        const typeName = t(`issue.type.${key}`);
        const list = templates.data.filter((template) => template.issueTypeKey === key);
        return (
          <section key={key} aria-labelledby={`global-templates-${key}`} className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <IssueTypeIcon type={key} />
              <Typography id={`global-templates-${key}`} variant="h3" component="h2" className="flex-1">{typeName}</Typography>
              <Button
                kind="ghost"
                startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />}
                aria-label={t('templates.addFor', { type: typeName })}
                onClick={() => setDraft({ issueTypeKey: key, name: '', content: '', mode: TemplateMode.fill, inUse: false })}
              >
                {t('templates.add')}
              </Button>
            </div>
            {list.length === 0 && <Typography variant="body2" color="textSecondary">{t('globalTemplates.none', { type: typeName })}</Typography>}
            {list.map((template) => (
              <div
                key={template.id}
                className="flex min-h-[42px] items-center gap-3 px-3 py-2"
                style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
              >
                <Typography variant="body2" className="min-w-0 flex-1 truncate">{template.name}</Typography>
                {template.mode === TemplateMode.placeholder && <Badge tone="subtle">{t('templates.mode.placeholder')}</Badge>}
                <Typography variant="body2" color="textSecondary" className="whitespace-nowrap">
                  {t('globalTemplates.spaceCount', { count: template.usageCount ?? 0 })}
                </Typography>
                <span className="flex gap-3">
                  <Link
                    component="button"
                    variant="body2"
                    aria-label={`${t('common.edit')} ${template.name}`}
                    onClick={() => setDraft({
                      id: template.id!,
                      issueTypeKey: key,
                      name: template.name ?? '',
                      content: template.content ?? '',
                      mode: template.mode ?? TemplateMode.fill,
                      inUse: (template.usageCount ?? 0) > 0,
                    })}
                  >
                    {t('common.edit')}
                  </Link>
                  <Link
                    component="button"
                    variant="body2"
                    aria-label={`${t('common.delete')} ${template.name}`}
                    sx={{ color: sem('feedback-danger-text') }}
                    onClick={() => setRemoving(template)}
                  >
                    {t('common.delete')}
                  </Link>
                </span>
              </div>
            ))}
          </section>
        );
      })}
      {draft && <GlobalTemplateDialog draft={draft} onClose={() => setDraft(null)} />}
      <DialogConfirm
        open={removing !== null}
        title={t('templates.deleteTitle', { name: removing?.name })}
        description={
          removing?.usageCount
            ? t('globalTemplates.deleteUsed', { count: removing.usageCount })
            : t('templates.deleteBody')
        }
        confirmLabel={t('common.delete')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id!)}
      />
    </div>
  );
}
