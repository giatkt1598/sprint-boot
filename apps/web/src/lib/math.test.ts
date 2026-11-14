import { describe, expect, it } from 'vitest';
import { renderStreamingMarkdown } from '../features/assistant/streaming';
import { normalizeMathDelimiters, typesetMath } from './math';

describe('normalizeMathDelimiters', () => {
  it('turns brackets into dollar signs outside code', () => {
    expect(normalizeMathDelimiters('Half is \\( \\frac{1}{2} \\) and \\[x^2\\]')).toBe('Half is $\\frac{1}{2}$ and $$x^2$$');
    expect(normalizeMathDelimiters('Use `\\(x\\)` or\n```\n\\[y\\]\n```')).toBe('Use `\\(x\\)` or\n```\n\\[y\\]\n```');
  });
});

describe('formulas in answers', () => {
  it('keep their TeX as the server writes it', () => {
    expect(renderStreamingMarkdown('Total $4! = 24$ ways')).toBe(
      '<p>Total <span data-type="inline-math" data-latex="4! = 24">4! = 24</span> ways</p>',
    );
    expect(renderStreamingMarkdown('So $24 \\\\times \\\\frac{1}{2} = 12$')).toContain('data-latex="24 \\times \\frac{1}{2} = 12"');
    expect(renderStreamingMarkdown('$$\na_1 + b_1\n$$')).toBe('<div data-type="block-math" data-latex="a_1 + b_1">a_1 + b_1</div>');
    expect(renderStreamingMarkdown('See $$x^2$$ here')).toContain('<span data-type="block-math" data-latex="x^2">');
  });

  it('leave prices and code alone', () => {
    expect(renderStreamingMarkdown('It costs $5 and $10.')).toBe('<p>It costs $5 and $10.</p>');
    expect(renderStreamingMarkdown('Use `$x$` here')).toBe('<p>Use <code>$x$</code> here</p>');
  });

  it('are typeset with KaTeX', async () => {
    const root = document.createElement('div');
    root.innerHTML = renderStreamingMarkdown('Half is $\\frac{1}{2}$');
    document.body.append(root);
    typesetMath(root);
    await expect.poll(() => root.querySelector('.katex')).not.toBeNull();
    expect(root.querySelector('annotation')?.textContent).toBe('\\frac{1}{2}');
    root.remove();
  });
});
