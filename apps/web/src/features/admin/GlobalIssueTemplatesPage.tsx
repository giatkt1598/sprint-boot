import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { sem } from '../../theme/tokens';
import { globalIssueTemplatesKey, TemplateMode, useGlobalIssueTemplates, type GlobalIssueTemplate, type TemplateModeValue } from '../issues/api';
import { RichTextEditor } from '../issues/RichTextEditor';
import { TemplateModeField } from '../issues/TemplateModeField';

interface Draft {
  id?: string;
  name: string;
  content: string;
  mode: TemplateModeValue;
}

function GlobalTemplateDialog({ draft, onClose }: { draft: Draft; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);

  const save = useMutation({
    mutationFn: () => {
      const body = { name: value.name, content: value.content || null, mode: value.mode };
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
      title={t(value.id ? 'globalTemplates.editTitle' : 'globalTemplates.createTitle')}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t('common.save')} confirmDisabled={!value.name.trim() || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />
      }
    >
      <TextField label={t('templates.name')} value={value.name} autoFocus onChange={(event) => setValue({ ...value, name: event.target.value })} />
      <TemplateModeField value={value.mode} onChange={(mode) => setValue({ ...value, mode })} />
      {/* No space is given, so a template holds no uploaded files an issue could later lose. */}
      <RichTextEditor
        label={t('templates.content')}
        showLabel
        people={[]}
        initialValue={draft.content}
        onChange={(content) => setValue((current) => ({ ...current, content }))}
      />
    </Dialog>
  );
}

/** Workspace-wide templates, which every space can reuse for its own issue types. */
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
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('globalTemplates.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('globalTemplates.subtitle')}</Typography>
        </div>
        <Button onClick={() => setDraft({ name: '', content: '', mode: TemplateMode.fill })}>{t('templates.add')}</Button>
      </div>
      {templates.isPending && <LoadingState />}
      {templates.isError && <ErrorState onRetry={() => void templates.refetch()} />}
      {templates.data && templates.data.length === 0 && (
        <Typography variant="body2" color="textSecondary">{t('globalTemplates.empty')}</Typography>
      )}
      {templates.data && templates.data.length > 0 && (
        <Table<GlobalIssueTemplate>
          label={t('globalTemplates.title')}
          rows={templates.data}
          rowKey={(item) => item.id!}
          columns={[
            { key: 'name', header: t('templates.name'), render: (item) => item.name },
            {
              key: 'mode',
              header: t('templates.mode.label'),
              render: (item) => t(item.mode === TemplateMode.placeholder ? 'templates.mode.placeholder' : 'templates.mode.fill'),
            },
            { key: 'used', header: t('globalTemplates.used'), render: (item) => t('globalTemplates.spaceCount', { count: item.usageCount ?? 0 }) },
            {
              key: 'actions',
              header: <span className="sr-only">{t('invitations.actions')}</span>,
              render: (item) => (
                <span className="flex gap-3 whitespace-nowrap">
                  <Link
                    component="button"
                    variant="body2"
                    aria-label={`${t('common.edit')} ${item.name}`}
                    onClick={() => setDraft({ id: item.id!, name: item.name ?? '', content: item.content ?? '', mode: item.mode ?? TemplateMode.fill })}
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
