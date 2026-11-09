import type { IconSet } from '../../components/icons';
import { drawnIcon } from './drawn';

/** A five-petal sakura blossom around a point. */
const blossom = (cx: number, cy: number, r: number) => (
  <>
    {[0, 72, 144, 216, 288].map((angle) => (
      <ellipse key={angle} cx={cx} cy={cy - r} rx={r * 0.55} ry={r * 0.8} transform={`rotate(${angle} ${cx} ${cy})`} />
    ))}
    <circle cx={cx} cy={cy} r={r * 0.25} fill="currentColor" stroke="none" />
  </>
);

/**
 * The Japanese theme's own icons: fine lines after shoji screens, torii, enso, kamon crests, waves and sakura.
 * They cover the sidebar, issue types, priorities and the top bar; the rest come from the theme's library set.
 */
const japanese: IconSet = {
  overview: drawnIcon('overview', <>
    <rect x="4" y="3" width="16" height="18" rx="0.5" />
    <path d="M12 3v18M4 9h16M4 15h16" />
  </>, 1.25),
  list: drawnIcon('list', <path d="M6 7h12M7.5 12h9M5 17h14" />, 1.25),
  board: drawnIcon('board', <path d="M3 6.5c4 1 14 1 18 0M5 10h14M7.5 7v13.5M16.5 7v13.5M12 10V7.2" />, 1.25),
  epic: drawnIcon('epic', <>
    <path d="M3 17c2-6 7-9.5 12-8.5-3 1-4 4-2 6 2 2 5 1 6-1 0 4-4 7.5-9 6.5" />
    <path d="M3 20.5h18" />
  </>, 1.25),
  task: drawnIcon('task', <>
    <path d="M18.6 7.2A8 8 0 1 0 19.4 15" />
    <path d="M8.5 12l2.5 2.5 5-5" />
  </>, 1.25),
  people: drawnIcon('people', <>
    <circle cx="8.5" cy="7" r="2.5" />
    <circle cx="15.5" cy="7" r="2.5" />
    <path d="M6 20.5c0-5 1-8.5 2.5-8.5s2.5 3.5 2.5 8.5zM13 20.5c0-5 1-8.5 2.5-8.5s2.5 3.5 2.5 8.5z" />
  </>, 1.25),
  settings: drawnIcon('settings', <>
    <circle cx="12" cy="12" r="8.5" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M12 3.5v6M12 14.5v6M4.6 7.75l5.2 3M14.2 13.25l5.2 3M4.6 16.25l5.2-3M14.2 10.75l5.2-3" />
  </>, 1.25),
  story: drawnIcon('story', <>
    <path d="M9 3h6v18l-3-2.2L9 21z" />
    <path d="M12 7v6" />
  </>, 1.25),
  bug: drawnIcon('bug', <>
    <ellipse cx="12" cy="14" rx="5" ry="6" />
    <path d="M12 8v12M12 8L9.5 3.5M12 8l2.5-4.5M7 12H4M7 16H4M17 12h3M17 16h3" />
  </>, 1.25),
  subtask: drawnIcon('subtask', <>
    <path d="M4 4.5V11c0 3 2 5 5 5h4.5" />
    {blossom(18, 16, 2.6)}
  </>, 1.25),
  priorityHighest: drawnIcon('priorityHighest', <path d="M4 12l8-6 8 6M4 18.5l8-6 8 6" />, 1.25),
  priorityHigh: drawnIcon('priorityHigh', <path d="M4 15.5l8-6 8 6" />, 1.25),
  priorityMedium: drawnIcon('priorityMedium', <path d="M5 9.5h14M5 14.5h14" />, 1.25),
  priorityLow: drawnIcon('priorityLow', <path d="M4 8.5l8 6 8-6" />, 1.25),
  priorityLowest: drawnIcon('priorityLowest', <path d="M4 5.5l8 6 8-6M4 12l8 6 8-6" />, 1.25),
  notifications: drawnIcon('notifications', <>
    <path d="M7 12.5a5 5 0 0 1 10 0v.5H7z" />
    <path d="M12 7.5V4M12 13v4" />
    <rect x="10" y="17" width="4" height="4" />
  </>, 1.25),
  search: drawnIcon('search', <>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="M15.5 15.5L21 21" />
  </>, 1.25),
  add: drawnIcon('add', <>
    <path d="M18.6 7.2A8 8 0 1 0 19.4 15" />
    <path d="M12 8v8M8 12h8" />
  </>, 1.25),
  assistant: drawnIcon('assistant', blossom(12, 12, 4.5), 1.25),
};

export default japanese;
