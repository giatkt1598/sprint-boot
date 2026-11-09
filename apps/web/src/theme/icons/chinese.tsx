import type { ReactNode } from 'react';
import type { IconName } from '../../components/icons';
import { drawnSet } from './drawn';

const dot = (x: number, y: number, r = 0.9) => <circle cx={x} cy={y} r={r} fill="currentColor" stroke="none" />;
/** A square-holed bronze coin, the round frame of many icons here. */
const coin = <circle cx="12" cy="12" r="8.5" />;
/** A seal, the square frame of the others. */
const seal = <rect x="4" y="4" width="16" height="16" rx="1" />;
/** A hanging scroll: rollers at the top and bottom of a sheet. */
const scroll = <path d="M6.5 3.5h11v17h-11zM4.5 3.5h15M4.5 20.5h15" />;

/**
 * The Chinese theme's icons, cut with square ends after brush and seal carving: lattice windows, square-holed coins,
 * roof eaves, xiangyun clouds, seals, scrolls, lanterns and the Chinese knot.
 */
const shapes: Partial<Record<IconName, ReactNode>> = {
  overview: <>
    <rect x="3.5" y="3.5" width="17" height="17" rx="1" />
    <path d="M12 3.5v17M3.5 12h17M6.5 6.5h2.5v2.5M17.5 6.5H15v2.5M6.5 17.5h2.5V15M17.5 17.5H15V15" />
  </>,
  list: <path d="M5 4v16M19 4v16M8.5 7.5h7M8.5 12h7M8.5 16.5h7" />,
  backlog: <>
    <path d="M5 4h14v16H5zM5 9.3h14M5 14.6h14" />
    {dot(8, 6.7)}
    {dot(8, 12)}
    {dot(8, 17.3)}
  </>,
  board: <path d="M3 5.5l6-1.5v16l-6 1.5zM9 4l6 1.5v16L9 20zM15 5.5l6-1.5v16l-6 1.5z" />,
  epic: <path d="M5 16c-2.5 0-2.5-4.5.5-4.5 0-4.5 6.5-5.5 7.5-1.5 1-3.5 6.5-2.5 6.5 1 3 0 3 5 0 5zM9.5 16c0-2 3-2 3 0" />,
  task: <>
    <rect x="4.5" y="4.5" width="15" height="15" rx="2" />
    <path d="M8.5 12l2.5 2.5 4.5-5" />
  </>,
  people: <>
    <circle cx="9" cy="8" r="2.5" />
    <circle cx="16.5" cy="9" r="2" />
    <path d="M9 5.5V4M16.5 7V5.8M4 20c0-3.5 2.2-6 5-6s5 2.5 5 6M14.5 14.5c3 0 5.5 2 5.5 5.5" />
  </>,
  user: <>
    <circle cx="12" cy="8" r="3.5" />
    <path d="M12 4.5V2.8M5 20.5c0-4 3-6.5 7-6.5s7 2.5 7 6.5" />
  </>,
  settings: <>
    {coin}
    <rect x="9.5" y="9.5" width="5" height="5" />
    <path d="M12 3.5v2.5M12 18v2.5M3.5 12H6M18 12h2.5" />
  </>,
  story: <>
    <path d="M5.5 5h13M5 19h14M7 5v14M17 5v14M10 9h4M10 12.5h4M12 5L10 2.5M12 5l2-2.5" />
  </>,
  bug: <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M9 5l1.5 2.5M15 5l-1.5 2.5M7 11.5H4M7 15.5H4M17 11.5h3M17 15.5h3" />
  </>,
  subtask: <>
    <path d="M4 4.5c0 6 3 10.5 10 10.5M7 9.5l-2 1" />
    <circle cx="17" cy="15" r="3" />
    {dot(17, 15)}
  </>,
  priorityHighest: <path d="M4 11.5c3 0 5-2 8-5 3 3 5 5 8 5M4 18c3 0 5-2 8-5 3 3 5 5 8 5" />,
  priorityHigh: <path d="M4 15c3 0 5-2 8-5 3 3 5 5 8 5" />,
  priorityMedium: <>
    <path d="M5 9.5h14M5 14.5h14" />
    {dot(12, 9.5, 1)}
  </>,
  priorityLow: <path d="M4 9c3 0 5 2 8 5 3-3 5-5 8-5" />,
  priorityLowest: <path d="M4 6c3 0 5 2 8 5 3-3 5-5 8-5M4 12.5c3 0 5 2 8 5 3-3 5-5 8-5" />,
  notifications: <>
    <path d="M9.5 5h5M10.5 5V3.5h3V5M8.5 5c0 5.5-.8 8.5-2 10.5h11c-1.2-2-2-5-2-10.5M5 15.5h14" />
    <circle cx="12" cy="18.5" r="1.3" />
  </>,
  // Three carved strokes, as in the character for three.
  menu: <path d="M5 6.5h14M7 12h10M4 17.5h16" />,
  search: <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <rect x="9" y="9" width="3" height="3" />
    <path d="M15.5 15.5L20.5 20.5" />
  </>,
  add: <>
    {coin}
    <path d="M12 8v8M8 12h8" />
  </>,
  assistant: <>
    <ellipse cx="12" cy="12" rx="6" ry="8" />
    <path d="M12 2v2M9 4h6M9 20h6M12 20v2M12 4v16M8.5 5.5c-1.5 4-1.5 9 0 13M15.5 5.5c1.5 4 1.5 9 0 13" />
  </>,
  // A ding cauldron with its lid.
  delete: <path d="M4.5 8h15M9 8V5h6v3M6 8l1 11h10l1-11M10 11.5v5M14 11.5v5M7.5 19L7 21M16.5 19l.5 2" />,
  warning: <path d="M10 3.5h4M12 3.5L21 19.5H3zM12 9.5v4.5M12 16.5v.5" />,
  // A calligraphy brush.
  edit: <path d="M19 3.5l1.5 1.5-9 9-2.5.5.5-2.5zM8.5 15.5c-2 0-3.5 1.5-4 4 2.5-.5 4-2 4-4z" />,
  copy: <path d="M8.5 8.5h11.5V20H8.5zM15.5 8.5V4H4v11.5h4.5" />,
  filter: <path d="M4 5h16l-6 7.5V19l-4 2v-8.5z" />,
  // Two coins strung together.
  link: <>
    <circle cx="8.5" cy="12" r="5" />
    <circle cx="15.5" cy="12" r="5" />
    <rect x="7.5" y="11" width="2" height="2" />
    <rect x="14.5" y="11" width="2" height="2" />
  </>,
  pullRequest: <>
    <rect x="4" y="3.5" width="4" height="4" />
    <rect x="4" y="16.5" width="4" height="4" />
    <rect x="16" y="16.5" width="4" height="4" />
    <path d="M6 7.5v9M18 16.5V9h-5M15 6.5L12.5 9 15 11.5" />
  </>,
  commit: <path d="M3 12h6M15 12h6M9 9h6v6H9z" />,
  // A plum branch in blossom.
  branch: <>
    <path d="M6 3v18M6 15c0-4.5 4-6 9-6" />
    <circle cx="17.5" cy="9" r="2.5" />
    {dot(17.5, 9, 0.8)}
  </>,
  show: <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12zM10 10h4v4h-4z" />,
  hide: <path d="M2.5 12S6 5.5 12 5.5c2 0 3.7.7 5 1.7M21.5 12s-3.5 6.5-9.5 6.5c-2 0-3.7-.7-5-1.7M10 10h4v4h-4zM4 4l16 16" />,
  // An abacus.
  tool: <path d="M4 4h16v16H4zM4 10h16M8 6v2M12 6v2M16 6v2M8 12.5v5M12 12.5v5M16 12.5v5" />,
  upload: <path d="M12 16V4.5M7 9.5l5-5 5 5M4 15v5h16v-5" />,
  like: <path d="M4 10h3v10H4zM7 10l4-6.5c1.5 0 2.5 1 2.2 2.5L12.5 9H19l-1.5 11H7" />,
  reaction: <>
    <circle cx="11" cy="12.5" r="8" />
    <path d="M8 14.5c1.6 2 4.4 2 6 0M8.5 10v1M13.5 10v1M19.5 2.5v4M17.5 4.5h4" />
  </>,
  admin: <path d="M12 3l8 3v6c0 5-3.5 8-8 9.5C7.5 20 4 17 4 12V6zM9.5 9.5h5v5h-5z" />,
  // A Chinese knot.
  attachment: <path d="M12 2.5l4 4-4 4-4-4zM12 10.5l4 4-4 4-4-4zM12 18.5v3M8 6.5H5.5M16 6.5h2.5" />,
  signOut: <path d="M10 4H4v16h6M14 8l4 4-4 4M18 12H9" />,
  locked: <path d="M4 10.5h16v8H4zM8 10.5V7a4 4 0 0 1 8 0v3.5M12 13v3" />,
  info: <>
    {seal}
    <path d="M12 11v6M12 7.5v.5" />
  </>,
  help: <>
    {coin}
    <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5v.4M12 16.5v.3" />
  </>,
  success: <>
    {coin}
    <path d="M8 12.5l3 3 5-6" />
  </>,
  error: <>
    {coin}
    <path d="M12 7.5v5.5M12 16v.5" />
  </>,
  inbox: <path d="M4 13l2.5-8h11L20 13v6H4zM4 13h5l1 2h4l1-2h5" />,
  history: <path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3 4v4h4M12 8v4l3 2" />,
  // A sundial face.
  time: <>
    {coin}
    <path d="M12 7v5l3.5 2M12 2.5v1M12 20.5v1M2.5 12h1M20.5 12h1" />
  </>,
  document: <>
    {scroll}
    <path d="M9.5 8h5M9.5 12h5M9.5 16h3" />
  </>,
  notFound: <>
    {scroll}
    <path d="M10 10a2 2 0 1 1 3 1.7c-.7.4-1 .9-1 1.6M12 16v.3" />
  </>,
  // A lunar calendar page with the moon.
  date: <>
    <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4" />
    <circle cx="12" cy="15" r="2" />
  </>,
  dateRange: <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4M8 14h2M14 14h2M8 17h2M14 17h2" />,
  bot: <path d="M5 8h14v11H5zM12 8V4M9.5 4h5M9 12.5v1.5M15 12.5v1.5M9.5 16.5h5M3 12v3M21 12v3" />,
  // A landscape painting: mountains and the sun.
  image: <>
    <path d="M4 4h16v16H4zM4 17l5-6 4 4 2-2 5 5" />
    <circle cx="15.5" cy="8.5" r="1.5" />
  </>,
  // A candle.
  callout: <path d="M10 10h4v10h-4zM12 10V8.5M12 8.5c-1.2-1-1.2-2.5 0-4 1.2 1.5 1.2 3 0 4zM7.5 20h9" />,
  loading: <>
    <path d="M12 3.5a8.5 8.5 0 1 1-8.5 8.5" />
    <rect x="10.5" y="10.5" width="3" height="3" />
  </>,
};

export default drawnSet(shapes, { width: 1.5, cap: 'square' });
