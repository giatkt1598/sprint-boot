export const formatDate = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium' }).format(new Date(value)) : '';

export const formatDateTime = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '';

export const formatRelative = (value: string | null | undefined, language: string) => {
  if (!value) {
    return '';
  }
  const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
  const format = new Intl.RelativeTimeFormat(language, { numeric: 'auto' });
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [['day', 86400], ['hour', 3600], ['minute', 60]];
  for (const [unit, size] of steps) {
    if (Math.abs(seconds) >= size) {
      return format.format(Math.trunc(seconds / size), unit);
    }
  }
  return format.format(0, 'minute');
};

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
