import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { X } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Checkbox } from '../../components/Checkbox';
import { PickerPopover } from '../../components/PickerPopover';
import { SelectField, TextField } from '../../components/TextField';
import { font, radius, sem } from '../../theme/tokens';

/** Kinds as the server numbers them. */
const CONDITION = 0;
const VALIDATOR = 1;
const POST_FUNCTION = 2;

export interface DraftRule {
  kind: number;
  key: string;
  config?: Record<string, unknown> | null;
}

interface NestedRule {
  key: string;
  config?: Record<string, unknown> | null;
}

const ANY_OF = 'any-of';
const KEYS: Record<number, string[]> = {
  [CONDITION]: ['role', 'user', 'permission', 'subtasks-done'],
  [VALIDATOR]: ['required-field', 'comment-required', 'linked-pull-request'],
  [POST_FUNCTION]: ['set-field', 'assign', 'set-resolution', 'add-comment', 'notify', 'webhook'],
};
const ROLES = ['Admin', 'Member', 'Viewer'];
const PEOPLE = ['assignee', 'reporter'];
const PERMISSIONS = ['Project.Manage', 'Project.Members.Manage', 'Issue.Edit', 'Issue.Transition', 'Sprint.Manage'];
const REQUIRED_FIELDS = ['assignee', 'reporter', 'description', 'labels', 'storyPoints', 'startDate', 'dueDate', 'parent'];
const SETTABLE_FIELDS = ['assignee', 'priority', 'labels', 'storyPoints', 'dueDate'];
const ASSIGN_TO = ['reporter', 'actor'];

function RuleConfig({ rule, onChange }: { rule: NestedRule; onChange: (config: Record<string, unknown>) => void }) {
  const { t } = useTranslation();
  const config = rule.config ?? {};
  const list = (name: string) => (Array.isArray(config[name]) ? (config[name] as string[]) : []);
  const text = (name: string) => (typeof config[name] === 'string' ? (config[name] as string) : '');
  const set = (name: string, value: unknown) => onChange({ ...config, [name]: value });
  const toggles = (name: string, values: string[], label: (value: string) => string) => (
    <div className="flex flex-wrap gap-x-3">
      {values.map((value) => (
        <Checkbox
          key={value}
          label={label(value)}
          checked={list(name).includes(value)}
          onChange={(event) => set(name, event.target.checked ? [...list(name), value] : list(name).filter((item) => item !== value))}
        />
      ))}
    </div>
  );
  const field = (name: string) => t(`designer.field.${name}`);

  switch (rule.key) {
    case 'role':
      return toggles('roles', ROLES, (value) => t(`invitations.roles.${value}`));
    case 'user':
      return toggles('is', PEOPLE, field);
    case 'permission':
      return (
        <SelectField
          quiet
          label={t('designer.rule.permission')}
          value={text('permission')}
          displayEmpty
          onChange={(event) => set('permission', event.target.value)}
          options={[{ value: '', label: t('designer.choose') }, ...PERMISSIONS.map((value) => ({ value, label: value }))]}
        />
      );
    case 'required-field':
      return toggles('fields', REQUIRED_FIELDS, field);
    case 'set-field':
      return (
        <>
          <SelectField
            quiet
            label={t('designer.fieldName')}
            value={text('field')}
            displayEmpty
            onChange={(event) => set('field', event.target.value)}
            options={[{ value: '', label: t('designer.choose') }, ...SETTABLE_FIELDS.map((value) => ({ value, label: field(value) }))]}
          />
          <TextField quiet label={t('designer.fieldValue')} placeholder={t('designer.fieldValueHint')} value={text('value')} onChange={(event) => set('value', event.target.value || null)} />
        </>
      );
    case 'assign':
      return (
        <SelectField
          quiet
          label={t('designer.assignTo')}
          value={text('to')}
          displayEmpty
          onChange={(event) => set('to', event.target.value)}
          options={[{ value: '', label: t('designer.choose') }, ...ASSIGN_TO.map((value) => ({ value, label: t(`designer.assign.${value}`) }))]}
        />
      );
    case 'set-resolution':
      return <TextField quiet label={t('designer.resolution')} value={text('value')} onChange={(event) => set('value', event.target.value)} />;
    case 'add-comment':
      return <TextField quiet label={t('designer.commentBody')} value={text('body')} onChange={(event) => set('body', event.target.value)} />;
    case 'notify':
      return <TextField quiet label={t('designer.message')} value={text('message')} onChange={(event) => set('message', event.target.value)} />;
    case 'webhook':
      return <TextField quiet label={t('designer.webhookUrl')} placeholder="https://" value={text('url')} onChange={(event) => set('url', event.target.value)} />;
    default:
      return null;
  }
}

const NO_SETTINGS = ['subtasks-done', 'comment-required', 'linked-pull-request'];

