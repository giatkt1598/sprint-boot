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
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { usePageTitle } from '../../lib/pageTitle';
import { radius, sem } from '../../theme/tokens';
import { SpacePermission, type Space } from '../spaces/api';
import { useTicketTypes } from '../tickets/api';
import {
  ALWAYS_SHOWN,
  BUILT_IN_FIELDS,
  fieldKey,
  useFieldLayout,
  useRefreshFields,
  useSpaceFields,
  type CustomField,
  type SpaceCustomField,
} from './api';
import { CustomFieldDialog } from './CustomFieldDialog';
import { DeleteFieldDialog, OptionChips } from './CustomFieldsAdminPage';

/** A built-in field (shown or hidden, never removed) or a custom one (on or off, for some ticket types). */
type Row =
  | { kind: 'builtIn'; key: string; shown: boolean }
  | { kind: 'custom'; key: string; field: CustomField; enabled: boolean; typeIds: string[] };

/** In the layout's order: the fields in use, then the workspace fields the space has not turned on. */
function toRows(fields: SpaceCustomField[], order: string[], hidden: string[]): Row[] {
  const custom = new Map(fields.map((item) => [fieldKey(item.field!), item]));
  const inOrder = order.flatMap((key): Row[] => {
    if (key in BUILT_IN_FIELDS) {
      return [{ kind: 'builtIn', key, shown: !hidden.includes(key) }];
    }
    const item = custom.get(key);
    return item?.enabled ? [{ kind: 'custom', key, field: item.field!, enabled: true, typeIds: item.ticketTypeIds ?? [] }] : [];
  });
  const off = fields.filter((item) => !item.enabled).map((item): Row => ({ kind: 'custom', key: fieldKey(item.field!), field: item.field!, enabled: false, typeIds: [] }));
  return [...inOrder, ...off];
}

const inUse = (row: Row) => row.kind === 'builtIn' || row.enabled;

