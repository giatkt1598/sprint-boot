/**
 * A hint cookie for the web server: present, `/` serves the app; absent, it may serve the landing page (welcome.html).
 * It holds no secret and proves nothing; the API still checks the bearer token. Set when sign-in finishes and on every
 * app start with a session, cleared on sign-out and when the session cannot be renewed.
 * The name has no dot, so nginx can read it as $cookie_sb_signed_in.
 */
export const SIGNED_IN_COOKIE = 'sb_signed_in';
const THIRTY_DAYS = 30 * 24 * 60 * 60;

const attributes = () => `; Path=/; SameSite=Lax${window.location.protocol === 'https:' ? '; Secure' : ''}`;

export function markSignedIn() {
  document.cookie = `${SIGNED_IN_COOKIE}=1; Max-Age=${THIRTY_DAYS}${attributes()}`;
}

export function clearSignedIn() {
  document.cookie = `${SIGNED_IN_COOKIE}=; Max-Age=0${attributes()}`;
}
