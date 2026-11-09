import type { IconSet } from '../../components/icons';
import { drawnIcon } from './drawn';

/** A pointed gothic arch over a baseline, rising (up) or falling (down) at a height. */
const arch = (y: number, up: boolean) => (up ? `M5 ${y}c3-1 6-3 7-6 1 3 4 5 7 6` : `M5 ${y - 6}c3 1 6 3 7 6 1-3 4-5 7-6`);

/**
 * The European theme's own icons, after rose windows, gothic arches, crowns, wax seals, quills and the fleur-de-lis.
 * They cover the sidebar, issue types, priorities and the top bar; the rest come from the theme's library set.
 */
const european: IconSet = {
  overview: drawnIcon('overview', <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M12 3.5v6M12 14.5v6M3.5 12h6M14.5 12h6M6 6l4.2 4.2M13.8 13.8L18 18M18 6l-4.2 4.2M10.2 13.8L6 18" />
  </>),
  list: drawnIcon('list', <>
    <rect x="4" y="5" width="4.5" height="4.5" rx="0.5" />
    <path d="M11.5 6h8.5M11.5 9h8.5M4 13.5h16M4 17.5h16" />
  </>),
  board: drawnIcon('board', <path d="M4 20V9.5a2.5 2.5 0 0 1 5 0V20M9.5 20V7.5a2.5 2.5 0 0 1 5 0V20M15 20V9.5a2.5 2.5 0 0 1 5 0V20M3 20h18" />),
  epic: drawnIcon('epic', <>
    <path d="M4.5 17.5L3.5 8l5 4 3.5-7 3.5 7 5-4-1 9.5z" />
    <path d="M4.5 20.5h15" />
  </>),
  task: drawnIcon('task', <>
    <circle cx="12" cy="11" r="7.5" />
    <path d="M8.5 11l2.5 2.5 4.5-5" />
    <path d="M9 18l-1.5 3.5M15 18l1.5 3.5" />
  </>),
  people: drawnIcon('people', <>
    <circle cx="9" cy="8" r="3" />
    <circle cx="16.5" cy="9" r="2.5" />
    <path d="M3 20c0-4 2.7-6 6-6s6 2 6 6M15.5 14.5c3 0 5.5 2 5.5 5.5" />
  </>),
  settings: drawnIcon('settings', <>
    <circle cx="12" cy="12" r="7.5" />
    <path d="M12 2.5l1.4 8.1L12 12l-1.4-1.4zM12 21.5l-1.4-8.1L12 12l1.4 1.4zM2.5 12l8.1-1.4L12 12l-1.4 1.4zM21.5 12l-8.1 1.4L12 12l1.4-1.4z" />
  </>),
  story: drawnIcon('story', <>
    <path d="M5 4.5h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3z" />
    <path d="M5 17.5a3 3 0 0 1 3-3h11M9 8.5h6" />
  </>),
  bug: drawnIcon('bug', <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M7.5 9.5c1.5-2 3-2.5 4.5-2.5s3 .5 4.5 2.5M7 12H4M7 16H4M17 12h3M17 16h3" />
  </>),
  subtask: drawnIcon('subtask', <>
    <path d="M20 4c-8 1-12 6-13 13l-2 3.5" />
    <path d="M20 4c-1 6-5 10-11 11" />
  </>),
  priorityHighest: drawnIcon('priorityHighest', <path d={`${arch(12, true)}${arch(18.5, true)}`} />),
  priorityHigh: drawnIcon('priorityHigh', <path d={arch(15.5, true)} />),
  priorityMedium: drawnIcon('priorityMedium', <path d="M5 9.5h14M5 14.5h14" />),
  priorityLow: drawnIcon('priorityLow', <path d={arch(15, false)} />),
  priorityLowest: drawnIcon('priorityLowest', <path d={`${arch(12, false)}${arch(18.5, false)}`} />),
  notifications: drawnIcon('notifications', <>
    <path d="M7 17v-6a5 5 0 0 1 10 0v6l2 2H5z" />
    <path d="M12 3v3" />
    <circle cx="12" cy="21" r="1" />
  </>),
  search: drawnIcon('search', <>
    <circle cx="10" cy="10" r="6" />
    <path d="M14.5 14.5L20 20M16 4c2 1 3 3 3 5" />
  </>),
  add: drawnIcon('add', <path d="M10 4h4l-1 6 6-1v4l-6-1 1 6h-4l1-6-6 1V9l6 1z" />),
  assistant: drawnIcon('assistant', <path d="M12 3c-2 3-2 6 0 9 2-3 2-6 0-9zM12 12c-2-3-7-3-7 1 0 2 2 3 3 2M12 12c2-3 7-3 7 1 0 2-2 3-3 2M8 15.5h8M12 12v9" />),
};

export default european;
