import { createApiClient } from '@sprint-boot/api-client';
import { getAccessToken } from './auth/oidc';

const ANONYMOUS_PATHS = [
  '/api/v1/auth/login',
  '/api/v1/auth/forgot-password',
  '/api/v1/auth/validate-reset-token',
  '/api/v1/auth/reset-password',
];

export const apiClient = createApiClient();

apiClient.use({
  async onRequest({ request }) {
    // CSRF tokens for these are issued anonymously, so a leftover session must not change who is calling.
    if (ANONYMOUS_PATHS.some((path) => new URL(request.url).pathname === path)) {
      return request;
    }
    const token = await getAccessToken();
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`);
    }
    return request;
  },
  async onResponse({ response }) {
    if (response.status === 401) {
      window.dispatchEvent(new Event('sb:unauthorized'));
    }
    return response;
  },
});

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly data: Record<string, string> = {},
  ) {
    super(message);
  }
}

interface ApiResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/** Throws ApiError for a failed call, otherwise returns the body. */
export async function unwrap<T>(call: Promise<ApiResult<T>>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) {
    const detail = (error as { error?: { code?: string; message?: string; data?: Record<string, string> } } | undefined)?.error;
    throw new ApiError(response.status, detail?.code, detail?.message ?? response.statusText, detail?.data);
  }
  return data as T;
}

/** POST for anonymous cookie endpoints that require the CSRF token. */
export async function fetchCsrfToken() {
  await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' });
  const cookie = document.cookie.split('; ').find((item) => item.startsWith('XSRF-TOKEN='));
  return decodeURIComponent(cookie?.split('=')[1] ?? '');
}
