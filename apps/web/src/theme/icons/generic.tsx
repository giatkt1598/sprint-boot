import type { ReactNode } from 'react';
import type { IconName } from '../../components/icons';

/**
 * Shapes of the plain interface glyphs (arrows, chevrons, text formatting and the like) on the 24px grid. They carry
 * no motif of their own, so every culture set draws them from here in its own stroke, cap and join, and draws its own
 * pictures for everything that does carry a meaning.
 */
export const genericShapes: Partial<Record<IconName, ReactNode>> = {
  close: <path d="M6 6l12 12M18 6L6 18" />,
  check: <path d="M5 12.5l4.5 4.5L19 7.5" />,
  more: (
    <>
      <circle cx="5.5" cy="12" r="1" />
      <circle cx="12" cy="12" r="1" />
      <circle cx="18.5" cy="12" r="1" />
    </>
  ),
  chevronRight: <path d="M9.5 6l6 6-6 6" />,
  chevronLeft: <path d="M14.5 6l-6 6 6 6" />,
  chevronDown: <path d="M6 9.5l6 6 6-6" />,
  chevronUp: <path d="M6 14.5l6-6 6 6" />,
  dragHandle: (
    <>
      <circle cx="9" cy="6" r="1" />
      <circle cx="15" cy="6" r="1" />
      <circle cx="9" cy="12" r="1" />
      <circle cx="15" cy="12" r="1" />
      <circle cx="9" cy="18" r="1" />
      <circle cx="15" cy="18" r="1" />
    </>
  ),
  back: <path d="M19 12H5M11 6l-6 6 6 6" />,
  stop: <rect x="6.5" y="6.5" width="11" height="11" rx="1.5" />,
  reply: <path d="M10 6L4.5 11.5 10 17M5 11.5h8.5a6 6 0 0 1 6 6V19" />,
  minus: <path d="M5 12h14" />,
  maximize: <path d="M14 4h6v6M10 20H4v-6M20 4l-6.5 6.5M4 20l6.5-6.5" />,
  columns: <path d="M4 4.5h16v15H4zM9.3 4.5v15M14.7 4.5v15" />,
  expandCollapse: <path d="M8 9l4-4 4 4M8 15l4 4 4-4" />,
  arrowUp: <path d="M12 19V5M6 11l6-6 6 6" />,
  arrowDown: <path d="M12 5v14M6 13l6 6 6-6" />,
  mention: (
    <>
      <circle cx="12" cy="12" r="3.5" />
      <path d="M15.5 12v1.5a2.5 2.5 0 0 0 5 0V12a8.5 8.5 0 1 0-3.4 6.8" />
    </>
  ),
  inlineCode: <path d="M9 7l-5 5 5 5M15 7l5 5-5 5" />,
  codeBlock: <path d="M4 4.5h16v15H4zM10 9.5L7.5 12l2.5 2.5M14 9.5l2.5 2.5-2.5 2.5" />,
  heading1: <path d="M4 6v12M12 6v12M4 12h8M16.5 10l2.5-2v10" />,
  heading2: <path d="M4 6v12M12 6v12M4 12h8M16 10a2.2 2.2 0 1 1 4 1.3L16 18h4.5" />,
  heading3: <path d="M4 6v12M12 6v12M4 12h8M16 8.5h4l-2.3 3a2.6 2.6 0 1 1-2 4.5" />,
  taskList: <path d="M4 6.5l1.5 1.5 3-3M4 13.5l1.5 1.5 3-3M11.5 7h8.5M11.5 14h8.5M11.5 19h8.5" />,
  orderedList: <path d="M10.5 6.5H20M10.5 12H20M10.5 17.5H20M4.5 5h1v4M4 9h2.5M4 14.5a1.2 1.2 0 1 1 2 .9L4 18h2.5" />,
  quote: <path d="M5 17.5c2.5 0 4-1.5 4-4.5V7H4.5v6H9M15 17.5c2.5 0 4-1.5 4-4.5V7h-4.5v6H19" />,
  table: <path d="M4 4.5h16v15H4zM4 9.5h16M4 14.5h16M10 9.5v10" />,
  text: <path d="M5 6.5V5h14v1.5M12 5v14M9.5 19h5" />,
  alignLeft: <path d="M4 6h16M4 10h10M4 14h16M4 18h10" />,
  alignCenter: <path d="M4 6h16M7 10h10M4 14h16M7 18h10" />,
  alignRight: <path d="M4 6h16M10 10h10M4 14h16M10 18h10" />,
  bold: <path d="M7 5h6a3.5 3.5 0 0 1 0 7H7zM7 12h7a3.5 3.5 0 0 1 0 7H7z" />,
  italic: <path d="M10.5 5H18M6 19h7.5M14.5 5l-5 14" />,
  underline: <path d="M7 4.5v6a5 5 0 0 0 10 0v-6M5.5 20h13" />,
  strikethrough: <path d="M16.5 7.5c-.8-1.8-2.6-2.7-4.5-2.7-2.7 0-4.5 1.5-4.5 3.4 0 1.8 1.4 2.7 3.5 3.3M4.5 12h15M14 13.8c1.8.6 2.7 1.6 2.7 3 0 2-2 3.4-4.7 3.4-2.2 0-4-1-4.8-2.8" />,
  highlight: <path d="M14.5 4.5l5 5-8 8H6.5v-5zM4 20.5h9" />,
  textColor: <path d="M6.5 16l5.5-12 5.5 12M8.6 11.5h6.8M4.5 20h15" />,
  clearFormatting: <path d="M5 6.5V5h12v1.5M11 5l-3.5 14M4 4l16 16" />,
  splitColumns: <path d="M4 4.5h16v15H4zM12 4.5v15" />,
  splitRows: <path d="M4 4.5h16v15H4zM4 12h16" />,
};
