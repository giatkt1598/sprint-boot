import '@fontsource/lora/600.css';
import '@fontsource/oswald/600.css';
import '@fontsource/noto-serif/700.css';
// Headings are Latin and Vietnamese, so only those subsets of Noto Serif JP; its Japanese files alone weigh over 100 KB of CSS.
import '@fontsource/noto-serif-jp/latin-600.css';
import '@fontsource/noto-serif-jp/latin-ext-600.css';
import '@fontsource/noto-serif-jp/vietnamese-600.css';
import '@fontsource/alegreya/700.css';
import '@fontsource/playfair-display/700.css';

/**
 * The fonts people can choose. Body text is SVN-Poppins, or the system font for those who prefer it; the other choices
 * keep SVN-Poppins for body text and set the headings in a display font (self-hosted, OFL, with Vietnamese glyphs).
 * A theme suggests one of them, and choosing the theme picks it, but the person may choose another.
 */
export const FONT_CHOICES = ['poppins', 'system', 'lora', 'oswald', 'notoSerif', 'notoSerifJp', 'alegreya', 'playfair'] as const;

export type FontChoice = (typeof FONT_CHOICES)[number];

const POPPINS = '"SVN-Poppins", sans-serif';
const SYSTEM = 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

export const FONT_FAMILIES: Record<FontChoice, { body: string; heading: string }> = {
  poppins: { body: POPPINS, heading: POPPINS },
  system: { body: SYSTEM, heading: SYSTEM },
  lora: { body: POPPINS, heading: '"Lora", "SVN-Poppins", serif' },
  oswald: { body: POPPINS, heading: '"Oswald", "SVN-Poppins", sans-serif' },
  notoSerif: { body: POPPINS, heading: '"Noto Serif", "SVN-Poppins", serif' },
  notoSerifJp: { body: POPPINS, heading: '"Noto Serif JP", "Noto Serif", "SVN-Poppins", serif' },
  alegreya: { body: POPPINS, heading: '"Alegreya", "SVN-Poppins", serif' },
  playfair: { body: POPPINS, heading: '"Playfair Display", "SVN-Poppins", serif' },
};
