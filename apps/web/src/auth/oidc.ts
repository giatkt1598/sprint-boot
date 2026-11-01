import { UserManager } from 'oidc-client-ts';

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

export async function getAccessToken() {
  const user = await userManager.getUser();
  return user && !user.expired ? user.access_token : null;
}

export async function signOut() {
  await userManager.revokeTokens(['refresh_token']).catch(() => undefined);
  await userManager.signoutRedirect();
}
