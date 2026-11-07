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
