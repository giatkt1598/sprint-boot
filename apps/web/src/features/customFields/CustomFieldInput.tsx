import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import MuiCheckbox from '@mui/material/Checkbox';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Avatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { Checkbox } from '../../components/Checkbox';
import { DatePicker } from '../../components/DatePicker';
import { CheckIcon, LinkIcon } from '../../components/icons';
import { Popover } from '../../components/Popover';
import { SelectField, TextField } from '../../components/TextField';
import { UserPicker, type PickerUser } from '../../components/UserPicker';
import { formatDate } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { DETAIL_INSET, DetailText } from '../tickets/DetailLayout';
import { TagInput } from '../../components/TagInput';
import { FieldType, optionTone, useFieldTags, type CustomField, type CustomFieldOption, type TicketCustomField } from './api';

/** A value as the ticket API takes it. */
export type FieldValue = string | number | boolean | string[] | null;

/** The value of a ticket's field, as the API takes it back. */
export function valueOf(item: TicketCustomField): FieldValue {
  switch (item.field?.type) {
    case FieldType.number:
      return item.number ?? null;
    case FieldType.date:
      return item.date ?? null;
    case FieldType.checkbox:
      return item.checked ?? null;
    case FieldType.user:
      return item.user?.id ?? null;
    case FieldType.select:
      return item.options?.[0]?.id ?? null;
    case FieldType.multiSelect:
      return (item.options ?? []).map((option) => option.id!);
    case FieldType.tags:
      return item.tags ?? [];
    default:
      return item.text ?? null;
  }
}

/** Whether a value says something: not empty, not an empty list. */
export const isSet = (value: FieldValue | undefined) => value !== null && value !== undefined && value !== '' && !(Array.isArray(value) && value.length === 0);

/** What a new ticket starts with in the field, as the form shows it. */
export function defaultOf(field: CustomField): FieldValue {
  if (!field.defaultValue) {
    return field.type === FieldType.multiSelect || field.type === FieldType.tags ? [] : null;
  }
  try {
    return JSON.parse(field.defaultValue) as FieldValue;
  } catch {
    return null;
  }
}

/** The options people may pick: the visible ones, and a hidden one the ticket already has. */
const offered = (field: CustomField, chosen: string[]) => (field.options ?? []).filter((option) => !option.isHidden || chosen.includes(option.id!));

function OptionBadges({ options }: { options: CustomFieldOption[] }) {
  return (
    <span className="flex flex-wrap gap-1">
      {options.map((option) => <Badge key={option.id} tone={optionTone(option.color)}>{option.label}</Badge>)}
    </span>
  );
}

const NUMBER_PATTERN = /^-?\d*\.?\d*$/;

/** Holds the typed text and saves it on blur or Enter, so every keystroke does not save. */
function TextInput({ field, value, quiet, multiline, numeric, onChange }: {
  field: CustomField;
  value: FieldValue;
  quiet: boolean;
  multiline?: boolean;
  numeric?: boolean;
  onChange: (value: FieldValue) => void;
}) {
  const shown = value === null ? '' : String(value);
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    if (draft === null || draft === shown) {
      setDraft(null);
      return;
    }
    const trimmed = draft.trim();
    onChange(trimmed === '' ? null : numeric ? Number(trimmed) : trimmed);
    setDraft(null);
  };

  return (
    <TextField
      quiet={quiet}
      label={field.name ?? ''}
      hint={quiet ? undefined : field.description ?? undefined}
      multiline={multiline}
      // A quiet paragraph starts its text where the other quiet fields do.
      sx={multiline && quiet ? { '&.MuiInputBase-multiline': { paddingInline: 0 } } : undefined}
      minRows={multiline ? 2 : undefined}
      inputMode={numeric ? 'decimal' : undefined}
      type={field.type === FieldType.url ? 'url' : undefined}
      value={draft ?? shown}
      onChange={(event) => (!numeric || NUMBER_PATTERN.test(event.target.value)) && setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter' && !multiline) {
          (event.target as HTMLElement).blur();
        }
        if (event.key === 'Escape') {
          setDraft(null);
        }
      }}
    />
  );
}