/**
 * Space settings → Fields: every field of a ticket's Details sidebar, built-in and custom, in the order the sidebar
 * shows them. Built-in fields can be hidden but not removed; custom fields are on for some ticket types; the space's own
 * custom fields can be edited and deleted here.
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
  const layout = useFieldLayout(space.id);
  const types = useTicketTypes(space.id);
  const [rows, setRows] = useState<Row[]>([]);
  const [dirty, setDirty] = useState(false);
  const [editing, setEditing] = useState<CustomField | 'new' | null>(null);
  const [removing, setRemoving] = useState<CustomField | null>(null);

  useEffect(() => {
    if (fields.data && layout.data && !dirty) {
      setRows(toRows(fields.data, layout.data.order ?? [], layout.data.hidden ?? []));
    }
  }, [fields.data, layout.data, dirty]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/custom-field/set-space-fields/{projectId}', {
        params: { path: { projectId: space.id! } },
        body: {
          items: rows.flatMap((row) => (row.kind === 'custom' && row.enabled ? [{ fieldId: row.field.id!, ticketTypeIds: row.typeIds }] : [])),
          order: rows.filter(inUse).map((row) => row.key),
          hidden: rows.flatMap((row) => (row.kind === 'builtIn' && !row.shown ? [row.key] : [])),
        },
      })),
    onSuccess: async () => {
      setDirty(false);
      await refresh();
      toast('success', t('customFields.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const change = (index: number, next: Row) => {
    const list = rows.map((row, at) => (at === index ? next : row));
    // The fields in use come first, in their order; turning one on puts it last among them.
    setRows([...list.filter(inUse), ...list.filter((row) => !inUse(row))]);
    setDirty(true);
  };
  const move = (index: number, step: number) => {
    const next = [...rows];
    [next[index], next[index + step]] = [next[index + step], next[index]];
    setRows(next);
    setDirty(true);
  };
  const usedCount = rows.filter(inUse).length;
  const standardTypes = (types.data ?? []).filter((type) => type.hierarchy !== 0).map((type) => type.id!);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('customFields.spaceTitle')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('customFields.spaceSubtitle')}</Typography>
        </div>
        {canManage && <Button kind="secondary" onClick={() => setEditing('new')}>{t('customFields.add')}</Button>}
      </div>
      {(fields.isPending || layout.isPending || types.isPending) && <LoadingState />}
      {(fields.isError || layout.isError) && <ErrorState onRetry={() => { void fields.refetch(); void layout.refetch(); }} />}
      {rows.length > 0 && types.data && (
        <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label={t('customFields.spaceTitle')}>
          {rows.map((row, index) => {
            const name = row.kind === 'builtIn' ? t(BUILT_IN_FIELDS[row.key]) : row.field.name ?? '';
            const own = row.kind === 'custom' && row.field.projectId === space.id;
            const muted = row.kind === 'builtIn' ? !row.shown : !row.enabled;
            return (
              <Box
                component="li"
                key={row.key}
                className="flex flex-col gap-2 p-3"
                sx={{ border: `1px solid ${sem('border-default')}`, borderRadius: `${radius.card}px`, opacity: muted ? 0.75 : 1 }}
              >
                <div className="flex flex-wrap items-center gap-2">
                  {row.kind === 'custom' && !own && canManage ? (
                    <Checkbox
                      label={name}
                      checked={row.enabled}
                      onChange={(_, checked) => change(index, { ...row, enabled: checked, typeIds: checked && row.typeIds.length === 0 ? standardTypes : row.typeIds })}
                    />
                  ) : (
                    <Typography sx={{ fontWeight: 500 }}>{name}</Typography>
                  )}
                  {row.kind === 'custom' && <Badge tone="subtle">{t(`customFields.types.${row.field.type}`)}</Badge>}
                  <Badge tone={row.kind === 'builtIn' ? 'subtle' : own ? 'info' : 'neutral'}>
                    {t(row.kind === 'builtIn' ? 'customFields.builtIn' : own ? 'customFields.thisSpace' : 'customFields.workspace')}
                  </Badge>
                  <span className="flex-1" />
                  {/* Built-in fields: one line, shown or hidden; the status always shows. */}
                  {row.kind === 'builtIn' && (ALWAYS_SHOWN.includes(row.key) ? (
                    <Typography variant="body2" color="textSecondary" title={t('customFields.alwaysShown')}>{t('customFields.alwaysShownShort')}</Typography>
                  ) : (
                    <Checkbox
                      label={t('customFields.showInDetails')}
                      disabled={!canManage}
                      checked={row.shown}
                      onChange={(_, checked) => change(index, { ...row, shown: checked })}
                    />
                  ))}
                  {canManage && own && (
                    <span className="flex gap-3 whitespace-nowrap">
                      <Link component="button" variant="body2" aria-label={`${t('common.edit')} ${name}`} onClick={() => setEditing(row.field)}>{t('common.edit')}</Link>
                      <Link component="button" variant="body2" aria-label={`${t('common.delete')} ${name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(row.field)}>
                        {t('common.delete')}
                      </Link>
                    </span>
                  )}
                  {canManage && inUse(row) && (
                    <span className="flex">
                      <IconButton size="small" aria-label={t('customFields.moveUp', { name })} disabled={index === 0} onClick={() => move(index, -1)}>
                        <ArrowUpIcon size={16} strokeWidth={1.5} aria-hidden />
                      </IconButton>
                      <IconButton size="small" aria-label={t('customFields.moveDown', { name })} disabled={index >= usedCount - 1} onClick={() => move(index, 1)}>
                        <ArrowDownIcon size={16} strokeWidth={1.5} aria-hidden />
                      </IconButton>
                    </span>
                  )}
                </div>
                {row.kind === 'custom' && (
                  <>
                    {row.field.description && <Typography variant="body2" color="textSecondary">{row.field.description}</Typography>}
                    <OptionChips field={row.field} />
                    {row.enabled ? (
                      <fieldset className="flex flex-wrap items-center gap-x-4" style={{ border: 'none', margin: 0, padding: 0 }}>
                        <legend className="sr-only">{`${t('customFields.ticketTypes')}: ${name}`}</legend>
                        <Typography variant="body2" color="textSecondary" aria-hidden>{t('customFields.ticketTypes')}</Typography>
                        {(types.data ?? []).map((type) => (
                          <span key={type.id} className="flex items-center gap-1">
                            <TicketTypeIcon type={type.icon as IconType} />
                            <Checkbox
                              label={type.name ?? ''}
                              disabled={!canManage}
                              checked={row.typeIds.includes(type.id!)}
                              onChange={(_, checked) => change(index, { ...row, typeIds: checked ? [...row.typeIds, type.id!] : row.typeIds.filter((id) => id !== type.id) })}
                            />
                          </span>
                        ))}
                      </fieldset>
                    ) : (
                      <Typography variant="body2" color="textSecondary">{t('customFields.off')}</Typography>
                    )}
                  </>
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
