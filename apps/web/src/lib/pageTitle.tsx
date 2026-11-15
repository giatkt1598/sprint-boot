import { createContext, use, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * The browser tab title: "<page> - <section> - <space> | Sprint Boot". A page names itself with usePageTitle; the
 * layouts around it add what they stand for (the space, Settings, Account) with PageTitleScope, so a page never has to
 * know where it sits.
 *
 * Every mounted title is kept in a list and the tab shows the deepest one, the latest among equals: a ticket panel opened
 * over the board names the tab while it is open, and the board's title comes back when it closes.
 */

type Part = string | null | undefined | false;

interface Scope {
  parts: string[];
  depth: number;
}

const ScopeContext = createContext<Scope>({ parts: [], depth: 0 });

interface Entry {
  title: string;
  depth: number;
  order: number;
}

const entries: Entry[] = [];
let mounted = 0;
let fallback: string | null = null;

function show() {
  const top = entries.reduce<Entry | null>((best, entry) => (!best || entry.depth > best.depth || (entry.depth === best.depth && entry.order > best.order) ? entry : best), null);
  fallback ??= document.title;
  document.title = top?.title ?? fallback;
}

/** Joins the parts into a tab title; empty parts are left out. */
export function formatPageTitle(parts: Part[], appName: string) {
  const named = parts.filter((part): part is string => typeof part === 'string' && part.trim().length > 0);
  return named.length > 0 ? `${named.join(' - ')} | ${appName}` : appName;
}

/**
 * Adds parts after the titles of every page inside, and puts those pages above the titles outside. With reset, the parts
 * of the scopes outside are dropped: a ticket panel over the board names its own space.
 */
export function PageTitleScope({ parts = [], reset = false, children }: { parts?: Part[]; reset?: boolean; children: ReactNode }) {
  const outer = use(ScopeContext);
  const key = parts.join('\u0000');
  const value = useMemo<Scope>(
    () => ({ parts: [...parts.filter((part): part is string => typeof part === 'string' && part.length > 0), ...(reset ? [] : outer.parts)], depth: outer.depth + 1 }),
    // The parts arrive as a new array on every render; their text is what matters.
    [key, reset, outer],
  );
  return <ScopeContext value={value}>{children}</ScopeContext>;
}

/** Names the tab after this page, for as long as it is mounted. */
export function usePageTitle(...parts: Part[]) {
  const { t } = useTranslation();
  const scope = use(ScopeContext);
  const title = formatPageTitle([...parts, ...scope.parts], t('app.name'));
  const entry = useRef<Entry | null>(null);

  useEffect(() => {
    const own: Entry = { title: '', depth: scope.depth, order: ++mounted };
    entry.current = own;
    entries.push(own);
    return () => {
      entries.splice(entries.indexOf(own), 1);
      entry.current = null;
      show();
    };
  }, [scope.depth]);

  useEffect(() => {
    if (entry.current) {
      entry.current.title = title;
      show();
    }
  }, [title, scope.depth]);
}
