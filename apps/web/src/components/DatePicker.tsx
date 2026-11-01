import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react';
import { useMemo, useState, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { font, radius, sem } from '../theme/tokens';
import { Popover } from './Popover';

const pad = (value: number) => String(value).padStart(2, '0');
const toIso = (year: number, month: number, day: number) => `${year}-${pad(month + 1)}-${pad(day)}`;
const SUNDAY = new Date(2023, 0, 1);

/** The days of a month, padded so the first falls under its weekday. */
export function monthCells(year: number, month: number) {
  const blanks = new Date(year, month, 1).getDay();
  const days = new Date(year, month + 1, 0).getDate();
  return [...Array<null>(blanks).fill(null), ...Array.from({ length: days }, (_, index) => index + 1)];
}

export interface DatePickerProps {
  label: string;
  /** ISO date, yyyy-mm-dd, or empty. */
  value: string;
  onChange: (value: string) => void;
  quiet?: boolean;
  disabled?: boolean;
}

export function DatePicker({ label, value, onChange, quiet, disabled }: DatePickerProps) {
  const { t, i18n } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const initial = value ? new Date(`${value}T00:00:00`) : new Date();
  const [view, setView] = useState({ year: initial.getFullYear(), month: initial.getMonth() });

  const weekdays = useMemo(() => {
    const format = new Intl.DateTimeFormat(i18n.language, { weekday: 'short' });
    return Array.from({ length: 7 }, (_, index) =>
      format.format(new Date(SUNDAY.getFullYear(), SUNDAY.getMonth(), SUNDAY.getDate() + index)),
    );
  }, [i18n.language]);

  const title = new Intl.DateTimeFormat(i18n.language, { month: 'long', year: 'numeric' }).format(new Date(view.year, view.month));
  const shown = value
    ? new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium' }).format(new Date(`${value}T00:00:00`))
    : t('common.none');

  const open = (event: MouseEvent<HTMLElement>) => {
    const base = value ? new Date(`${value}T00:00:00`) : new Date();
    setView({ year: base.getFullYear(), month: base.getMonth() });
    setAnchor(event.currentTarget);
  };

  const shift = (delta: number) => {
    const next = new Date(view.year, view.month + delta);
    setView({ year: next.getFullYear(), month: next.getMonth() });
  };

  const pick = (next: string) => {
    onChange(next);
    setAnchor(null);
  };

  return (
    <div className="flex flex-col gap-1">
      {!quiet && (
        <span aria-hidden style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
          {label}
        </span>
      )}
      <ButtonBase
        aria-haspopup="dialog"
        disabled={disabled}
        onClick={open}
        className="flex h-9 w-full items-center gap-2 px-2"
        sx={{
          justifyContent: 'flex-start',
          borderRadius: `${radius.control}px`,
          fontSize: font.size.md,
          color: sem(value ? 'text-primary' : 'text-tertiary'),
          border: quiet ? 'none' : `1px solid ${sem('border-strong')}`,
          '&:hover': { backgroundColor: sem('bg-hover') },
        }}
      >
        {!quiet && <CalendarDays size={16} strokeWidth={1.5} aria-hidden />}
        <span className="sr-only">{label}:</span>
        {shown}
      </ButtonBase>
      <Popover width={264} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div role="group" aria-label={label} className="flex flex-col gap-1">
          <div className="flex items-center gap-2">
            <IconButton size="small" aria-label={t('common.previousMonth')} onClick={() => shift(-1)}>
              <ChevronLeft size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
            <span aria-live="polite" className="flex-1" style={{ fontWeight: font.weight.medium }}>{title}</span>
            <IconButton size="small" aria-label={t('common.nextMonth')} onClick={() => shift(1)}>
              <ChevronRight size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </div>
          <div className="grid grid-cols-7 text-center">
            {weekdays.map((day) => (
              <span key={day} style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{day}</span>
            ))}
            {monthCells(view.year, view.month).map((day, index) => {
              if (day === null) {
                return <span key={`blank-${index}`} />;
              }
              const iso = toIso(view.year, view.month, day);
              const selected = iso === value;
              return (
                <ButtonBase
                  key={iso}
                  aria-label={`${day} ${title}`}
                  aria-pressed={selected}
                  onClick={() => pick(iso)}
                  sx={{
                    height: 32,
                    fontSize: font.size.xs,
                    borderRadius: `${radius.control}px`,
                    backgroundColor: selected ? sem('accent-default') : 'transparent',
                    color: sem(selected ? 'text-on-accent' : 'text-primary'),
                    '&:hover': { backgroundColor: sem(selected ? 'accent-hover' : 'bg-hover') },
                  }}
                >
                  {day}
                </ButtonBase>
              );
            })}
          </div>
          {value && (
            <ButtonBase
              onClick={() => pick('')}
              sx={{ alignSelf: 'flex-start', padding: 1, fontSize: font.size.xs, color: sem('text-accent') }}
            >
              {t('common.clearDate')}
            </ButtonBase>
          )}
        </div>
      </Popover>
    </div>
  );
}
