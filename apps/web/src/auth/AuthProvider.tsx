import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createContext, use, useEffect, useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../api';
import { userManager } from './oidc';

export type CurrentUser = components['schemas']['CurrentUserDto'];

const AuthContext = createContext<CurrentUser | null>(null);

export function useCurrentUser() {
  const user = use(AuthContext);
  if (!user) {
    throw new Error('useCurrentUser must be used inside RequireAuth');
  }
  return user;
}

export function useCan(permission: string) {
  return (useCurrentUser().permissions ?? []).includes(permission);
}

export function RequireAuth({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  const session = useQuery({
    queryKey: ['auth', 'session'],
    queryFn: async () => (await userManager.getUser()) ?? null,
    staleTime: Infinity,
  });
  const signedIn = Boolean(session.data && !session.data.expired) || Boolean(session.data?.refresh_token);

  useEffect(() => {
    if (session.isSuccess && !signedIn) {
      void userManager.signinRedirect({ state: { returnTo: location.pathname + location.search } });
    }
  }, [session.isSuccess, signedIn, location.pathname, location.search]);

  useEffect(() => {
    const onExpired = () => {
      queryClient.clear();
      void navigate('/session-expired');
    };
    window.addEventListener('sb:unauthorized', onExpired);
    userManager.events.addSilentRenewError(onExpired);
    return () => {
      window.removeEventListener('sb:unauthorized', onExpired);
      userManager.events.removeSilentRenewError(onExpired);
    };
  }, [navigate, queryClient]);

  const me = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/auth/me')),
    enabled: signedIn,
  });

  useEffect(() => {
    if (me.data && me.data.language !== i18n.language) {
      void i18n.changeLanguage(me.data.language ?? undefined);
    }
  }, [me.data, i18n]);

  const value = useMemo(() => me.data ?? null, [me.data]);
  return value ? <AuthContext value={value}>{children}</AuthContext> : null;
}
