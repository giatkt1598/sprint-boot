import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { closeOpenMarkdown, renderStreamingMarkdown, useSmoothText } from './streaming';

describe('closeOpenMarkdown', () => {
  it('closes an open code fence', () => {
    expect(closeOpenMarkdown('Run:\n```ts\nconst a = 1;')).toBe('Run:\n```ts\nconst a = 1;\n```');
  });

  it('closes bold, strikethrough and inline code left open in the last paragraph', () => {
    expect(closeOpenMarkdown('Done.\n\nThis is **bold')).toBe('Done.\n\nThis is **bold**');
    expect(closeOpenMarkdown('Old ~~text')).toBe('Old ~~text~~');
    expect(closeOpenMarkdown('Call `get_issue')).toBe('Call `get_issue`');
    expect(closeOpenMarkdown('**Done** already')).toBe('**Done** already');
  });

  it('holds back a link until it closes', () => {
    expect(closeOpenMarkdown('See [SB-1](/issues/SB')).toBe('See ');
    expect(closeOpenMarkdown('See [SB-1')).toBe('See ');
    expect(closeOpenMarkdown('See [SB-1](/issues/SB-1)')).toBe('See [SB-1](/issues/SB-1)');
  });

  it('holds back a table row still being written', () => {
    expect(closeOpenMarkdown('| Key | Title |\n| --- | --- |\n| SB-1 | Pay')).toBe('| Key | Title |\n| --- | --- |');
  });
});

describe('renderStreamingMarkdown', () => {
  it('renders GFM with single newlines as breaks', () => {
    const html = renderStreamingMarkdown('**Bold**\nnext\n\n| A |\n| - |\n| 1 |');
    expect(html).toContain('<strong>Bold</strong><br>');
    expect(html).toContain('<table>');
  });

  it('leaves no layout newlines that would show as empty lines', () => {
    const html = renderStreamingMarkdown('Intro\nsecond line\n\n- a\n- b\n\n| A |\n| - |\n| 1 |\n\n```\ncode\n```\n\nEnd');
    expect(html).toBe(
      '<p>Intro<br>second line</p><ul><li>a</li><li>b</li></ul><table><thead><tr><th>A</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table><pre><code>code</code></pre><p>End</p>',
    );
  });

  it('wraps newly shown text to fade in, part way through when it appeared earlier', () => {
    const html = renderStreamingMarkdown('Hello **big** world', [
      { from: 0, to: 6, at: 1000 },
      { from: 6, to: 15, at: 1250 },
    ]);
    expect(html).toBe(
      '<p><span class="sb-reveal" style="animation-delay: -250ms;">Hello </span><strong><span class="sb-reveal">big</span></strong><span class="sb-reveal"> world</span></p>',
    );
  });

  it('strips scripts and event handlers from model output', () => {
    const html = renderStreamingMarkdown('Hi <script>alert(1)</script><img src=x onerror="alert(1)">');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('onerror');
  });
});

describe('useSmoothText', () => {
  afterEach(() => vi.useRealTimers());

  it('reveals whole words over several frames and finishes once the text is complete', () => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] });
    const text = 'one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen';
    const { result, rerender } = renderHook(({ target, finished }) => useSmoothText(target, finished), {
      initialProps: { target: text, finished: false },
    });
    expect(result.current.shown).toBe('');

    act(() => vi.advanceTimersToNextFrame());
    const first = result.current.shown;
    expect(first.length).toBeGreaterThan(0);
    expect(first.length).toBeLessThan(text.length);
    expect(text.startsWith(first)).toBe(true);
    expect(text[first.length]).toMatch(/\s/);

    // The last word may still grow, so it waits until the answer is finished.
    act(() => vi.advanceTimersByTime(2000));
    expect(result.current.shown).toBe(text.slice(0, text.lastIndexOf(' ') + 1));
    expect(result.current.caughtUp).toBe(false);

    rerender({ target: text, finished: true });
    act(() => vi.advanceTimersByTime(2000));
    expect(result.current.shown).toBe(text);
    expect(result.current.caughtUp).toBe(true);
  });

  it('shows everything at once when skipped', () => {
    const { result } = renderHook(() => useSmoothText('all of it', true, true));
    expect(result.current).toEqual({ shown: 'all of it', marks: [], caughtUp: true });
  });
});
