import type { ReactNode } from 'react';
import type { IconName } from '../../components/icons';
import { drawnSet } from './drawn';

/** A pointed gothic arch over a baseline, rising (up) or falling (down) at a height. */
const arch = (y: number, up: boolean) => (up ? `M5 ${y}c3-1 6-3 7-6 1 3 4 5 7 6` : `M5 ${y - 6}c3 1 6 3 7 6 1-3 4-5 7-6`);
/** A medallion: a round frame with an inner ring, the frame of many icons here. */
const medallion = (
  <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="7" strokeOpacity="0.45" />
  </>
);
/** A parchment with a curled top. */
const parchment = <path d="M7 4h11v13a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3h11M7 4a2 2 0 0 0-2 2v11" />;
/** A small fleur-de-lis around a point. */
const fleur = (cx: number, cy: number, s: number) => (
  <path
    transform={`translate(${cx} ${cy}) scale(${s})`}
    d="M0-6c-1.3 2-1.3 4 0 6 1.3-2 1.3-4 0-6zM0 0c-1.3-2-4.7-2-4.7.7 0 1.3 1.3 2 2 1.3M0 0c1.3-2 4.7-2 4.7.7 0 1.3-1.3 2-2 1.3M-2.7 2.3h5.4M0 0v6"
  />
);

/**
 * The European theme's icons, after rose windows, gothic arches, crowns, wax seals, quills, heraldic shields,
 * hourglasses, chests and the fleur-de-lis.
 */
