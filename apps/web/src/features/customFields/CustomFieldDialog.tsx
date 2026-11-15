import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { AddIcon, ArrowDownIcon, ArrowUpIcon, DeleteIcon } from '../../components/icons';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, sem } from '../../theme/tokens';
import { FIELD_TYPES, FieldType, OPTION_COLORS, hasOptions, useRefreshFields, type CustomField } from './api';

interface OptionDraft {
  /** A saved option keeps its id, so tickets that have it keep it. */
  id?: string;
  /** For React keys before an option is saved. */
  draftId: string;
  label: string;
  color: string;
  isHidden: boolean;
}

let nextDraft = 0;
const newOption = (label = ''): OptionDraft => ({ draftId: `new-${++nextDraft}`, label, color: 'gray', isHidden: false });

/** The default as the form edits it; the API takes it as JSON. */
function readDefault(field: CustomField | undefined): string {
  if (!field?.defaultValue) {
    return '';
  }
  try {
    const value: unknown = JSON.parse(field.defaultValue);
    return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' ? String(value) : '';
  } catch {
    return '';
  }
}

function writeDefault(type: number, text: string): string | null {
  if (!text) {
    return null;
  }
  if (type === FieldType.number) {
    return Number.isFinite(Number(text)) ? JSON.stringify(Number(text)) : null;
  }
  if (type === FieldType.checkbox) {
    return text === 'true' ? 'true' : null;
  }
  return JSON.stringify(text);
}

/** The kinds that take a default: the rest start empty. */
const takesDefault = (type: number) => type === FieldType.text || type === FieldType.number || type === FieldType.select || type === FieldType.checkbox;

