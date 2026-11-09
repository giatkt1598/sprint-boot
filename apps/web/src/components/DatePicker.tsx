import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import { ChevronLeftIcon, ChevronRightIcon, CloseIcon, DateRangeIcon } from './icons';
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

export type RangePart = 'start' | 'end' | 'single' | 'inside';

/** Where a day falls in a range of ISO dates: an end, between them, or outside (undefined). */
export function rangePart(iso: string, start: string, end: string): RangePart | undefined {
  if (start && end && start > end) {
    return iso === start ? 'start' : iso === end ? 'end' : undefined;
  }
  if (iso === start && iso === end) return 'single';
  if (iso === start) return start && end ? 'start' : 'single';
  if (iso === end) return start && end ? 'end' : 'single';
  return start && end && iso > start && iso < end ? 'inside' : undefined;
}

export interface DatePickerProps {
  label: string;
  /** ISO date, yyyy-mm-dd, or empty. */
  value: string;
  onChange: (value: string) => void;
  quiet?: boolean;
  disabled?: boolean;
  /**
   * The other end of a from–to pair: give the "to" picker its rangeStart, the "from" picker its rangeEnd. The calendar
   * then shades the days between, and while a day is hovered, the range it would make.
   */
  rangeStart?: string;
  rangeEnd?: string;
}

export function DatePicker({ label, value, onChange, quiet, disabled, rangeStart, rangeEnd }: DatePickerProps) {
  const { t, i18n } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [hovered, setHovered] = useState<string | null>(null);
  const ranged = rangeStart !== undefined || rangeEnd !== undefined;
  const mine = hovered ?? value;
  const start = rangeStart ?? mine;
  const end = rangeEnd ?? mine;
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

  const now = new Date();
  const today = toIso(now.getFullYear(), now.getMonth(), now.getDate());

  const pick = (next: string) => {
    onChange(next);
    setHovered(null);
    setAnchor(null);
  };

  return (
    <div className="flex flex-col gap-1">
      {!quiet && (
        <span aria-hidden style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
          {label}
        </span>
      )}
      <div className="relative">
      <ButtonBase
        aria-haspopup="dialog"
        disabled={disabled}
        onClick={open}
        className="flex h-9 w-full items-center gap-2 pl-2 pr-8"
        sx={{
          justifyContent: 'flex-start',
          borderRadius: `${radius.control}px`,
          fontSize: font.size.md,
          color: sem(value ? 'text-primary' : 'text-tertiary'),
          border: quiet ? 'none' : `1px solid ${sem('border-strong')}`,
          '&:hover': { backgroundColor: sem('bg-hover') },
        }}
      >
        {!quiet && <DateRangeIcon size={16} strokeWidth={1.5} aria-hidden />}
        <span className="sr-only">{label}:</span>
        {shown}
      </ButtonBase>
      {value && !disabled && (
        <IconButton
          size="small"
          aria-label={`${t('common.clearDate')}: ${label}`}
          onClick={() => onChange('')}
          sx={{ position: 'absolute', right: 4, top: 4 }}
        >
          <CloseIcon size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      )}
      </div>
      <Popover width={264} open={Boolean(anchor)} anchorEl={anchor} onClose={() => { setHovered(null); setAnchor(null); }}>
        <div role="group" aria-label={label} className="flex flex-col gap-1">
          <div className="flex items-center gap-2">
            <IconButton size="small" aria-label={t('common.previousMonth')} onClick={() => shift(-1)}>
              <ChevronLeftIcon size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
            <span aria-live="polite" className="flex-1" style={{ fontWeight: font.weight.medium }}>{title}</span>
            <IconButton size="small" aria-label={t('common.nextMonth')} onClick={() => shift(1)}>
              <ChevronRightIcon size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </div>
          <div className="grid grid-cols-7 text-center" onMouseLeave={() => setHovered(null)}>
            {weekdays.map((day) => (
              <span key={day} style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{day}</span>
            ))}
            {monthCells(view.year, view.month).map((day, index) => {
              if (day === null) {
                return <span key={`blank-${index}`} />;
              }
              const iso = toIso(view.year, view.month, day);
              const part = ranged ? rangePart(iso, start, end) : undefined;
              // The ends of a range are filled like a picked day; the days between sit on a band joining them.
              // While a day is hovered, the range shown is the one it would make, so the current value may sit inside it.
              const selected = ranged ? part !== undefined && part !== 'inside' : iso === value;
              const isToday = iso === today;
              const band = sem('bg-selected');
              const bandBackground = {
                inside: band,
                start: `linear-gradient(to right, transparent 50%, ${band} 50%)`,
                end: `linear-gradient(to left, transparent 50%, ${band} 50%)`,
                single: undefined,
              }[part ?? 'single'];
              return (
                <span key={iso} className="flex justify-center" style={{ background: bandBackground, marginBlock: 1 }}>
                  <ButtonBase
                    aria-label={`${day} ${title}`}
                    aria-pressed={iso === value}
                    aria-current={isToday ? 'date' : undefined}
                    onClick={() => pick(iso)}
                    onMouseEnter={() => ranged && setHovered(iso)}
                    onFocus={() => ranged && setHovered(iso)}
                    sx={{
                      height: 32,
                      width: ranged || isToday ? 32 : '100%',
                      fontSize: font.size.xs,
                      borderRadius: ranged || isToday ? '50%' : `${radius.control}px`,
                      ...(isToday && { border: `1px solid ${sem('accent-default')}` }),
                      backgroundColor: selected ? sem('accent-default') : 'transparent',
                      color: sem(selected ? 'text-on-accent' : part === 'inside' ? 'text-accent' : 'text-primary'),
                      '&:hover': { backgroundColor: sem(selected ? 'accent-hover' : 'bg-hover') },
                    }}
                  >
                    {day}
                  </ButtonBase>
                </span>
              );
            })}
          </div>
        </div>
      </Popover>
    </div>
  );
}