const shapes: Partial<Record<IconName, ReactNode>> = {
  overview: <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M12 3.5v6M12 14.5v6M3.5 12h6M14.5 12h6M6 6l4.2 4.2M13.8 13.8L18 18M18 6l-4.2 4.2M10.2 13.8L6 18" />
  </>,
  // An illuminated page: a drop capital and lines.
  list: <>
    <rect x="4" y="5" width="4.5" height="4.5" rx="0.5" />
    <path d="M11.5 6h8.5M11.5 9h8.5M4 13.5h16M4 17.5h16" />
  </>,
  // A ledger.
  backlog: <path d="M5 3.5h14v17H5zM8 3.5v17M10.5 8h6M10.5 12h6M10.5 16h6" />,
  board: <path d="M4 20V9.5a2.5 2.5 0 0 1 5 0V20M9.5 20V7.5a2.5 2.5 0 0 1 5 0V20M15 20V9.5a2.5 2.5 0 0 1 5 0V20M3 20h18" />,
  epic: <path d="M4.5 17.5L3.5 8l5 4 3.5-7 3.5 7 5-4-1 9.5zM4.5 20.5h15" />,
  // A wax seal with its ribbons.
  task: <>
    <circle cx="12" cy="11" r="7.5" />
    <path d="M8.5 11l2.5 2.5 4.5-5M9 18l-1.5 3.5M15 18l1.5 3.5" />
  </>,
  people: <>
    <circle cx="9" cy="8" r="3" />
    <circle cx="16.5" cy="9" r="2.5" />
    <path d="M3 20c0-4 2.7-6 6-6s6 2 6 6M15.5 14.5c3 0 5.5 2 5.5 5.5" />
  </>,
  // A cameo.
  user: <>
    <ellipse cx="12" cy="12" rx="7.5" ry="9" />
    <circle cx="12" cy="9.5" r="2.5" />
    <path d="M7.5 18c1-3 2.5-4.5 4.5-4.5s3.5 1.5 4.5 4.5" />
  </>,
  // A compass rose.
  settings: <>
    <circle cx="12" cy="12" r="7.5" />
    <path d="M12 2.5l1.4 8.1L12 12l-1.4-1.4zM12 21.5l-1.4-8.1L12 12l1.4 1.4zM2.5 12l8.1-1.4L12 12l-1.4 1.4zM21.5 12l-8.1 1.4L12 12l1.4-1.4z" />
  </>,
  // A bound book.
  story: <path d="M5 4.5h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3zM5 17.5a3 3 0 0 1 3-3h11M9 8.5h6" />,
  bug: <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M7.5 9.5c1.5-2 3-2.5 4.5-2.5s3 .5 4.5 2.5M7 12H4M7 16H4M17 12h3M17 16h3" />
  </>,
  // A quill.
  subtask: <path d="M20 4c-8 1-12 6-13 13l-2 3.5M20 4c-1 6-5 10-11 11" />,
  priorityHighest: <path d={`${arch(12, true)}${arch(18.5, true)}`} />,
  priorityHigh: <path d={arch(15.5, true)} />,
  priorityMedium: <path d="M5 9.5h14M5 14.5h14" />,
  priorityLow: <path d={arch(15, false)} />,
  priorityLowest: <path d={`${arch(12, false)}${arch(18.5, false)}`} />,
  // A church bell.
  notifications: <>
    <path d="M7 17v-6a5 5 0 0 1 10 0v6l2 2H5zM12 3v3" />
    <circle cx="12" cy="21" r="1" />
  </>,
  // A monocle on its chain.
  search: <>
    <circle cx="10" cy="10" r="6" />
    <path d="M14.5 14.5L20 20M16 4c2 1 3 3 3 5" />
  </>,
  // A cross pattée.
  add: <path d="M10 4h4l-1 6 6-1v4l-6-1 1 6h-4l1-6-6 1V9l6 1z" />,
  assistant: fleur(12, 12, 1.4),
  // An urn with its lid.
  delete: <path d="M5 7h14M9 7V4.5h6V7M6.5 7l1 13h9l1-13M10 10.5v6.5M14 10.5v6.5" />,
  warning: <path d="M12 3.5L21.5 19.5h-19zM12 9.5v4.5M12 16.5v.5" />,
  // A pen nib.
  edit: <>
    <path d="M12 3l5 7-5 11-5-11zM12 10v5" />
    <circle cx="12" cy="10" r="1" />
  </>,
  copy: <path d="M9 8h10v12.5H9zM15 8V3.5H5V16h4" />,
  filter: <path d="M4 5h16l-6 7.5V19l-4 2v-8.5z" />,
  // Two chain links.
  link: <>
    <rect x="2.5" y="8.5" width="11" height="7" rx="3.5" />
    <rect x="10.5" y="8.5" width="11" height="7" rx="3.5" />
  </>,
  pullRequest: <>
    <circle cx="6" cy="5.5" r="2" />
    <circle cx="6" cy="18.5" r="2" />
    <circle cx="18" cy="18.5" r="2" />
    <path d="M6 7.5v9M18 16.5V9h-5M15 6.5L12.5 9 15 11.5" />
  </>,
  commit: <>
    <path d="M12 8l4 4-4 4-4-4z" />
    <path d="M3 12h5M16 12h5" />
  </>,
  // A branch ending in a fleur-de-lis.
  branch: <>
    <path d="M6 3v18M6 15c0-4.5 4-6 8-6" />
    {fleur(17, 8.5, 0.6)}
  </>,
  show: <>
    <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z" />
    <path d="M12 9l3 3-3 3-3-3z" />
  </>,
  hide: <path d="M2.5 12S6 5.5 12 5.5c2 0 3.7.7 5 1.7M21.5 12s-3.5 6.5-9.5 6.5c-2 0-3.7-.7-5-1.7M4 4l16 16" />,
  // An old key.
  tool: <>
    <circle cx="7" cy="12" r="3.5" />
    <path d="M10.5 12H21M17.5 12v3M20 12v2.5" />
  </>,
  upload: <path d="M12 16V4.5M7 9.5l5-5 5 5M4 15v5h16v-5" />,
  like: <path d="M4 10h3v10H4zM7 10l4-6.5c1.5 0 2.5 1 2.2 2.5L12.5 9H19l-1.5 11H7" />,
  reaction: <>
    <circle cx="11" cy="12.5" r="8" />
    <path d="M8 14.5c1.6 2 4.4 2 6 0M8.5 10v1M13.5 10v1M19.5 2.5v4M17.5 4.5h4" />
  </>,
  // A heraldic shield with a fleur-de-lis.
  admin: <>
    <path d="M12 3l8 3v6c0 5-3.5 8-8 9.5C7.5 20 4 17 4 12V6z" />
    {fleur(12, 11.5, 0.65)}
  </>,
  attachment: <path d="M20 11.5l-7.8 7.8a5 5 0 0 1-7-7L13 4.5a3.3 3.3 0 0 1 4.7 4.7l-7.8 7.8a1.7 1.7 0 0 1-2.3-2.3L14.5 7.8" />,
  // An arched door.
  signOut: <path d="M10.5 20.5H5V9a5 5 0 0 1 5.5-5M14 8l4 4-4 4M18 12H9" />,
  locked: <>
    <path d="M5 10.5h14v9.5H5zM8 10.5V7a4 4 0 0 1 8 0v3.5" />
    <circle cx="12" cy="14.3" r="1.3" />
    <path d="M12 15.6v1.9" />
  </>,
  info: <>
    {medallion}
    <path d="M12 11v5M12 8v.3" />
  </>,
  help: <>
    {medallion}
    <path d="M10 10a2 2 0 1 1 3 1.8c-.6.3-1 .7-1 1.4v.3M12 16v.3" />
  </>,
  success: <>
    {medallion}
    <path d="M8.5 12.5l2.5 2.5 4.5-5.5" />
  </>,
  error: <>
    {medallion}
    <path d="M12 8v5M12 15.8v.3" />
  </>,
  // A chest with a domed lid.
  inbox: <path d="M4 11a8 4.5 0 0 1 16 0v9H4zM4 13h16M11 13v3h2v-3" />,
  // An hourglass.
  history: <path d="M6 3h12M6 21h12M7 3c0 5 10 5 10 9s-10 4-10 9M17 3c0 5-10 5-10 9s10 4 10 9" />,
  // A clock face with its hours marked.
  time: <>
    <circle cx="12" cy="12" r="8.5" />
    <path d="M12 7.5V12l3 2M12 4v1.3M12 18.7V20M4 12h1.3M18.7 12H20" />
  </>,
  document: <>
    {parchment}
    <path d="M9.5 8h5.5M9.5 11h5.5M9.5 14h3" />
  </>,
  notFound: <>
    {parchment}
    <path d="M10.5 9.5a2 2 0 1 1 3 1.7c-.7.4-1 .9-1 1.6M12.5 15v.3" />
  </>,
  date: <>
    <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4" />
    {fleur(12, 15, 0.45)}
  </>,
  dateRange: <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4M8 14h2M14 14h2M8 17h2M14 17h2" />,
  // A clockwork automaton.
  bot: <>
    <path d="M5.5 8.5h13v10h-13zM12 8.5V5M10 5h4M10 16h4" />
    <circle cx="9.5" cy="12.5" r="1.3" />
    <circle cx="14.5" cy="12.5" r="1.3" />
  </>,
  // A painting in a gilt frame.
  image: <path d="M3 4h18v16H3zM5.5 6.5h13v11h-13zM5.5 15l4-4 3 3 2-2 4 4" />,
  // A candle on its holder.
  callout: <path d="M10 9h4v9h-4zM12 9V7.5M12 7.5c-1.2-1-1.2-2.5 0-4 1.2 1.5 1.2 3 0 4zM6 18h12a3 3 0 0 1-3 3H9a3 3 0 0 1-3-3z" />,
  loading: <>
    <path d="M12 3.5a8.5 8.5 0 1 1-8.5 8.5" />
    <path d="M12 9l3 3-3 3-3-3z" />
  </>,
};

export default drawnSet(shapes, { width: 1.5 });