/** Adds a field for the workspace (no space) or for one space, or edits one; the kind and the key never change. */
export function CustomFieldDialog({ field, projectId, onClose }: { field?: CustomField; projectId?: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefreshFields();
  const [name, setName] = useState(field?.name ?? '');
  const [description, setDescription] = useState(field?.description ?? '');
  const [type, setType] = useState<number>(field?.type ?? FieldType.text);
  const [options, setOptions] = useState<OptionDraft[]>(
    field?.options?.length
      ? field.options.map((option) => ({ id: option.id, draftId: option.id!, label: option.label ?? '', color: option.color ?? 'gray', isHidden: option.isHidden ?? false }))
      : [newOption(), newOption()],
  );
  // A select default is stored by option id; the form picks it by label.
  const [defaultText, setDefaultText] = useState(() => {
    const stored = readDefault(field);
    return field?.options?.find((option) => option.id === stored)?.label ?? stored;
  });

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name,
        description: description || null,
        options: hasOptions(type) ? options.filter((option) => option.label.trim()).map(({ id, label, color, isHidden }) => ({ id, label, color, isHidden })) : null,
        // A default option is picked by its label while it may not have an id yet; the API takes labels too.
        defaultValue: takesDefault(type) ? writeDefault(type, defaultText) : null,
      };
      return field
        ? unwrap(apiClient.PUT('/api/v1/custom-field/{id}', { params: { path: { id: field.id! } }, body }))
        : unwrap(apiClient.POST('/api/v1/custom-field', { body: { ...body, projectId: projectId ?? null, type: type as CustomField['type'] } }));
    },
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const move = (index: number, step: number) => {
    const next = [...options];
    [next[index], next[index + step]] = [next[index + step], next[index]];
    setOptions(next);
  };
  const change = (index: number, patch: Partial<OptionDraft>) => setOptions(options.map((option, at) => (at === index ? { ...option, ...patch } : option)));
  const labels = options.map((option) => option.label.trim()).filter(Boolean);
  const ready = Boolean(name.trim()) && (!hasOptions(type) || labels.length > 0) && !save.isPending;

  return (
    <Dialog
      open
      width={560}
      title={field ? t('customFields.editTitle', { name: field.name }) : t('customFields.createTitle')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('common.save')} confirmDisabled={!ready} onCancel={onClose} onConfirm={() => save.mutate()} />}
    >
      <TextField label={t('customFields.name')} value={name} autoFocus onChange={(event) => setName(event.target.value)} />
      <TextField label={t('customFields.description')} hint={t('customFields.descriptionHint')} value={description} multiline minRows={2} onChange={(event) => setDescription(event.target.value)} />
      <SelectField
        label={t('customFields.type')}
        value={String(type)}
        disabled={Boolean(field)}
        onChange={(event) => setType(Number(event.target.value))}
        options={FIELD_TYPES.map((value) => ({ value: String(value), label: t(`customFields.types.${value}`) }))}
      />
      {field && (
        <Typography variant="body2" color="textSecondary">
          {t('customFields.typeLocked')} {t('customFields.keyHint', { key: field.key })}
        </Typography>
      )}

      {hasOptions(type) && (
        <fieldset className="flex flex-col gap-2" style={{ border: 'none', margin: 0, padding: 0 }}>
          <legend style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary'), paddingBottom: 4 }}>{t('customFields.options')}</legend>
          {options.map((option, index) => {
            const number = index + 1;
            return (
              <div key={option.draftId} className="flex flex-wrap items-center gap-2">
                <div className="min-w-0 flex-1" style={{ minWidth: 160 }}>
                  <TextField quiet label={t('customFields.optionLabel', { number })} placeholder={t('customFields.optionLabel', { number })} value={option.label} onChange={(event) => change(index, { label: event.target.value })} />
                </div>
                <div style={{ width: 128 }}>
                  <SelectField
                    quiet
                    label={t('customFields.optionColor', { number })}
                    value={option.color}
                    onChange={(event) => change(index, { color: event.target.value })}
                    options={OPTION_COLORS.map((color) => ({ value: color, label: t(`customFields.colors.${color}`) }))}
                  />
                </div>
                {option.id && (
                  <Checkbox label={t('customFields.hidden')} checked={option.isHidden} onChange={(_, checked) => change(index, { isHidden: checked })} slotProps={{ input: { 'aria-label': t('customFields.hideOption', { number }) } }} />
                )}
                <IconButton size="small" aria-label={t('customFields.moveUp', { name: option.label || number })} disabled={index === 0} onClick={() => move(index, -1)}>
                  <ArrowUpIcon size={16} strokeWidth={1.5} aria-hidden />
                </IconButton>
                <IconButton size="small" aria-label={t('customFields.moveDown', { name: option.label || number })} disabled={index === options.length - 1} onClick={() => move(index, 1)}>
                  <ArrowDownIcon size={16} strokeWidth={1.5} aria-hidden />
                </IconButton>
                <IconButton size="small" aria-label={t('customFields.removeOption', { number })} onClick={() => setOptions(options.filter((_, at) => at !== index))}>
                  <DeleteIcon size={16} strokeWidth={1.5} aria-hidden />
                </IconButton>
              </div>
            );
          })}
          <div>
            <Button kind="link" startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setOptions([...options, newOption()])}>{t('customFields.addOption')}</Button>
          </div>
        </fieldset>
      )}

      {takesDefault(type) && (type === FieldType.checkbox ? (
        <Checkbox label={t('customFields.defaultValue')} checked={defaultText === 'true'} onChange={(_, checked) => setDefaultText(checked ? 'true' : '')} />
      ) : type === FieldType.select ? (
        <SelectField
          label={t('customFields.defaultValue')}
          value={labels.includes(defaultText) ? defaultText : ''}
          displayEmpty
          onChange={(event) => setDefaultText(event.target.value)}
          options={[{ value: '', label: t('customFields.noDefault') }, ...labels.map((label) => ({ value: label, label }))]}
        />
      ) : (
        <TextField
          label={t('customFields.defaultValue')}
          value={defaultText}
          inputMode={type === FieldType.number ? 'decimal' : undefined}
          onChange={(event) => setDefaultText(event.target.value)}
        />
      ))}
    </Dialog>
  );
}
