import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AddIcon } from '../../components/icons';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import {
  globalTicketTemplatesKey,
  ticketTemplatesKey,
  TEMPLATE_NAME_MAX,
  TemplateMode,
  useGlobalTicketTemplates,
  useTicketTemplates,
  useTicketTypes,
  type GlobalTicketTemplate,
  type TicketTemplate,
  type TicketType,
  type TemplateModeValue,
} from '../tickets/api';
import { RichTextEditor, RichTextView } from '../tickets/RichTextEditor';
import { TemplateModeField } from '../tickets/TemplateModeField';
import { SpacePermission, type Space } from './api';

/** Epic first, then the standard types; subtasks have no templates. */
const typeOrder = (type: TicketType) => (type.hierarchy === 2 ? 0 : 1);

interface Draft {
  id?: string;
  ticketTypeId: string;
  name: string;
  content: string;
  mode: TemplateModeValue;
  /** Empty for a template written for this space. */
  globalTemplateId: string;
  isDefault: boolean;
}

const OWN = '';

/** A row of a type's list: a template of the space, or a workspace one it could use. */
type Row = { kind: 'own'; item: TicketTemplate } | { kind: 'available'; item: GlobalTicketTemplate };

function TemplateDialog({ draft, types, projectId, onClose }: { draft: Draft; types: TicketType[]; projectId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);
  const type = types.find((item) => item.id === value.ticketTypeId);
  const globals = useGlobalTicketTemplates();
  const offered = (globals.data ?? []).filter((item) => item.ticketTypeKey === type?.icon);
  const reused = offered.find((item) => item.id === value.globalTemplateId);

  const save = useMutation({
    mutationFn: () => {
      const body = value.globalTemplateId
        ? { ticketTypeId: value.ticketTypeId, globalTemplateId: value.globalTemplateId, isDefault: value.isDefault }
        : { ticketTypeId: value.ticketTypeId, name: value.name, content: value.content || null, mode: value.mode, isDefault: value.isDefault };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/ticket-template/{id}', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/ticket-template', { params: { query: { projectId } }, body }));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ticketTemplatesKey(projectId) });
      toast('success', t('templates.saved'));
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <Dialog
      open
      width={640}
      title={t(value.id ? 'templates.editTitle' : 'templates.createTitle', { type: t(`ticket.type.${type?.icon ?? 'task'}`) })}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('common.save')}
          confirmDisabled={(value.globalTemplateId ? !reused : !value.name.trim()) || save.isPending}
          onCancel={onClose}
          onConfirm={() => save.mutate()}
        />
      }
    >
      {offered.length > 0 && (
        <SelectField
          label={t('templates.source')}
          value={value.globalTemplateId}
          onChange={(event) => setValue({ ...value, globalTemplateId: event.target.value })}
          options={[
            { value: OWN, label: t('templates.ownSource') },
            ...offered.map((item) => ({ value: item.id!, label: item.name! })),
          ]}
        />
      )}
      {value.globalTemplateId ? (
        reused && (
          <div className="flex flex-col gap-2">
            <Typography variant="body2" color="textSecondary">
              {t('templates.reusedHint', { mode: t(reused.mode === TemplateMode.placeholder ? 'templates.mode.placeholder' : 'templates.mode.fill') })}
            </Typography>
            <div className="px-3 py-2" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
              <RichTextView html={reused.content ?? ''} />
            </div>
          </div>
        )
      ) : (
        <>
          <TextField label={t('templates.name')} value={value.name} autoFocus inputProps={{ maxLength: TEMPLATE_NAME_MAX }} onChange={(event) => setValue({ ...value, name: event.target.value })} />
          {/* No space is given, so a template holds no uploaded files a ticket could later lose. */}
          <RichTextEditor
            label={t('templates.content')}
            showLabel
            people={[]}
            initialValue={draft.content}
            onChange={(content) => setValue((current) => ({ ...current, content }))}
          />
          <TemplateModeField value={value.mode} onChange={(mode) => setValue({ ...value, mode })} />
        </>
      )}
      <Checkbox
        label={t('templates.makeDefault')}
        checked={value.isDefault}
        onChange={(event) => setValue({ ...value, isDefault: event.target.checked })}
      />
    </Dialog>
  );
}

export function SpaceTicketTemplatesPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const projectId = space.id!;
  const canManage = space.permissions?.includes(SpacePermission.manage) ?? false;
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useTicketTypes(projectId);
  const templates = useTicketTemplates(projectId);
  const globals = useGlobalTicketTemplates();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<TicketTemplate | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/ticket-template/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ticketTemplatesKey(projectId) });
      setRemoving(null);
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  /** Makes a template the one new tickets of its type start with; a workspace one is added to the space first. */
  const use = useMutation({
    mutationFn: ({ ticketTypeId, template }: { ticketTypeId: string; template: Row }) => {
      if (template.kind === 'available') {
        return unwrap(apiClient.POST('/api/v1/ticket-template', {
          params: { query: { projectId } },
          body: { ticketTypeId, globalTemplateId: template.item.id!, isDefault: true },
        }));
      }
      const own = template.item;
      const body = own.globalTemplateId
        ? { ticketTypeId, globalTemplateId: own.globalTemplateId, isDefault: true }
        : { ticketTypeId, name: own.name!, content: own.content || null, mode: own.mode ?? TemplateMode.fill, isDefault: true };
      return unwrap(apiClient.PUT('/api/v1/ticket-template/{id}', { params: { path: { id: own.id! } }, body }));
    },
    onSuccess: async (_, { template }) => {
      await queryClient.invalidateQueries({ queryKey: ticketTemplatesKey(projectId) });
      await queryClient.invalidateQueries({ queryKey: globalTicketTemplatesKey });
      toast('success', t('templates.used', { name: template.item.name }));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const usable = [...(types.data ?? [])].filter((type) => type.hierarchy !== 0).sort((a, b) => typeOrder(a) - typeOrder(b));

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{t('templates.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('templates.subtitle')}</Typography>
      </div>
      {(types.isPending || templates.isPending) && <LoadingState />}
      {(types.isError || templates.isError) && <ErrorState onRetry={() => void Promise.all([types.refetch(), templates.refetch()])} />}
      {types.data && templates.data && usable.map((type) => {
        const typeName = t(`ticket.type.${type.icon}`);
        const own = templates.data.filter((template) => template.ticketTypeId === type.id);
        // Workspace templates for this type that the space does not use yet.
        const available = (globals.data ?? []).filter(
          (global) => global.ticketTypeKey === type.icon && !own.some((template) => template.globalTemplateId === global.id),
        );
        // Global ones first: those the space already has, then the rest of the workspace's, then its own.
        const rows: Row[] = [
          ...own.filter((template) => template.globalTemplateId).map((item) => ({ kind: 'own' as const, item })),
          ...available.map((item) => ({ kind: 'available' as const, item })),
          ...own.filter((template) => !template.globalTemplateId).map((item) => ({ kind: 'own' as const, item })),
        ];
        return (
          <section key={type.id} aria-labelledby={`templates-${type.id}`} className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <TicketTypeIcon type={type.icon as IconType} />
              <Typography id={`templates-${type.id}`} variant="h3" component="h2" className="flex-1">{typeName}</Typography>
              {canManage && (
                <Button
                  kind="ghost"
                  startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />}
                  aria-label={t('templates.addFor', { type: typeName })}
                  onClick={() =>
                    setDraft({ ticketTypeId: type.id!, name: '', content: '', mode: TemplateMode.fill, globalTemplateId: OWN, isDefault: own.length === 0 })
                  }
                >
                  {t('templates.add')}
                </Button>
              )}
            </div>
            {rows.length === 0 && <Typography variant="body2" color="textSecondary">{t('templates.none', { type: typeName })}</Typography>}
            {rows.map((row) => {
              const template = row.item;
              const isGlobal = row.kind === 'available' || Boolean(row.item.globalTemplateId);
              const used = row.kind === 'own' && row.item.isDefault;
              return (
                <Box
                  key={`${row.kind}-${template.id}`}
                  className="flex min-h-[42px] items-center gap-3 px-3 py-2"
                  sx={{
                    border: `1px ${row.kind === 'available' ? 'dashed' : 'solid'} ${sem('border-default')}`,
                    borderRadius: radius.card,
                    '&:hover': { backgroundColor: sem('bg-hover') },
                  }}
                >
                  <Typography variant="body2" className="min-w-0 flex-1 truncate">
                    {template.name}
                    {isGlobal && <em className="ml-2">({t('templates.global')})</em>}
                  </Typography>
                  {used && <Badge tone="info">{t('templates.inUse')}</Badge>}
                  {canManage && !used && (
                    <Link
                      component="button"
                      variant="body2"
                      disabled={use.isPending}
                      aria-label={`${t('templates.use')} ${template.name}`}
                      onClick={() => use.mutate({ ticketTypeId: type.id!, template: row })}
                    >
                      {t('templates.use')}
                    </Link>
                  )}
                  {canManage && row.kind === 'own' && (
                    <span className="flex gap-3">
                      <Link
                        component="button"
                        variant="body2"
                        aria-label={`${t('common.edit')} ${row.item.name}`}
                        onClick={() =>
                          setDraft({
                            id: row.item.id!,
                            ticketTypeId: row.item.ticketTypeId!,
                            name: row.item.name ?? '',
                            content: row.item.content ?? '',
                            mode: row.item.mode ?? TemplateMode.fill,
                            globalTemplateId: row.item.globalTemplateId ?? OWN,
                            isDefault: row.item.isDefault ?? false,
                          })
                        }
                      >
                        {t('common.edit')}
                      </Link>
                      <Link
                        component="button"
                        variant="body2"
                        aria-label={`${t('common.delete')} ${row.item.name}`}
                        sx={{ color: sem('feedback-danger-text') }}
                        onClick={() => setRemoving(row.item)}
                      >
                        {t('common.delete')}
                      </Link>
                    </span>
                  )}
                </Box>
              );
            })}
          </section>
        );
      })}
      {draft && <TemplateDialog draft={draft} types={usable} projectId={projectId} onClose={() => setDraft(null)} />}
      <DialogConfirm
        open={removing !== null}
        title={t('templates.deleteTitle', { name: removing?.name })}
        description={t('templates.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id!)}
      />
    </div>
  );
}
