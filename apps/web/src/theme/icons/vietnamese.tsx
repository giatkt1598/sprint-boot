import type { ReactNode } from 'react';
import type { IconName } from '../../components/icons';
import { drawnSet } from './drawn';

/** The star at the centre of a Đông Sơn drum, with as many points as asked. */
const star = (points: number, outer: number, inner: number, cx = 12, cy = 12) => {
  let d = '';
  for (let i = 0; i < points * 2; i++) {
    const r = i % 2 ? inner : outer;
    const a = (i / (points * 2)) * Math.PI * 2 - Math.PI / 2;
    d += `${i ? 'L' : 'M'}${(cx + Math.cos(a) * r).toFixed(2)} ${(cy + Math.sin(a) * r).toFixed(2)}`;
  }
  return <path d={`${d}Z`} />;
};

/** The upturned eaves of a đình roof, rising (up) or falling (down) at a height. */
const eave = (y: number, up: boolean) =>
  up ? `M3 ${y}c2 0 3-.8 4-1.8l5-4.2 5 4.2c1 1 2 1.8 4 1.8` : `M3 ${y - 6}c2 0 3 .8 4 1.8l5 4.2 5-4.2c1-1 2-1.8 4-1.8`;

/** The rim of a drum face, the round frame of many icons here, with a ring of tiny bosses. */
const drum = <circle cx="12" cy="12" r="8.5" />;
/** A sắc phong scroll. */
const decree = <path d="M6 4h12v16H6zM4 4h16M4 20h16M8.5 2.5v3M15.5 2.5v3" />;

/**
 * The Vietnamese theme's icons, after the Đông Sơn drum, nón lá, lotus, bamboo, the đình roof, the gong, the lạc bird,
 * baskets and oil lamps.
 */
