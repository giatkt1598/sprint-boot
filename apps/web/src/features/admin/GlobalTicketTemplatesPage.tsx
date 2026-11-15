import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AddIcon } from '../../components/icons';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { TicketTypeIcon } from '../../components/TicketIcons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import {
  globalTicketTemplatesKey,
  TEMPLATE_NAME_MAX,
  TemplateMode,
  TemplateTypeKeys,
  useGlobalTicketTemplates,
  type GlobalTicketTemplate,
  type TemplateModeValue,
  type TemplateTypeKey,
} from '../tickets/api';
import { RichTextEditor } from '../tickets/RichTextEditor';
import { TemplateModeField } from '../tickets/TemplateModeField';
import { usePageTitle } from '../../lib/pageTitle';

interface Draft {
  id?: string;
  ticketTypeKey: TemplateTypeKey;
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
      const body = { ticketTypeKey: value.ticketTypeKey, name: value.name, content: value.content || null, mode: value.mode };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/global-ticket-template/{id}', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/global-ticket-template', { body }));
    },
    onSuccess: async () => {
      // Spaces that reuse it read the new text too.
      await queryClient.invalidateQueries({ queryKey: globalTicketTemplatesKey });
      await queryClient.invalidateQueries({ queryKey: ['ticket-templates'] });
      toast('success', t('templates.saved'));
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <Dialog
      open
      width={640}
      title={t(value.id ? 'globalTemplates.editTitle' : 'globalTemplates.createTitle', { type: t(`ticket.type.${value.ticketTypeKey}`) })}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t('common.save')} confirmDisabled={!value.name.trim() || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />
      }
    >
      <TextField label={t('templates.name')} value={value.name} autoFocus inputProps={{ maxLength: TEMPLATE_NAME_MAX }} onChange={(event) => setValue({ ...value, name: event.target.value })} />
      <div className="flex flex-col gap-1">
        <SelectField
          label={t('globalTemplates.ticketType')}
          value={value.ticketTypeKey}
          disabled={value.inUse}
          onChange={(event) => setValue({ ...value, ticketTypeKey: event.target.value as TemplateTypeKey })}
          options={TemplateTypeKeys.map((key) => ({ value: key, label: t(`ticket.type.${key}`), icon: <TicketTypeIcon type={key} /> }))}
        />
        {value.inUse && <Typography variant="body2" color="textSecondary">{t('globalTemplates.typeLocked')}</Typography>}
      </div>
      {/* No space is given, so a template holds no uploaded files a ticket could later lose. */}
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

/** Workspace-wide templates by ticket type; every space can use them for its type of the same kind. */
export function GlobalTicketTemplatesPage() {
  const { t } = useTranslation();
  usePageTitle(t('globalTemplates.title'));
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const templates = useGlobalTicketTemplates();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<GlobalTicketTemplate | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/global-ticket-template/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: globalTicketTemplatesKey });
      await queryClient.invalidateQueries({ queryKey: ['ticket-templates'] });
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
        const typeName = t(`ticket.type.${key}`);
        const list = templates.data.filter((template) => template.ticketTypeKey === key);
        return (
          <section key={key} aria-labelledby={`global-templates-${key}`} className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <TicketTypeIcon type={key} />
              <Typography id={`global-templates-${key}`} variant="h3" component="h2" className="flex-1">{typeName}</Typography>
              <Button
                kind="ghost"
                startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />}
                aria-label={t('templates.addFor', { type: typeName })}
                onClick={() => setDraft({ ticketTypeKey: key, name: '', content: '', mode: TemplateMode.fill, inUse: false })}
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
                      ticketTypeKey: key,
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
