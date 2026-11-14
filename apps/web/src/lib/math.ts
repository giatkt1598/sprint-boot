import type { MarkedExtension, Tokens } from 'marked';

/**
 * Formulas in assistant answers. The server (RichTextMarkdown.AnswerToHtmlAsync) and the streaming preview both write a
 * formula as an element with data-type inline-math or block-math and its TeX in data-latex; typesetMath turns those into
 * KaTeX. The rules below mirror Markdig's mathematics extension.
 */

const MATH_DELIMITERS =
  /(```[\s\S]*?(?:```|$)|~~~[\s\S]*?(?:~~~|$)|`[^`\n]*`)|\\\[([\s\S]+?)\\\]|\\\(([^\n]+?)\\\)/g;

/** \( \) and \[ \] become dollar signs outside code, as RichTextMarkdown.NormalizeMathDelimiters does. */
export function normalizeMathDelimiters(text: string): string {
  return text.replace(MATH_DELIMITERS, (match, code?: string, display?: string, inline?: string) =>
    code !== undefined ? match : display !== undefined ? `$$${display.trim()}$$` : `$${inline!.trim()}$`,
  );
}

/** A command written with its backslash doubled, as if escaped in a string, means the command, not a line break. */
const cleanTex = (tex: string) => tex.trim().replace(/\\\\(?=[A-Za-z])/g, '\\');

const escapeHtml = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const mathHtml = (tag: 'span' | 'div', display: boolean, tex: string) =>
  `<${tag} data-type="${display ? 'block-math' : 'inline-math'}" data-latex="${escapeHtml(tex)}">${escapeHtml(tex)}</${tag}>`;

interface MathToken extends Tokens.Generic {
  tex: string;
  display: boolean;
}

/** $…$ and $$…$$ inline, $$ on lines of their own as a block. */
export const markedMath: MarkedExtension = {
  extensions: [
    {
      name: 'blockMath',
      level: 'block',
      start: (src) => src.match(/^\$\$/m)?.index,
      tokenizer(src) {
        const match = /^\$\$[ \t]*\n([\s\S]+?)\n\$\$[ \t]*(?:\n|$)/.exec(src);
        return match ? { type: 'blockMath', raw: match[0], tex: cleanTex(match[1]), display: true } : undefined;
      },
      renderer: (token) => mathHtml('div', true, (token as MathToken).tex),
    },
    {
      name: 'inlineMath',
      level: 'inline',
      start: (src) => src.indexOf('$'),
      tokenizer(src) {
        // No space just inside the dollars, and no digit right after the closing one, so prices stay text.
        const match = /^(\${1,2})(?!\s)((?:\\[\s\S]|[^\\$])+?)(?<!\s)\1(?!\d)/.exec(src);
        return match
          ? { type: 'inlineMath', raw: match[0], tex: cleanTex(match[2]), display: match[1].length === 2 }
          : undefined;
      },
      renderer: (token) => mathHtml('span', (token as MathToken).display, (token as MathToken).tex),
    },
  ],
};

const SELECTOR = '[data-type="inline-math"][data-latex], [data-type="block-math"][data-latex]';

let katex: typeof import('katex').default | undefined;
let loading: Promise<void> | undefined;

/** KaTeX and its stylesheet load the first time a formula shows up. */
const loadKatex = () =>
  (loading ??= Promise.all([import('katex'), import('katex/dist/katex.min.css')]).then(([module]) => {
    katex = module.default;
  }));

/** Typesets the formulas in `root`: at once when KaTeX is loaded, otherwise once it is. TeX it cannot read shows as written, in the error color. */
export function typesetMath(root: HTMLElement) {
  const formulas = [...root.querySelectorAll<HTMLElement>(SELECTOR)];
  if (formulas.length === 0) {
    return;
  }
  const render = (lib: NonNullable<typeof katex>) => {
    for (const formula of formulas) {
      if (!formula.isConnected || formula.dataset.typeset === 'true') {
        continue;
      }
      lib.render(formula.dataset.latex ?? '', formula, {
        displayMode: formula.dataset.type === 'block-math',
        throwOnError: false,
        output: 'htmlAndMathml',
      });
      formula.dataset.typeset = 'true';
    }
  };
  if (katex) {
    render(katex);
  } else {
    void loadKatex().then(() => render(katex!));
  }
}
