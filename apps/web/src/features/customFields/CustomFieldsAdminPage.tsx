import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { usePageTitle } from '../../lib/pageTitle';
import { font, sem } from '../../theme/tokens';
import { optionTone, useCustomFields, useRefreshFields, type CustomField } from './api';
import { CustomFieldDialog } from './CustomFieldDialog';

/** The options of a select field as their chips, hidden ones left out. */
export function OptionChips({ field }: { field: CustomField }) {
  const shown = (field.options ?? []).filter((option) => !option.isHidden);
  return shown.length === 0 ? null : (
    <span className="flex flex-wrap gap-1">
      {shown.map((option) => <Badge key={option.id} tone={optionTone(option.color)}>{option.label}</Badge>)}
    </span>
  );
}

/** Asks before a field goes, saying how many tickets lose a shown value. */
export function DeleteFieldDialog({ field, onClose }: { field: CustomField | null; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefreshFields();
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/custom-field/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (failure) => {
      onClose();
      toast('error', toMessage(failure));
    },
  });
  const count = field?.usageCount ?? 0;

  return (
    <DialogConfirm
      open={field !== null}
      title={t('customFields.deleteTitle', { name: field?.name })}
      description={count > 0 ? t('customFields.deleteBody', { count }) : t('customFields.deleteBodyUnused')}
      confirmLabel={t('common.delete')}
      onCancel={onClose}
      onConfirm={() => field && remove.mutate(field.id!)}
    />
  );
}

/** Administration → Custom fields: the fields every space can turn on. */
export function CustomFieldsAdminPage() {
  const { t } = useTranslation();
  usePageTitle(t('customFields.title'));
  const fields = useCustomFields();
  const [editing, setEditing] = useState<CustomField | 'new' | null>(null);
  const [removing, setRemoving] = useState<CustomField | null>(null);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('customFields.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('customFields.subtitle')}</Typography>
        </div>
        <Button onClick={() => setEditing('new')}>{t('customFields.add')}</Button>
      </div>
      {fields.isPending && <LoadingState />}
      {fields.isError && <ErrorState onRetry={() => void fields.refetch()} />}
      {fields.data?.length === 0 && <EmptyState title={t('customFields.empty')} />}
      {fields.data && fields.data.length > 0 && (
        <Table<CustomField>
          label={t('customFields.title')}
          rows={fields.data}
          rowKey={(item) => item.id!}
          columns={[
            {
              key: 'name',
              header: t('customFields.name'),
              render: (item) => (
                <span className="flex flex-col gap-1">
                  <span>{item.name}</span>
                  <OptionChips field={item} />
                </span>
              ),
            },
            { key: 'type', header: t('customFields.type'), render: (item) => t(`customFields.types.${item.type}`) },
            { key: 'key', header: t('customFields.key'), render: (item) => <code style={{ fontFamily: font.mono, fontSize: font.size.xs, color: sem('text-secondary') }}>cf.{item.key}</code> },
            { key: 'used', header: t('customFields.used'), render: (item) => t('customFields.usage', { count: item.usageCount ?? 0 }) },
            {
              key: 'actions',
              header: <span className="sr-only">{t('invitations.actions')}</span>,
              render: (item) => (
                <span className="flex gap-3 whitespace-nowrap">
                  <Link component="button" variant="body2" aria-label={`${t('common.edit')} ${item.name}`} onClick={() => setEditing(item)}>{t('common.edit')}</Link>
                  <Link component="button" variant="body2" aria-label={`${t('common.delete')} ${item.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(item)}>
                    {t('common.delete')}
                  </Link>
                </span>
              ),
            },
          ]}
        />
      )}
      {editing && <CustomFieldDialog field={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      <DeleteFieldDialog field={removing} onClose={() => setRemoving(null)} />
    </div>
  );
}
