import { UserManager } from 'oidc-client-ts';
import { clearSignedIn } from './signedInHint';

const origin = window.location.origin;

export const userManager = new UserManager({
  authority: origin,
  client_id: 'sprint-boot-web',
  redirect_uri: `${origin}/auth/callback`,
  post_logout_redirect_uri: `${origin}/login`,
  response_type: 'code',
  scope: 'openid profile email offline_access sprint-boot-api',
  automaticSilentRenew: true,
});

let renewing: Promise<unknown> | null = null;

/** Returns a valid token, renewing an expired one once however many requests are waiting. */
export async function getAccessToken() {
  const user = await userManager.getUser();
  if (!user) {
    return null;
  }
  if (!user.expired) {
    return user.access_token;
  }
  if (!user.refresh_token) {
    clearSignedIn();
    return null;
  }
  renewing ??= userManager.signinSilent().finally(() => {
    renewing = null;
  });
  await renewing.catch(() => undefined);
  const renewed = await userManager.getUser();
  if (!renewed || renewed.expired) {
    // Signing in again is the only way on, so `/` may show the landing page meanwhile.
    clearSignedIn();
    return null;
  }
  return renewed.access_token;
}

export async function signOut() {
  clearSignedIn();
  await userManager.revokeTokens(['refresh_token']).catch(() => undefined);
  const idToken = (await userManager.getUser())?.id_token;
  await userManager.removeUser();
  await userManager.signoutRedirect({ id_token_hint: idToken });
}
