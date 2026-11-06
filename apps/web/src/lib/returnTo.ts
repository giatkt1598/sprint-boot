const KEY = 'sprint-boot.return-to';

/** Called by the app shell on every page, so settings can send people back where they came from. */
export function rememberAppLocation(path: string) {
  try {
    sessionStorage.setItem(KEY, path);
  } catch {
    // Storage can be unavailable, such as in private windows; the fallback then applies.
  }
}

/** The last app page this tab showed, or the fallback when it showed none. */
export function appLocationOr(fallback: string) {
  try {
    return sessionStorage.getItem(KEY) ?? fallback;
  } catch {
    return fallback;
  }
}
