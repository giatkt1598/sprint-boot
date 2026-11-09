import type { IconSet } from '../../components/icons';
import { drawnIcon } from './drawn';

const dot = (x: number, y: number, r = 0.9) => <circle cx={x} cy={y} r={r} fill="currentColor" stroke="none" />;

/**
 * The Chinese theme's own icons, after lattice windows, square-holed coins, roof eaves, xiangyun clouds and seals.
 * They cover the sidebar, issue types, priorities and the top bar; the rest come from the theme's library set.
 */
const chinese: IconSet = {
  overview: drawnIcon('overview', <>
    <rect x="3.5" y="3.5" width="17" height="17" rx="1" />
    <path d="M12 3.5v17M3.5 12h17" />
    <path d="M6.5 6.5h2.5v2.5M17.5 6.5H15v2.5M6.5 17.5h2.5V15M17.5 17.5H15V15" />
  </>),
  list: drawnIcon('list', <>
    <path d="M5 4v16M19 4v16" />
    <path d="M8.5 7.5h7M8.5 12h7M8.5 16.5h7" />
  </>),
  board: drawnIcon('board', <path d="M3 5.5l6-1.5v16l-6 1.5zM9 4l6 1.5v16L9 20zM15 5.5l6-1.5v16l-6 1.5z" />),
  epic: drawnIcon('epic', <>
    <path d="M5 16c-2.5 0-2.5-4.5.5-4.5 0-4.5 6.5-5.5 7.5-1.5 1-3.5 6.5-2.5 6.5 1 3 0 3 5 0 5z" />
    <path d="M9.5 16c0-2 3-2 3 0" />
  </>),
  task: drawnIcon('task', <>
    <rect x="4.5" y="4.5" width="15" height="15" rx="2" />
    <path d="M8.5 12l2.5 2.5 4.5-5" />
  </>),
  people: drawnIcon('people', <>
    <circle cx="9" cy="8" r="2.5" />
    <circle cx="16.5" cy="9" r="2" />
    <path d="M9 5.5V4M16.5 7V5.8" />
    <path d="M4 20c0-3.5 2.2-6 5-6s5 2.5 5 6M14.5 14.5c3 0 5.5 2 5.5 5.5" />
  </>),
  settings: drawnIcon('settings', <>
    <circle cx="12" cy="12" r="8.5" />
    <rect x="9.5" y="9.5" width="5" height="5" />
    <path d="M12 3.5v2.5M12 18v2.5M3.5 12H6M18 12h2.5" />
  </>),
  story: drawnIcon('story', <>
    <path d="M5.5 5h13M5 19h14M7 5v14M17 5v14" />
    <path d="M10 9h4M10 12.5h4" />
    <path d="M12 5L10 2.5M12 5l2-2.5" />
  </>),
  bug: drawnIcon('bug', <>
    <ellipse cx="12" cy="13.5" rx="5" ry="6" />
    <path d="M12 7.5v12M9 5l1.5 2.5M15 5l-1.5 2.5M7 11.5H4M7 15.5H4M17 11.5h3M17 15.5h3" />
  </>),
  subtask: drawnIcon('subtask', <>
    <path d="M4 4.5c0 6 3 10.5 10 10.5" />
    <circle cx="17" cy="15" r="3" />
    {dot(17, 15)}
    <path d="M7 9.5l-2 1" />
  </>),
  priorityHighest: drawnIcon('priorityHighest', <path d="M4 11.5c3 0 5-2 8-5 3 3 5 5 8 5M4 18c3 0 5-2 8-5 3 3 5 5 8 5" />),
  priorityHigh: drawnIcon('priorityHigh', <path d="M4 15c3 0 5-2 8-5 3 3 5 5 8 5" />),
  priorityMedium: drawnIcon('priorityMedium', <>
    <path d="M5 9.5h14M5 14.5h14" />
    {dot(12, 9.5, 1)}
  </>),
  priorityLow: drawnIcon('priorityLow', <path d="M4 9c3 0 5 2 8 5 3-3 5-5 8-5" />),
  priorityLowest: drawnIcon('priorityLowest', <path d="M4 6c3 0 5 2 8 5 3-3 5-5 8-5M4 12.5c3 0 5 2 8 5 3-3 5-5 8-5" />),
  notifications: drawnIcon('notifications', <>
    <path d="M9.5 5h5M10.5 5V3.5h3V5" />
    <path d="M8.5 5c0 5.5-.8 8.5-2 10.5h11c-1.2-2-2-5-2-10.5" />
    <path d="M5 15.5h14" />
    <circle cx="12" cy="18.5" r="1.3" />
  </>),
  search: drawnIcon('search', <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <rect x="9" y="9" width="3" height="3" />
    <path d="M15.5 15.5L20.5 20.5" />
  </>),
  add: drawnIcon('add', <>
    <circle cx="12" cy="12" r="8.5" />
    <path d="M12 8v8M8 12h8" />
  </>),
  assistant: drawnIcon('assistant', <>
    <path d="M12 2v2M9 4h6M9 20h6M12 20v2" />
    <ellipse cx="12" cy="12" rx="6" ry="8" />
    <path d="M12 4v16M8.5 5.5c-1.5 4-1.5 9 0 13M15.5 5.5c1.5 4 1.5 9 0 13" />
  </>),
};

export default chinese;
