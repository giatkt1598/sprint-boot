import '@fontsource/lora/600.css';
import '@fontsource/oswald/600.css';

/**
 * The fonts people can choose. Body text is SVN-Poppins, or the system font for those who prefer it; the other choices
 * keep SVN-Poppins for body text and set the headings in a display font (self-hosted, OFL, with Vietnamese glyphs).
 * A theme suggests one of them, and choosing the theme picks it, but the person may choose another.
 */
export const FONT_CHOICES = ['poppins', 'system', 'lora', 'oswald'] as const;

export type FontChoice = (typeof FONT_CHOICES)[number];

const POPPINS = '"SVN-Poppins", sans-serif';
const SYSTEM = 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

export const FONT_FAMILIES: Record<FontChoice, { body: string; heading: string }> = {
  poppins: { body: POPPINS, heading: POPPINS },
  system: { body: SYSTEM, heading: SYSTEM },
  lora: { body: POPPINS, heading: '"Lora", "SVN-Poppins", serif' },
  oswald: { body: POPPINS, heading: '"Oswald", "SVN-Poppins", sans-serif' },
};
