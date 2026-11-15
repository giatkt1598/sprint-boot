import dayjs from 'dayjs';
import 'dayjs/locale/vi';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

let displayTimeZone: string | undefined;

/** The person's time zone for every timestamp shown; empty goes back to the browser's. Set once their profile loads. */
export const setDisplayTimeZone = (zone: string | null | undefined) => {
  displayTimeZone = zone || undefined;
};

/** A calendar day carries no time, so no zone may move it. */
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;
const zoneOf = (value: string) => (DATE_ONLY.test(value) ? 'UTC' : displayTimeZone);

/** Today's date as YYYY-MM-DD in the person's time zone. */
export const todayIso = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: displayTimeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());

/** "2 hours ago", in the given language. */
export const formatRelative = (value: string | null | undefined, language: string) =>
  (value ? dayjs(value).locale(language.startsWith('vi') ? 'vi' : 'en').fromNow() : '');

const RECENT_DAYS = 7;

/** "2 hours ago" within the last week; older than that, "2026 Mar 04" in the person's time zone. */
export const formatRecent = (value: string | null | undefined, language: string, now = Date.now()) => {
  if (!value) {
    return '';
  }

  if (now - new Date(value).getTime() < RECENT_DAYS * 24 * 60 * 60 * 1000) {
    return dayjs(value).locale(language.startsWith('vi') ? 'vi' : 'en').from(now);
  }

  const parts = new Intl.DateTimeFormat(language, { year: 'numeric', month: 'short', day: '2-digit', timeZone: zoneOf(value) })
    .formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)?.value ?? '';
  return `${part('year')} ${part('month')} ${part('day')}`;
};

export const formatDate = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeZone: zoneOf(value) }).format(new Date(value)) : '';

export const formatDateTime = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short', timeZone: zoneOf(value) }).format(new Date(value)) : '';


export const formatSize = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

export const formatMinutes = (minutes: number) => {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return [hours ? `${hours}h` : '', rest ? `${rest}m` : ''].filter(Boolean).join(' ') || '0m';
};

/** Accepts "1h 30m", "90m", "2h" or a plain number of minutes. Returns null when unreadable. */
export const parseDuration = (text: string) => {
  const value = text.trim().toLowerCase();
  if (/^\d+$/.test(value)) {
    return Number(value);
  }
  const match = /^(?:(\d+)h)?\s*(?:(\d+)m)?$/.exec(value);
  if (!match || (!match[1] && !match[2])) {
    return null;
  }
  return Number(match[1] ?? 0) * 60 + Number(match[2] ?? 0);
};

/** Moves a typed duration by `step` minutes, never below 30m; an empty one only goes up. Null when there is nothing to move. */
export const stepDuration = (text: string, step: number) => {
  const empty = text.trim() === '';
  const minutes = empty ? 0 : parseDuration(text);
  if (minutes === null || (empty && step < 0)) {
    return null;
  }
  return formatMinutes(Math.max(30, minutes + step));
};
