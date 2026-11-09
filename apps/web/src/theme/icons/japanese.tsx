import type { ReactNode } from 'react';
import type { IconName } from '../../components/icons';
import { drawnSet } from './drawn';

/** A five-petal sakura blossom around a point. */
const blossom = (cx: number, cy: number, r: number) => (
  <>
    {[0, 72, 144, 216, 288].map((angle) => (
      <ellipse key={angle} cx={cx} cy={cy - r} rx={r * 0.55} ry={r * 0.8} transform={`rotate(${angle} ${cx} ${cy})`} />
    ))}
    <circle cx={cx} cy={cy} r={r * 0.25} fill="currentColor" stroke="none" />
  </>
);
/** An enso, the brush circle left open, the round frame of many icons here. */
const enso = <path d="M18.6 7.2A8 8 0 1 0 19.4 15" />;
/** A sheet of washi with a folded corner. */
const washi = <path d="M6 3h8.5L19 7.5V21H6zM14.5 3v4.5H19" />;

/**
 * The Japanese theme's icons, in fine lines: shoji screens, torii, enso, kamon crests, waves, sakura, kokeshi,
 * tanzaku, paper lanterns and wind chimes.
 */
const shapes: Partial<Record<IconName, ReactNode>> = {
  overview: <path d="M4 3h16v18H4zM12 3v18M4 9h16M4 15h16" />,
  list: <path d="M6 7h12M7.5 12h9M5 17h14" />,
  backlog: <>
    <path d="M9 6.5h11M9 12h11M9 17.5h11" />
    <circle cx="5" cy="6.5" r="1.3" />
    <circle cx="5" cy="12" r="1.3" />
    <circle cx="5" cy="17.5" r="1.3" />
  </>,
  board: <path d="M3 6.5c4 1 14 1 18 0M5 10h14M7.5 7v13.5M16.5 7v13.5M12 10V7.2" />,
  epic: <path d="M3 17c2-6 7-9.5 12-8.5-3 1-4 4-2 6 2 2 5 1 6-1 0 4-4 7.5-9 6.5M3 20.5h18" />,
  task: <>
    {enso}
    <path d="M8.5 12l2.5 2.5 5-5" />
  </>,
  people: <>
    <circle cx="8.5" cy="7" r="2.5" />
    <circle cx="15.5" cy="7" r="2.5" />
    <path d="M6 20.5c0-5 1-8.5 2.5-8.5s2.5 3.5 2.5 8.5zM13 20.5c0-5 1-8.5 2.5-8.5s2.5 3.5 2.5 8.5z" />
  </>,
  // A kokeshi doll.
  user: <>
    <circle cx="12" cy="7" r="3.5" />
    <path d="M8.5 21c0-6 1.5-10 3.5-10s3.5 4 3.5 10zM9.5 15.5h5" />
  </>,
  // A kamon crest.
  settings: <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M12 3.5v6M12 14.5v6M4.6 7.75l5.2 3M14.2 13.25l5.2 3M4.6 16.25l5.2-3M14.2 10.75l5.2-3" />
  </>,
  // A tanzaku strip.
  story: <path d="M9 3h6v18l-3-2.2L9 21zM12 7v6" />,
  bug: <>
    <ellipse cx="12" cy="14" rx="5" ry="6" />
    <path d="M12 8v12M12 8L9.5 3.5M12 8l2.5-4.5M7 12H4M7 16H4M17 12h3M17 16h3" />
  </>,
  subtask: <>
    <path d="M4 4.5V11c0 3 2 5 5 5h4.5" />
    {blossom(18, 16, 2.6)}
  </>,
  priorityHighest: <path d="M4 12l8-6 8 6M4 18.5l8-6 8 6" />,
  priorityHigh: <path d="M4 15.5l8-6 8 6" />,
  priorityMedium: <path d="M5 9.5h14M5 14.5h14" />,
  priorityLow: <path d="M4 8.5l8 6 8-6" />,
  priorityLowest: <path d="M4 5.5l8 6 8-6M4 12l8 6 8-6" />,
  // A furin wind chime.
  notifications: <path d="M7 12.5a5 5 0 0 1 10 0v.5H7zM12 7.5V4M12 13v4M10 17h4v4h-4z" />,
  search: <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="M15.5 15.5L21 21" />
  </>,
  add: <>
    {enso}
    <path d="M12 8v8M8 12h8" />
  </>,
  assistant: blossom(12, 12, 4.5),
  // A wooden oke bucket with its hoops.
  delete: <path d="M5 7h14l-1.5 13h-11zM4 7h16M9 4h6M6 12h12M6.6 16.5h10.8" />,
  warning: <path d="M12 3.5L21.5 19.5h-19zM12 9.5v4.5M12 16.5v.5" />,
  // A fude brush.
  edit: <path d="M17 3l4 4-10 10-5 1.5L7.5 13zM14 6l4 4M4 21h7" />,
  copy: <path d="M8 3h6v15H8zM14 6h4v15h-6v-3" />,
  filter: <path d="M4 5h16l-6.5 7.5V19L10.5 20.5v-8z" />,
  link: <path d="M10 14a4 4 0 0 1 0-5.7l2.3-2.3a4 4 0 0 1 5.7 5.7l-1.2 1.2M14 10a4 4 0 0 1 0 5.7L11.7 18A4 4 0 0 1 6 12.3l1.2-1.2" />,
  pullRequest: <>
    <circle cx="6" cy="5.5" r="2" />
    <circle cx="6" cy="18.5" r="2" />
    <circle cx="18" cy="18.5" r="2" />
    <path d="M6 7.5v9M18 16.5V9h-5M15 6.5L12.5 9 15 11.5" />
  </>,
  commit: <>
    <circle cx="12" cy="12" r="3.5" />
    <path d="M3 12h5.5M15.5 12H21" />
  </>,
  branch: <>
    <path d="M6 3v18M6 15c0-4.5 4-6 9-6" />
    {blossom(17.5, 9, 2.2)}
  </>,
  show: <>
    <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z" />
    <path d="M14.6 10.3A3 3 0 1 0 15 13" />
  </>,
  hide: <path d="M2.5 12S6 5.5 12 5.5c2 0 3.7.7 5 1.7M21.5 12s-3.5 6.5-9.5 6.5c-2 0-3.7-.7-5-1.7M4 4l16 16" />,
  // A kanazuchi hammer.
  tool: <path d="M5 21l9-9M11.5 4.5l8 8-3 3-8-8z" />,
  upload: <path d="M12 16V4.5M7.5 9l4.5-4.5L16.5 9M4 15.5V20h16v-4.5" />,
  like: <path d="M4 10h3v10H4zM7 10l4-6.5c1.5 0 2.5 1 2.2 2.5L12.5 9H19l-1.5 11H7" />,
  // A daruma face.
  reaction: <>
    <path d="M17.5 6.5A8 8 0 1 0 19.4 13" />
    <path d="M8 14c1.6 2 4.4 2 6 0M8.5 9.5v1M13.5 9.5v1M19.5 2.5v4M17.5 4.5h4" />
  </>,
  // A kabuto helmet.
  admin: <path d="M4 15c0-5 3.5-9 8-9s8 4 8 9zM3 15h18M12 6V3M8 6.5L6 3.5M16 6.5l2-3M8 15v3M16 15v3" />,
  // A mizuhiki knot.
  attachment: <path d="M5 8c3 0 3 8 7 8s4-8 7-8M5 16c3 0 3-8 7-8s4 8 7 8" />,
  // A noren curtain at a doorway.
  signOut: <path d="M4 4h10v16H4zM7.5 4v9M10.5 4v9M15 12h6M18 9l3 3-3 3" />,
  locked: <path d="M5 10.5h14v9.5H5zM8 10.5V7a4 4 0 0 1 8 0v3.5M12 14v2.5" />,
  info: <>
    {enso}
    <path d="M12 11v5.5M12 7.5v.3" />
  </>,
  help: <>
    {enso}
    <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5v.4M12 16.5v.3" />
  </>,
  success: <>
    <circle cx="12" cy="12" r="8.5" />
    <path d="M8 12.5l3 3 5-6" />
  </>,
  error: <>
    {enso}
    <path d="M12 7.5v5.5M12 16v.3" />
  </>,
  // A wooden hako box.
  inbox: <path d="M3.5 9h17v11h-17zM3.5 9l2-4.5h13l2 4.5M9 13h6" />,
  history: <path d="M4 12a8 8 0 1 0 2.4-5.7M4 4v4h4M12 8v4l3 2" />,
  time: <>
    {enso}
    <path d="M12 7v5l3.5 2" />
  </>,
  document: <>
    {washi}
    <path d="M9 11h7M9 14.5h7M9 18h4" />
  </>,
  notFound: <>
    {washi}
    <path d="M10.5 12a2 2 0 1 1 3 1.7c-.7.4-1 .9-1 1.6M12.5 18v.3" />
  </>,
  // A calendar page with the hinomaru.
  date: <>
    <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4" />
    <circle cx="12" cy="15" r="2" fill="currentColor" stroke="none" />
  </>,
  dateRange: <path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4M8 14h2M14 14h2M8 17h2M14 17h2" />,
  bot: <path d="M5.5 8.5h13v10h-13zM12 8.5V5M10 5h4M9.5 12.5v1.5M14.5 12.5v1.5M10 16h4" />,
  // Fuji in a frame.
  image: <>
    <path d="M3.5 4.5h17v15h-17zM3.5 17l6-7 3 3 2-1.5 5.5 5.5" />
    <circle cx="16" cy="8.5" r="1.5" />
  </>,
  // An andon paper lamp.
  callout: <path d="M8 6h8v12H8zM8 10h8M8 14h8M12 3v3M10 21h4M12 18v3" />,
  loading: <path d="M12 4a8 8 0 1 1-7.4 5" />,
};

export default drawnSet(shapes, { width: 1.25 });
