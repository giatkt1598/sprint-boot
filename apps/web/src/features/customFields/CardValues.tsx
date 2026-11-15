import { useTranslation } from 'react-i18next';
import { Avatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { formatDate } from '../../lib/format';
import { font, sem } from '../../theme/tokens';
import { FieldType, fieldKey, optionTone, type TicketCustomField } from './api';

/** The custom fields a board shows on its cards ("cf." keys, in the board's order), small and in one column. */
export function CardValues({ keys, items }: { keys: string[]; items: TicketCustomField[] | null | undefined }) {
  const { t, i18n } = useTranslation();
  const shown = keys.map((key) => items?.find((item) => fieldKey(item.field!) === key)).filter((item) => item !== undefined);
  if (shown.length === 0) {
    return null;
  }

  const small = { fontSize: font.size.xs, color: sem('text-secondary') };
  return (
    <span className="flex flex-col gap-1">
      {shown.map((item) => {
        const field = item.field!;
        const name = <span className="sr-only">{field.name}: </span>;
        switch (field.type) {
          case FieldType.select:
          case FieldType.multiSelect:
            return (
              <span key={field.id} className="flex flex-wrap gap-1">
                {name}
                {(item.options ?? []).map((option) => <Badge key={option.id} tone={optionTone(option.color)}>{option.label}</Badge>)}
              </span>
            );
          case FieldType.user:
            return (
              <span key={field.id} className="flex items-center gap-1" style={small}>
                {name}
                <Avatar name={item.user?.name ?? ''} src={item.user?.avatarUrl} />
                {item.user?.name}
              </span>
            );
          default: {
            const value = field.type === FieldType.checkbox ? t('customFields.yes')
              : field.type === FieldType.date ? formatDate(item.date, i18n.language)
              : field.type === FieldType.number ? String(item.number)
              : item.text;
            return <span key={field.id} className="line-clamp-2" style={small}>{t('customFields.chipIs', { name: field.name, value })}</span>;
          }
        }
      })}
    </span>
  );
}
