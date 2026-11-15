import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useOutletContext } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { ArrowDownIcon, ArrowUpIcon } from '../../components/icons';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { usePageTitle } from '../../lib/pageTitle';
import { radius, sem } from '../../theme/tokens';
import { SpacePermission, type Space } from '../spaces/api';
import { useTicketTypes } from '../tickets/api';
import { useRefreshFields, useSpaceFields, type CustomField, type SpaceCustomField } from './api';
import { CustomFieldDialog } from './CustomFieldDialog';
import { DeleteFieldDialog, OptionChips } from './CustomFieldsAdminPage';

interface Row {
  field: CustomField;
  enabled: boolean;
  typeIds: string[];
}

const toRows = (list: SpaceCustomField[]): Row[] => list.map((item) => ({ field: item.field!, enabled: item.enabled ?? false, typeIds: item.ticketTypeIds ?? [] }));

/**
 * Space settings → Fields: which custom fields the space uses, the ticket types that show each, and their order. The
 * space's own fields are always on; workspace fields can be turned on and off.
 */
export function SpaceFieldsPage() {
  const { t } = useTranslation();
  usePageTitle(t('customFields.spaceTitle'));
  const space = useOutletContext<Space>();
  const canManage = space.permissions?.includes(SpacePermission.manage) ?? false;
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefreshFields();
  const fields = useSpaceFields(space.id);
  const types = useTicketTypes(space.id);
  const [rows, setRows] = useState<Row[]>([]);
  const [dirty, setDirty] = useState(false);
  const [editing, setEditing] = useState<CustomField | 'new' | null>(null);
  const [removing, setRemoving] = useState<CustomField | null>(null);

  useEffect(() => {
    if (fields.data && !dirty) {
      setRows(toRows(fields.data));
    }
  }, [fields.data, dirty]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/custom-field/set-space-fields/{projectId}', {
        params: { path: { projectId: space.id! } },
        body: { items: rows.filter((row) => row.enabled).map((row) => ({ fieldId: row.field.id!, ticketTypeIds: row.typeIds })) },
      })),
    onSuccess: async () => {
      setDirty(false);
      await refresh();
      toast('success', t('customFields.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const change = (index: number, patch: Partial<Row>) => {
    const next = rows.map((row, at) => (at === index ? { ...row, ...patch } : row));
    // The fields in use come first, in their order; turning one on puts it last among them.
    setRows([...next.filter((row) => row.enabled), ...next.filter((row) => !row.enabled)]);
    setDirty(true);
  };
  const move = (index: number, step: number) => {
    const next = [...rows];
    [next[index], next[index + step]] = [next[index + step], next[index]];
    setRows(next);
    setDirty(true);
  };
  const enabledCount = rows.filter((row) => row.enabled).length;

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('customFields.spaceTitle')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('customFields.spaceSubtitle')}</Typography>
        </div>
        {canManage && <Button kind="secondary" onClick={() => setEditing('new')}>{t('customFields.add')}</Button>}
      </div>
      {(fields.isPending || types.isPending) && <LoadingState />}
      {fields.isError && <ErrorState onRetry={() => void fields.refetch()} />}
      {fields.data?.length === 0 && <EmptyState title={t('customFields.spaceEmpty')} />}
      {rows.length > 0 && types.data && (
        <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label={t('customFields.spaceTitle')}>
          {rows.map((row, index) => {
            const own = row.field.projectId === space.id;
            return (
              <Box
                component="li"
                key={row.field.id}
                className="flex flex-col gap-2 p-3"
                sx={{ border: `1px solid ${sem('border-default')}`, borderRadius: `${radius.card}px`, opacity: row.enabled ? 1 : 0.75 }}
              >
                <div className="flex flex-wrap items-center gap-2">
                  {own || !canManage ? (
                    <Typography sx={{ fontWeight: 500 }}>{row.field.name}</Typography>
                  ) : (
                    <Checkbox
                      label={row.field.name ?? ''}
                      checked={row.enabled}
                      onChange={(_, checked) => change(index, { enabled: checked, typeIds: checked && row.typeIds.length === 0 ? (types.data ?? []).filter((type) => type.hierarchy !== 0).map((type) => type.id!) : row.typeIds })}
                    />
                  )}
                  <Badge tone="subtle">{t(`customFields.types.${row.field.type}`)}</Badge>
                  <Badge tone={own ? 'info' : 'neutral'}>{t(own ? 'customFields.thisSpace' : 'customFields.workspace')}</Badge>
                  <span className="flex-1" />
                  {canManage && own && (
                    <span className="flex gap-3 whitespace-nowrap">
                      <Link component="button" variant="body2" aria-label={`${t('common.edit')} ${row.field.name}`} onClick={() => setEditing(row.field)}>{t('common.edit')}</Link>
                      <Link component="button" variant="body2" aria-label={`${t('common.delete')} ${row.field.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(row.field)}>
                        {t('common.delete')}
                      </Link>
                    </span>
                  )}
                  {canManage && row.enabled && (
                    <span className="flex">
                      <IconButton size="small" aria-label={t('customFields.moveUp', { name: row.field.name })} disabled={index === 0} onClick={() => move(index, -1)}>
                        <ArrowUpIcon size={16} strokeWidth={1.5} aria-hidden />
                      </IconButton>
                      <IconButton size="small" aria-label={t('customFields.moveDown', { name: row.field.name })} disabled={index >= enabledCount - 1} onClick={() => move(index, 1)}>
                        <ArrowDownIcon size={16} strokeWidth={1.5} aria-hidden />
                      </IconButton>
                    </span>
                  )}
                </div>
                {row.field.description && <Typography variant="body2" color="textSecondary">{row.field.description}</Typography>}
                <OptionChips field={row.field} />
                {row.enabled ? (
                  <fieldset className="flex flex-wrap items-center gap-x-4" style={{ border: 'none', margin: 0, padding: 0 }}>
                    <legend className="sr-only">{`${t('customFields.ticketTypes')}: ${row.field.name}`}</legend>
                    <Typography variant="body2" color="textSecondary" aria-hidden>{t('customFields.ticketTypes')}</Typography>
                    {(types.data ?? []).map((type) => (
                      <span key={type.id} className="flex items-center gap-1">
                        <TicketTypeIcon type={type.icon as IconType} />
                        <Checkbox
                          label={type.name ?? ''}
                          disabled={!canManage}
                          checked={row.typeIds.includes(type.id!)}
                          onChange={(_, checked) => change(index, { typeIds: checked ? [...row.typeIds, type.id!] : row.typeIds.filter((id) => id !== type.id) })}
                        />
                      </span>
                    ))}
                  </fieldset>
                ) : (
                  <Typography variant="body2" color="textSecondary">{t('customFields.off')}</Typography>
                )}
              </Box>
            );
          })}
        </ul>
      )}
      {canManage && rows.length > 0 && (
        <div className="flex justify-end">
          <Button disabled={!dirty || save.isPending} onClick={() => save.mutate()}>{t('customFields.saveOrder')}</Button>
        </div>
      )}
      {editing && <CustomFieldDialog field={editing === 'new' ? undefined : editing} projectId={space.id} onClose={() => { setEditing(null); setDirty(false); }} />}
      <DeleteFieldDialog field={removing} onClose={() => { setRemoving(null); setDirty(false); }} />
    </div>
  );
}