/** One line that says what the rule does, which is what the chip shows until it is opened. */
function useRuleSummary() {
  const { t } = useTranslation();
  return (rule: NestedRule) => {
    const config = rule.config ?? {};
    const list = (name: string) => (Array.isArray(config[name]) ? (config[name] as string[]) : []);
    const fields = (name: string) => list(name).map((value) => t(`designer.field.${value}`)).join(', ');
    const value = (name: string) => (typeof config[name] === 'string' ? (config[name] as string) : '');
    const details: Record<string, string> = {
      role: list('roles').map((role) => t(`invitations.roles.${role}`)).join(', '),
      user: fields('is'),
      permission: value('permission'),
      'required-field': fields('fields'),
      'set-field': value('field') && t(`designer.field.${value('field')}`),
      assign: value('to') && t(`designer.assign.${value('to')}`),
      'set-resolution': value('value'),
    };
    const detail = details[rule.key];
    return detail ? t(`designer.summary.${rule.key}`, { value: detail }) : t(`designer.rule.${rule.key}`);
  };
}

function RuleList({ title, hint, keys, rules, onChange, children }: {
  title: string;
  hint: string;
  keys: string[];
  rules: NestedRule[];
  onChange: (rules: NestedRule[]) => void;
  children?: ReactNode;
}) {
  const { t } = useTranslation();
  const summary = useRuleSummary();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [open, setOpen] = useState<number | null>(null);

  return (
    <section className="flex flex-col gap-1">
      <div className="flex items-center justify-between">
        <Typography variant="h3" component="h3">{title}</Typography>
        <Link component="button" variant="body2" sx={{ minHeight: 24 }} aria-label={`${t('designer.add')}: ${title}`} onClick={(event) => setAnchor(event.currentTarget)}>
          {t('designer.add')}
        </Link>
      </div>
      <Typography variant="caption" color="textSecondary">{hint}</Typography>
      {children}
      {rules.map((rule, index) => (
        <div key={`${rule.key}-${index}`} className="flex flex-col gap-2 p-2" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.control }}>
          <div className="flex items-center gap-1">
            {NO_SETTINGS.includes(rule.key) ? (
              <Typography variant="body2" className="flex-1">{summary(rule)}</Typography>
            ) : (
              <ButtonBase
                className="flex-1"
                aria-expanded={open === index}
                onClick={() => setOpen(open === index ? null : index)}
                sx={{ justifyContent: 'flex-start', textAlign: 'left', minHeight: 24, fontSize: font.size.xs, color: sem('text-primary') }}
              >
                {summary(rule)}
              </ButtonBase>
            )}
            <IconButton
              size="small"
              aria-label={`${t('common.delete')} ${summary(rule)}`}
              onClick={() => {
                setOpen(null);
                onChange(rules.filter((_, position) => position !== index));
              }}
            >
              <X size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </div>
          {open === index && (
            <RuleConfig rule={rule} onChange={(config) => onChange(rules.map((item, position) => (position === index ? { ...item, config } : item)))} />
          )}
        </div>
      ))}
      <PickerPopover
        label={title}
        anchor={anchor}
        items={keys.map((key) => ({ id: key, label: t(`designer.rule.${key}`) }))}
        onClose={() => setAnchor(null)}
        onPick={(key) => {
          setAnchor(null);
          setOpen(NO_SETTINGS.includes(key) ? null : rules.length);
          onChange([...rules, { key, config: {} }]);
        }}
      />
    </section>
  );
}

export function TransitionRules({ rules, onChange }: { rules: unknown[]; onChange: (rules: unknown[]) => void }) {
  const { t } = useTranslation();
  const all = rules as DraftRule[];
  const ofKind = (kind: number) => all.filter((rule) => rule.kind === kind);
  const replace = (kind: number, next: DraftRule[]) => onChange([...all.filter((rule) => rule.kind !== kind), ...next]);

  // Several conditions all have to pass; "any of" wraps them in the one rule the engine knows for that.
  const conditions = ofKind(CONDITION);
  const wrapped = conditions.length === 1 && conditions[0]!.key === ANY_OF;
  const inner: NestedRule[] = wrapped ? ((conditions[0]!.config?.rules as NestedRule[] | undefined) ?? []) : conditions;

  const writeConditions = (next: NestedRule[], matchAny: boolean) =>
    replace(
      CONDITION,
      matchAny
        ? [{ kind: CONDITION, key: ANY_OF, config: { rules: next } }]
        : next.map((rule) => ({ kind: CONDITION, key: rule.key, config: rule.config })),
    );

  const section = (title: string, hint: string, kind: number) => (
    <RuleList
      title={title}
      hint={hint}
      keys={KEYS[kind]!}
      rules={ofKind(kind)}
      onChange={(next) => replace(kind, next.map((rule) => ({ kind, key: rule.key, config: rule.config })))}
    />
  );

  return (
    <>
      <RuleList
        title={t('designer.conditions')}
        hint={t('designer.conditionsHint')}
        keys={KEYS[CONDITION]!}
        rules={inner}
        onChange={(next) => writeConditions(next, wrapped && next.length > 0)}
      >
        {inner.length > 1 && (
          <SelectField
            quiet
            label={t('designer.match')}
            value={wrapped ? 'any' : 'all'}
            onChange={(event) => writeConditions(inner, event.target.value === 'any')}
            options={[
              { value: 'all', label: t('designer.matchAll') },
              { value: 'any', label: t('designer.matchAny') },
            ]}
          />
        )}
      </RuleList>
      {section(t('designer.validators'), t('designer.validatorsHint'), VALIDATOR)}
      {section(t('designer.postFunctions'), t('designer.postFunctionsHint'), POST_FUNCTION)}
    </>
  );
}