/** Chips that open a list of checkboxes; the choice saves when the list closes. */
function MultiSelectInput({ field, value, quiet, onChange }: { field: CustomField; value: string[]; quiet: boolean; onChange: (value: FieldValue) => void }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [draft, setDraft] = useState<string[] | null>(null);
  const current = draft ?? value;
  const options = offered(field, value);
  const chosen = options.filter((option) => current.includes(option.id!));
  const close = () => {
    setAnchor(null);
    if (draft && (draft.length !== value.length || draft.some((id) => !value.includes(id)))) {
      // Kept in the field's order.
      onChange(options.filter((option) => draft.includes(option.id!)).map((option) => option.id!));
    }
    setDraft(null);
  };

  return (
    <div className="flex flex-col gap-1">
      {!quiet && <span aria-hidden style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{field.name}</span>}
      <ButtonBase
        aria-haspopup="dialog"
        aria-expanded={Boolean(anchor)}
        onClick={(event) => setAnchor(event.currentTarget)}
        className="w-full"
        sx={{
          justifyContent: 'flex-start',
          minHeight: quiet ? 32 : 36,
          paddingInline: `${DETAIL_INSET}px`,
          fontSize: 'inherit',
          borderRadius: `${radius.control}px`,
          border: quiet ? 'none' : `1px solid ${sem('border-strong')}`,
          '&:hover': { backgroundColor: sem('bg-hover') },
        }}
      >
        <span className="sr-only">{field.name}:</span>
        {chosen.length > 0 ? <OptionBadges options={chosen} /> : <span style={{ color: sem('text-tertiary') }}>{t('common.none')}</span>}
      </ButtonBase>
      <Popover width={264} open={Boolean(anchor)} anchorEl={anchor} onClose={close}>
        <div role="group" aria-label={field.name ?? undefined} className="flex flex-col">
          {options.map((option) => (
            <Checkbox
              key={option.id}
              label={option.label ?? ''}
              checked={current.includes(option.id!)}
              onChange={(_, checked) => setDraft(checked ? [...current, option.id!] : current.filter((id) => id !== option.id))}
            />
          ))}
        </div>
      </Popover>
    </div>
  );
}

/** Words shown like labels: outlined, small. */
function TagList({ tags }: { tags: string[] }) {
  return (
    <span className="flex flex-wrap gap-1">
      {tags.map((tag) => (
        <span key={tag} className="px-1" style={{ fontSize: font.size.xs, border: `1px solid ${sem('text-tertiary')}`, borderRadius: 4 }}>{tag}</span>
      ))}
    </span>
  );
}

/**
 * Free words typed like labels, with the field's words in the space offered. In the sidebar the words show until
 * clicked, and save when the field is left; in a form they save as they are typed.
 */
