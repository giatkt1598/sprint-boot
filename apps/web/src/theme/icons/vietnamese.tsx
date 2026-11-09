import type { IconSet } from '../../components/icons';
import { drawnIcon } from './drawn';

/** The star at the centre of a Đông Sơn drum, with as many points as asked. */
const star = (points: number, outer: number, inner: number) => {
  let d = '';
  for (let i = 0; i < points * 2; i++) {
    const r = i % 2 ? inner : outer;
    const a = (i / (points * 2)) * Math.PI * 2 - Math.PI / 2;
    d += `${i ? 'L' : 'M'}${(12 + Math.cos(a) * r).toFixed(2)} ${(12 + Math.sin(a) * r).toFixed(2)}`;
  }
  return <path d={`${d}Z`} />;
};

/** The upturned eaves of a đình roof, rising (up) or falling (down) at a height. */
const eave = (y: number, up: boolean) =>
  up ? `M3 ${y}c2 0 3-.8 4-1.8l5-4.2 5 4.2c1 1 2 1.8 4 1.8` : `M3 ${y - 6}c2 0 3 .8 4 1.8l5 4.2 5-4.2c1-1 2-1.8 4-1.8`;

/**
 * The Vietnamese theme's own icons, after the Đông Sơn drum, nón lá, lotus, bamboo, the gong and the lạc bird.
 * They cover the sidebar, issue types, priorities and the top bar; the rest come from the theme's library set.
 */
const vietnamese: IconSet = {
  overview: drawnIcon('overview', <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="5" />
    {star(8, 3.2, 1.3)}
  </>),
  list: drawnIcon('list', <path d="M4 7h16M4 12h16M4 17h16M10 6v2M15 11v2M8 16v2" />),
  board: drawnIcon('board', <path d="M6 3v18M12 3v18M18 3v18M5 8h2M11 13h2M17 9h2" />),
  epic: drawnIcon('epic', <>
    <path d="M12 19c-3-2-4-6-2-11 1 1 2 3 2 5 0-2 1-4 2-5 2 5 1 9-2 11z" />
    <path d="M12 19c-4 0-8-2-9-6 3-1 6 0 8 2M12 19c4 0 8-2 9-6-3-1-6 0-8 2" />
  </>),
  task: drawnIcon('task', <>
    <rect x="4.5" y="4.5" width="15" height="15" rx="3" />
    <path d="M8.5 12l2.5 2.5 4.5-5" />
  </>),
  people: drawnIcon('people', <>
    <path d="M3 10.5l5-5 5 5zM11 10.5l5-5 5 5z" />
    <path d="M5.5 21c0-5 1-8 2.5-8s2.5 3 2.5 8M13.5 21c0-5 1-8 2.5-8s2.5 3 2.5 8" />
  </>),
  settings: drawnIcon('settings', <>
    <circle cx="12" cy="12" r="8.5" />
    {star(12, 6, 3.4)}
  </>),
  story: drawnIcon('story', <>
    <path d="M3 15L12 4l9 11z" />
    <path d="M2 15h20M7.5 15l2.5 5M16.5 15L14 20" />
  </>),
  bug: drawnIcon('bug', <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M9.5 5l1.5 2.5M14.5 5L13 7.5M7 11.5H4M7 15.5H4M17 11.5h3M17 15.5h3" />
  </>),
  subtask: drawnIcon('subtask', <>
    <path d="M6 3v10c0 2.2 1.8 4 4 4h6" />
    <path d="M5 8h2M16 14l3 3-3 3" />
  </>),
  priorityHighest: drawnIcon('priorityHighest', <path d={`${eave(12, true)}${eave(18.5, true)}`} />),
  priorityHigh: drawnIcon('priorityHigh', <path d={eave(15.5, true)} />),
  priorityMedium: drawnIcon('priorityMedium', <path d="M5 9.5h14M5 14.5h14M5 8v3M19 8v3" />),
  priorityLow: drawnIcon('priorityLow', <path d={eave(15, false)} />),
  priorityLowest: drawnIcon('priorityLowest', <path d={`${eave(12, false)}${eave(18.5, false)}`} />),
  notifications: drawnIcon('notifications', <>
    <circle cx="12" cy="13.5" r="6.5" />
    <circle cx="12" cy="13.5" r="2" />
    <path d="M12 7V3.5M9 3.5h6" />
  </>),
  search: drawnIcon('search', <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="M15.5 15.5L21 21" />
    <path d="M10.5 8v5M8 10.5h5" />
  </>),
  add: drawnIcon('add', <>
    <circle cx="12" cy="12" r="8.5" />
    <path d="M12 8v8M8 12h8" />
  </>),
  assistant: drawnIcon('assistant', <path d="M3 13.5c4-1 7-4 9-8.5 1 4 4 6.5 9 6.5-4 2-8 3-12 3l-4 4.5" />),
};

export default vietnamese;
