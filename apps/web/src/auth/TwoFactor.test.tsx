import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import { ToastHost } from '../components/ToastHost';

const post = vi.fn();
const get = vi.fn();
const continueSignIn = vi.fn();
let me = { twoFactorEnabled: false, twoFactorRequired: false };

vi.mock('../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args), GET: (...args: unknown[]) => get(...args) },
  unwrap: async <T,>(call: Promise<T>) => call,
  fetchCsrfToken: () => Promise.resolve('csrf'),
}));
vi.mock('./oidc', () => ({ signOut: vi.fn(), userManager: {} }));
vi.mock('./AuthProvider', () => ({ useCurrentUser: () => me }));
vi.mock('./AuthPages', async () => ({
  ...(await vi.importActual<object>('./AuthPages')),
  continueSignIn: (...args: unknown[]) => continueSignIn(...args),
}));

// jsdom has no layout.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const { TwoFactorCodePage, TwoFactorSection } = await import('./TwoFactor');

const wrap = (node: ReactNode, path = '/two-factor') =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <ToastHost>{node}</ToastHost>
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe('two-factor', () => {
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('signs in with a code from the app, or with a recovery code, and goes on to the authorize request', async () => {
    post.mockResolvedValue(undefined);
    wrap(<TwoFactorCodePage />, '/two-factor?ReturnUrl=%2Fconnect%2Fauthorize%3Fx%3D1');

    fireEvent.change(screen.getByRole('textbox', { name: 'Authentication code' }), { target: { value: '123 456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));
    await waitFor(() => expect(continueSignIn).toHaveBeenCalledWith('/connect/authorize?x=1'));
    expect(post).toHaveBeenCalledWith('/api/v1/auth/two-factor', { body: { code: '123 456', isRecoveryCode: false }, headers: { RequestVerificationToken: 'csrf' } });

    // A new page: after a sign-in the page waits for the browser to leave.
    cleanup();
    wrap(<TwoFactorCodePage />);
    fireEvent.click(screen.getByRole('button', { name: 'Use a recovery code instead' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Recovery code' }), { target: { value: 'abcde-fghij' } });
    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));
    await waitFor(() => expect(post).toHaveBeenLastCalledWith('/api/v1/auth/two-factor', expect.objectContaining({ body: { code: 'abcde-fghij', isRecoveryCode: true } })));
  });

  it('turns on an authenticator app from the security page and shows the recovery codes once', async () => {
    me = { twoFactorEnabled: false, twoFactorRequired: false };
    get.mockResolvedValue({ sharedKey: 'ABCDEFGHIJKLMNOP', authenticatorUri: 'otpauth://totp/Sprint%20Boot:me?secret=ABCDEFGHIJKLMNOP' });
    post.mockResolvedValue({ recoveryCodes: ['AAAAA-BBBBB', 'CCCCC-DDDDD'] });
    wrap(<TwoFactorSection />);

    expect(screen.getByText('Off')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Turn on' }));
    // The key in fours, as the apps show it, beside the QR code.
    expect(await screen.findByText('ABCD EFGH IJKL MNOP')).toBeTruthy();
    expect(screen.getByTitle('QR code for the authenticator app')).toBeTruthy();

    fireEvent.change(screen.getByRole('textbox', { name: 'Authentication code' }), { target: { value: '654321' } });
    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));
    const list = await screen.findByRole('list', { name: 'Recovery codes' });
    expect(list.textContent).toContain('AAAAA-BBBBB');
    expect(post).toHaveBeenCalledWith('/api/v1/auth/two-factor/setup', { body: { code: '654321' } });
  });

  it('keeps two-factor on while the workspace requires it', () => {
    me = { twoFactorEnabled: true, twoFactorRequired: true };
    wrap(<TwoFactorSection />);
    expect(screen.getByRole('button', { name: 'New recovery codes' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Turn off' })).toBeNull();
  });
});
