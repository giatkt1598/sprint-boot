import { act, cleanup, render } from '@testing-library/react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import i18n from '../i18n';
import { formatPageTitle, PageTitleScope, usePageTitle } from './pageTitle';

function Page({ parts, children }: { parts: (string | null | undefined)[]; children?: ReactNode }) {
  usePageTitle(...parts);
  return <>{children}</>;
}

function Translated() {
  const { t } = useTranslation();
  usePageTitle(t('nav.settings'));
  return null;
}

describe('page title', () => {
  beforeEach(async () => {
    document.title = 'Sprint Boot';
    await i18n.changeLanguage('en');
  });
  afterEach(() => cleanup());

  it('joins the parts with dashes and ends with the app name, leaving out empty parts', () => {
    expect(formatPageTitle(['SB-123', 'Login fails on the landing page', 'Sprint Boot'], 'Sprint Boot')).toBe('SB-123 - Login fails on the landing page - Sprint Boot | Sprint Boot');
    expect(formatPageTitle(['Backlog', null, undefined, '', ' ', 'Acme'], 'Sprint Boot')).toBe('Backlog - Acme | Sprint Boot');
    expect(formatPageTitle([], 'Sprint Boot')).toBe('Sprint Boot');
  });

  it('adds the parts of the scopes around a page, nearest first', () => {
    render(
      <PageTitleScope parts={['Acme']}>
        <PageTitleScope parts={['Settings']}>
          <Page parts={['Members']} />
        </PageTitleScope>
      </PageTitleScope>,
    );
    expect(document.title).toBe('Members - Settings - Acme | Sprint Boot');
  });

  it('shows a panel over the page while it is open, then the page again, and the old title once nothing names the tab', () => {
    function Board() {
      const [open, setOpen] = useState(false);
      return (
        <PageTitleScope parts={['Acme']}>
          <Page parts={['Board']}>
            <button type="button" onClick={() => setOpen(!open)}>toggle</button>
            {open && (
              <PageTitleScope reset>
                <Page parts={['SB-7', 'Fix the login', 'Acme']} />
              </PageTitleScope>
            )}
          </Page>
        </PageTitleScope>
      );
    }
    const { getByRole, unmount } = render(<Board />);
    expect(document.title).toBe('Board - Acme | Sprint Boot');

    act(() => getByRole('button').click());
    expect(document.title).toBe('SB-7 - Fix the login - Acme | Sprint Boot');

    act(() => getByRole('button').click());
    expect(document.title).toBe('Board - Acme | Sprint Boot');

    unmount();
    expect(document.title).toBe('Sprint Boot');
  });

  it('follows the data and the language', async () => {
    const { rerender } = render(<Page parts={['SB-7', undefined]} />);
    expect(document.title).toBe('SB-7 | Sprint Boot');
    rerender(<Page parts={['SB-7', 'Fix the login']} />);
    expect(document.title).toBe('SB-7 - Fix the login | Sprint Boot');
    cleanup();

    render(<Translated />);
    expect(document.title).toBe('Settings | Sprint Boot');
    await act(() => i18n.changeLanguage('vi'));
    expect(document.title).toBe(`${i18n.t('nav.settings')} | Sprint Boot`);
    expect(document.title).not.toBe('Settings | Sprint Boot');
  });
});
