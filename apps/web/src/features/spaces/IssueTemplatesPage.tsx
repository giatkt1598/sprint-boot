import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { radius, sem } from '../../theme/tokens';
import { issueTemplatesKey, useIssueTemplates, useIssueTypes, type IssueTemplate, type IssueType } from '../issues/api';
import { RichTextEditor } from '../issues/RichTextEditor';
import { SpacePermission, type Space } from './api';

/** Epic first, then the standard types; subtasks have no templates. */
const typeOrder = (type: IssueType) => (type.hierarchy === 2 ? 0 : 1);

interface Draft {
  id?: string;
  issueTypeId: string;
  name: string;
  content: string;
  isDefault: boolean;
}

function TemplateDialog({ draft, types, projectId, onClose }: { draft: Draft; types: IssueType[]; projectId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(draft);

  const save = useMutation({
    mutationFn: () => {
      const body = { issueTypeId: value.issueTypeId, name: value.name, content: value.content || null, isDefault: value.isDefault };
      return value.id
        ? unwrap(apiClient.PUT('/api/v1/issue-template/{id}', { params: { path: { id: value.id } }, body }))
        : unwrap(apiClient.POST('/api/v1/issue-template', { params: { query: { projectId } }, body }));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: issueTemplatesKey(projectId) });
      toast('success', t('templates.saved'));
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const type = types.find((item) => item.id === value.issueTypeId);

  return (
    <Dialog
      open
      width={640}
      title={t(value.id ? 'templates.editTitle' : 'templates.createTitle', { type: t(`issue.type.${type?.icon ?? 'task'}`) })}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('common.save')}
          confirmDisabled={!value.name.trim() || save.isPending}
          onCancel={onClose}
          onConfirm={() => save.mutate()}
        />
      }
    >
      <TextField label={t('templates.name')} value={value.name} autoFocus onChange={(event) => setValue({ ...value, name: event.target.value })} />
      {/* No space is given, so a template holds no uploaded files an issue could later lose. */}
      <RichTextEditor
        label={t('templates.content')}
        showLabel
        people={[]}
        initialValue={draft.content}
        onChange={(content) => setValue((current) => ({ ...current, content }))}
      />
      <Checkbox
        label={t('templates.makeDefault')}
        checked={value.isDefault}
        onChange={(event) => setValue({ ...value, isDefault: event.target.checked })}
      />
    </Dialog>
  );
}

export function SpaceIssueTemplatesPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const projectId = space.id!;
  const canManage = space.permissions?.includes(SpacePermission.manage) ?? false;
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useIssueTypes(projectId);
  const templates = useIssueTemplates(projectId);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [removing, setRemoving] = useState<IssueTemplate | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/issue-template/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: issueTemplatesKey(projectId) });
      setRemoving(null);
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
        const typeName = t(`issue.type.${type.icon}`);
        const own = templates.data.filter((template) => template.issueTypeId === type.id);
        return (
          <section key={type.id} aria-labelledby={`templates-${type.id}`} className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <IssueTypeIcon type={type.icon as IconType} />
              <Typography id={`templates-${type.id}`} variant="h3" component="h2" className="flex-1">{typeName}</Typography>
              {canManage && (
                <Button
                  kind="ghost"
                  startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />}
                  aria-label={t('templates.addFor', { type: typeName })}
                  onClick={() => setDraft({ issueTypeId: type.id!, name: '', content: '', isDefault: own.length === 0 })}
                >
                  {t('templates.add')}
                </Button>
              )}
            </div>
            {own.length === 0 && <Typography variant="body2" color="textSecondary">{t('templates.none', { type: typeName })}</Typography>}
            {own.map((template) => (
              <div
                key={template.id}
                className="flex items-center gap-3 px-3 py-2"
                style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
              >
                <Typography variant="body2" className="min-w-0 flex-1 truncate">{template.name}</Typography>
                {template.isDefault && <Badge tone="info">{t('templates.default')}</Badge>}
                {canManage && (
                  <span className="flex gap-3">
                    <Link
                      component="button"
                      variant="body2"
                      aria-label={`${t('common.edit')} ${template.name}`}
                      onClick={() => setDraft({ id: template.id!, issueTypeId: template.issueTypeId!, name: template.name ?? '', content: template.content ?? '', isDefault: template.isDefault ?? false })}
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
                )}
              </div>
            ))}
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