function TagsInput({ field, value, quiet, projectId, onChange }: { field: CustomField; value: string[]; quiet: boolean; projectId?: string; onChange: (value: FieldValue) => void }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<string[] | null>(null);
  const known = useFieldTags(field.id, projectId, !quiet || draft !== null);

  if (!quiet) {
    return <TagInput label={field.name ?? ''} value={value} options={known.data ?? []} onChange={onChange} />;
  }

  if (draft === null) {
    return (
      <ButtonBase
        onClick={() => setDraft(value)}
        className="w-full"
        sx={{ justifyContent: 'flex-start', minHeight: 32, paddingInline: `${DETAIL_INSET}px`, fontSize: 'inherit', borderRadius: `${radius.control}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
      >
        <span className="sr-only">{field.name}:</span>
        {value.length > 0 ? <TagList tags={value} /> : <span style={{ color: sem('text-tertiary') }}>{t('common.none')}</span>}
      </ButtonBase>
    );
  }

  return (
    <div
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          if (draft.join('\n') !== value.join('\n')) {
            onChange(draft);
          }
          setDraft(null);
        }
      }}
    >
      <TagInput hideLabel autoFocus label={field.name ?? ''} value={draft} options={known.data ?? []} onChange={setDraft} />
    </div>
  );
}

/**
 * Shows or edits one custom field. Quiet in the Details sidebar, framed with its label in a form. Disabled shows the
 * value as text. Typed text saves on blur; pickers save on pick.
 */
export function CustomFieldInput({ field: given, value, quiet = false, disabled = false, people = [], projectId, onChange }: {
  field: CustomField;
  value: FieldValue;
  quiet?: boolean;
  disabled?: boolean;
  /** Who a person field can name: the space's members. */
  people?: PickerUser[];
  /** The space, for the words a tags field offers. */
  projectId?: string;
  onChange: (value: FieldValue) => void;
}) {
  const { t, i18n } = useTranslation();
  // A form marks a required field by its label; a required select offers no "none".
  const field = !quiet && given.isRequired ? { ...given, name: t('customFields.requiredLabel', { name: given.name }) } : given;
  const none = <span style={{ color: sem('text-tertiary') }}>{t('common.none')}</span>;
  const chosenIds = Array.isArray(value) ? value : typeof value === 'string' ? [value] : [];

  if (disabled) {
    switch (field.type) {
      case FieldType.select:
      case FieldType.multiSelect: {
        const chosen = (field.options ?? []).filter((option) => chosenIds.includes(option.id!));
        return <DetailText>{chosen.length > 0 ? <OptionBadges options={chosen} /> : none}</DetailText>;
      }
      case FieldType.user: {
        const person = people.find((user) => user.id === value);
        return <DetailText>{person ? <><Avatar name={person.name ?? ''} src={person.avatarUrl} />{person.name}</> : none}</DetailText>;
      }
      case FieldType.checkbox:
        return <DetailText>{value === true ? t('customFields.yes') : none}</DetailText>;
      case FieldType.tags:
        return <DetailText>{chosenIds.length > 0 ? <TagList tags={chosenIds} /> : none}</DetailText>;
      case FieldType.date:
        return <DetailText>{typeof value === 'string' ? formatDate(value, i18n.language) : none}</DetailText>;
      case FieldType.url:
        return <DetailText>{typeof value === 'string' ? <Link href={value} target="_blank" rel="noopener noreferrer" className="truncate">{value}</Link> : none}</DetailText>;
      default:
        return <DetailText><span className="min-w-0" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value === null || value === '' ? none : String(value)}</span></DetailText>;
    }
  }

  switch (field.type) {
    case FieldType.number:
      return <TextInput field={field} value={value} quiet={quiet} numeric onChange={onChange} />;
    case FieldType.paragraph:
      return <TextInput field={field} value={value} quiet={quiet} multiline onChange={onChange} />;
    case FieldType.url:
      return (
        <span className="flex items-center gap-1">
          <span className="min-w-0 flex-1"><TextInput field={field} value={value} quiet={quiet} onChange={onChange} /></span>
          {typeof value === 'string' && value && (
            <IconButton size="small" component="a" href={value} target="_blank" rel="noopener noreferrer" aria-label={`${t('customFields.openLink')}: ${field.name}`}>
              <LinkIcon size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          )}
        </span>
      );
    case FieldType.date:
      return <DatePicker quiet={quiet} label={field.name ?? ''} value={typeof value === 'string' ? value : ''} onChange={(text) => onChange(text || null)} />;
    case FieldType.checkbox:
      if (!quiet) {
        return <Checkbox label={field.name ?? ''} checked={value === true} onChange={(_, checked) => onChange(checked ? true : null)} />;
      }
      return (
        <span className="flex" style={{ paddingLeft: DETAIL_INSET - 9 }}>
          <MuiCheckbox
            checked={value === true}
            onChange={(_, checked) => onChange(checked ? true : null)}
            slotProps={{ input: { 'aria-label': field.name ?? undefined } }}
            icon={<span style={{ width: 16, height: 16, borderRadius: 3, border: `1px solid ${sem('border-strong')}` }} />}
            checkedIcon={<span style={{ width: 16, height: 16, borderRadius: 3, backgroundColor: sem('accent-default'), color: sem('text-on-accent'), display: 'flex', alignItems: 'center', justifyContent: 'center' }}><CheckIcon size={12} strokeWidth={2.5} aria-hidden /></span>}
          />
        </span>
      );
    case FieldType.user:
      return (
        <UserPicker
          hideLabel={quiet}
          label={field.name ?? ''}
          options={people}
          value={people.find((user) => user.id === value) ?? null}
          emptyLabel={t('common.none')}
          onChange={(user) => onChange(user?.id ?? null)}
        />
      );
    case FieldType.select:
      return (
        <SelectField
          quiet={quiet}
          label={field.name ?? ''}
          value={typeof value === 'string' ? value : ''}
          displayEmpty
          onChange={(event) => onChange(event.target.value || null)}
          options={[
            ...(field.isRequired && typeof value === 'string' && value ? [] : [{ value: '', label: t('common.none') }]),
            ...offered(field, chosenIds).map((option) => ({ value: option.id!, label: option.label ?? '' })),
          ]}
        />
      );
    case FieldType.multiSelect:
      return <MultiSelectInput field={field} value={chosenIds} quiet={quiet} onChange={onChange} />;
    case FieldType.tags:
      return <TagsInput field={field} value={chosenIds} quiet={quiet} projectId={projectId} onChange={onChange} />;
    default:
      return <TextInput field={field} value={value} quiet={quiet} onChange={onChange} />;
  }
}