const shapes: Partial<Record<IconName, ReactNode>> = {
  overview: <>
    {drum}
    <circle cx="12" cy="12" r="5" />
    {star(8, 3.2, 1.3)}
  </>,
  list: <path d="M4 7h16M4 12h16M4 17h16M10 6v2M15 11v2M8 16v2" />,
  backlog: <path d="M4 6h16M4 12h16M4 18h16M7 5v2M12 11v2M17 17v2M15 5v2M9 17v2" />,
  board: <path d="M6 3v18M12 3v18M18 3v18M5 8h2M11 13h2M17 9h2" />,
  epic: <path d="M12 19c-3-2-4-6-2-11 1 1 2 3 2 5 0-2 1-4 2-5 2 5 1 9-2 11zM12 19c-4 0-8-2-9-6 3-1 6 0 8 2M12 19c4 0 8-2 9-6-3-1-6 0-8 2" />,
  task: <>
    <rect x="4.5" y="4.5" width="15" height="15" rx="3" />
    <path d="M8.5 12l2.5 2.5 4.5-5" />
  </>,
  people: <path d="M3 10.5l5-5 5 5zM11 10.5l5-5 5 5zM5.5 21c0-5 1-8 2.5-8s2.5 3 2.5 8M13.5 21c0-5 1-8 2.5-8s2.5 3 2.5 8" />,
  // Someone under a nón lá.
  user: <path d="M5.5 9.5L12 3l6.5 6.5zM7 21c0-6 2-9 5-9s5 3 5 9" />,
  settings: <>
    {drum}
    {star(12, 6, 3.4)}
  </>,
  // A nón lá with its straps.
  story: <path d="M3 15L12 4l9 11zM2 15h20M7.5 15l2.5 5M16.5 15L14 20" />,
  bug: <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M9.5 5l1.5 2.5M14.5 5L13 7.5M7 11.5H4M7 15.5H4M17 11.5h3M17 15.5h3" />
  </>,
  // A bamboo shoot branching.
  subtask: <path d="M6 3v10c0 2.2 1.8 4 4 4h6M5 8h2M16 14l3 3-3 3" />,
  priorityHighest: <path d={`${eave(12, true)}${eave(18.5, true)}`} />,
  priorityHigh: <path d={eave(15.5, true)} />,
  priorityMedium: <path d="M5 9.5h14M5 14.5h14M5 8v3M19 8v3" />,
  priorityLow: <path d={eave(15, false)} />,
  priorityLowest: <path d={`${eave(12, false)}${eave(18.5, false)}`} />,
  // A gong on its cord.
  notifications: <>
    <circle cx="12" cy="13.5" r="6.5" />
    <circle cx="12" cy="13.5" r="2" />
    <path d="M12 7V3.5M9 3.5h6" />
  </>,
  // Bamboo canes, jointed in the middle.
  menu: <path d="M4 6.5h7M13 6.5h7M4 12h7M13 12h7M4 17.5h7M13 17.5h7" />,
  search: <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="M15.5 15.5L21 21M10.5 8v5M8 10.5h5" />
  </>,
  add: <>
    {drum}
    <path d="M12 8v8M8 12h8" />
  </>,
  // A lạc bird in flight.
  assistant: <path d="M3 13.5c4-1 7-4 9-8.5 1 4 4 6.5 9 6.5-4 2-8 3-12 3l-4 4.5" />,
  // A woven basket.
  delete: <path d="M4 8h16M9.5 5h5M5 8l2 12h10l2-12M6.3 12h11.4M7 16h10M10 8l1 12M14 8l-1 12" />,
  warning: <path d="M12 3.5L21.5 19.5h-19zM12 9.5v4.5M12 16.5v.5" />,
  // A bamboo pen.
  edit: <path d="M18 3l3 3-11 11-4.5 1.5L7 14zM15 6l3 3M13 8.5l2.5 2.5" />,
  copy: <>
    <rect x="8.5" y="8.5" width="11.5" height="11.5" rx="2.5" />
    <path d="M15.5 8.5V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7.5a2 2 0 0 0 2 2h2.5" />
  </>,
  filter: <path d="M4 5h16l-6 7.5V19l-4 2v-8.5z" />,
  // Two bamboo joints, linked.
  link: <path d="M10 14a4 4 0 0 1 0-5.7l2.3-2.3a4 4 0 0 1 5.7 5.7l-1.2 1.2M14 10a4 4 0 0 1 0 5.7L11.7 18A4 4 0 0 1 6 12.3l1.2-1.2M9 9l1.5 1.5M13.5 13.5L15 15" />,
  pullRequest: <>
    <circle cx="6" cy="5.5" r="2" />
    <circle cx="6" cy="18.5" r="2" />
    <circle cx="18" cy="18.5" r="2" />
    <path d="M6 7.5v9M18 16.5V9h-5M15 6.5L12.5 9 15 11.5" />
  </>,
  commit: <>
    {star(8, 3.5, 1.6)}
    <path d="M3 12h5M16 12h5" />
  </>,
  // A lotus stem branching to a flower.
  branch: <path d="M6 3v18M6 15c0-4.5 4-6 8-6M14 9c0-2 1.5-4 3.5-4.5.5 2-.5 4-3.5 4.5zM14 9c2 0 4 1.5 4.5 3.5-2 .5-4-.5-4.5-3.5z" />,
  show: <>
    <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z" />
    {star(6, 3, 1.4)}
  </>,
  hide: <path d="M2.5 12S6 5.5 12 5.5c2 0 3.7.7 5 1.7M21.5 12s-3.5 6.5-9.5 6.5c-2 0-3.7-.7-5-1.7M4 4l16 16" />,
  // A hoe.
  tool: <path d="M4.5 20.5L15 10M12.5 4.5c3 .5 6 3.5 7 7l-3 1.5c-1-2.5-3-4.5-5.5-5.5z" />,
  upload: <path d="M12 16V4.5M7 9.5l5-5 5 5M4 15v5h16v-5" />,
  like: <path d="M4 10h3v10H4zM7 10l4-6.5c1.5 0 2.5 1 2.2 2.5L12.5 9H19l-1.5 11H7" />,
  reaction: <>
    <circle cx="11" cy="12.5" r="8" />
    <path d="M8 14.5c1.6 2 4.4 2 6 0M8.5 10v1M13.5 10v1M19.5 2.5v4M17.5 4.5h4" />
  </>,
  admin: <>
    <path d="M12 3l8 3v6c0 5-3.5 8-8 9.5C7.5 20 4 17 4 12V6z" />
    {star(5, 3, 1.3, 12, 11.5)}
  </>,
  attachment: <path d="M20 11.5l-7.8 7.8a5 5 0 0 1-7-7L13 4.5a3.3 3.3 0 0 1 4.7 4.7l-7.8 7.8a1.7 1.7 0 0 1-2.3-2.3L14.5 7.8" />,
  signOut: <path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4M14 8l4 4-4 4M18 12H9" />,
  locked: <path d="M5 10.5h14v9.5H5zM8 10.5V7a4 4 0 0 1 8 0v3.5M12 14v2.5" />,
  info: <>
    {drum}
    <path d="M12 11v5.5M12 7.5v.3" />
  </>,
  help: <>
    {drum}
    <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5v.4M12 16.5v.3" />
  </>,
  success: <>
    {drum}
    <path d="M8 12.5l3 3 5-6" />
  </>,
  error: <>
    {drum}
    <path d="M12 7.5v5.5M12 16v.3" />
  </>,
  // A wooden chest.
  inbox: <path d="M4 9h16v11H4zM4 9l2.5-4.5h11L20 9M4 13h5l1 2h4l1-2h5" />,
  history: <path d="M4 12a8 8 0 1 0 2.4-5.7M4 4v4h4M12 8v4l3 2" />,
  time: <>
    {drum}
    <path d="M12 7v5l3.5 2" />
  </>,
  document: <>
    {decree}
    <path d="M9 9h6M9 12.5h6M9 16h4" />
  </>,
  notFound: <>
    {decree}
    <path d="M10 10a2 2 0 1 1 3 1.7c-.7.4-1 .9-1 1.6M12 16.5v.3" />
  </>,
  date: <>
    <rect x="4" y="5" width="16" height="15" rx="2" />
    <path d="M4 10h16M8 3v4M16 3v4" />
    {star(6, 2.5, 1.1, 12, 15)}
  </>,
  dateRange: <>
    <rect x="4" y="5" width="16" height="15" rx="2" />
    <path d="M4 10h16M8 3v4M16 3v4M8 14h2M14 14h2M8 17h2M14 17h2" />
  </>,
  bot: <path d="M5.5 8.5h13v10h-13zM12 8.5V5M10 5h4M9.5 12.5v1.5M14.5 12.5v1.5M10 16h4" />,
  // A lotus pond under the sun.
  image: <>
    <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
    <path d="M3.5 15.5c3-1 5-1 8.5 0s5.5 1 8.5 0M9 15c0-2.5 1.5-4.5 3-5 1.5.5 3 2.5 3 5" />
    <circle cx="16.5" cy="8.5" r="1.5" />
  </>,
  // An oil lamp.
  callout: <path d="M9 21h6M10 21l.5-4h3l.5 4M8 17h8l-1-6H9zM12 11V8.5M12 8.5c-1-1-1-2.5 0-4 1 1.5 1 3 0 4z" />,
  loading: <>
    <path d="M12 3.5a8.5 8.5 0 1 1-8.5 8.5" />
    {star(6, 2.5, 1.1)}
  </>,
};

export default drawnSet(shapes, { width: 1.5 });
