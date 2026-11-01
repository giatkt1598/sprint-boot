export const formatDate = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium' }).format(new Date(value)) : '';

export const formatDateTime = (value: string | null | undefined, language: string) =>
  value ? new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '';

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
