import { describe, expect, it } from 'vitest';
import { layeredLayout, nearestSpaced, replaceStatus, unreachableStatuses } from './workflowGraph';

describe('layeredLayout', () => {
  const groups: Record<string, number> = { backlog: 0, todo: 0, doing: 1, review: 1, done: 2, cancelled: 2 };
  const layout = (statusIds: string[], initial: string, transitions: { fromStatusId: string | null; toStatusId: string }[]) =>
    Object.fromEntries(layeredLayout(statusIds, initial, transitions, (id) => groups[id] ?? 0));

  it('puts a status that feeds the start to its left, and done statuses last', () => {
    // The Docmost workflow: Backlog leads into the start, the rest only come from Any status.
    const places = layout(['backlog', 'todo', 'doing', 'done', 'cancelled'], 'todo', [
      { fromStatusId: null, toStatusId: 'done' },
      { fromStatusId: 'todo', toStatusId: 'doing' },
      { fromStatusId: null, toStatusId: 'doing' },
      { fromStatusId: null, toStatusId: 'cancelled' },
      { fromStatusId: 'backlog', toStatusId: 'todo' },
      { fromStatusId: null, toStatusId: 'todo' },
      { fromStatusId: null, toStatusId: 'backlog' },
    ]);

    expect(places.backlog.layer).toBe(0);
    expect(places.todo.layer).toBe(1);
    expect(places.doing.layer).toBe(2);
    expect(places.done.layer).toBe(3);
    expect(places.cancelled.layer).toBe(3);
    // One straight line along the chain.
    expect([places.backlog.row, places.todo.row, places.doing.row]).toEqual([0, 0, 0]);
    expect(places.done.row).not.toBe(places.cancelled.row);
  });

  it('keeps loops from pushing the start to the right', () => {
    const places = layout(['todo', 'doing', 'review', 'done'], 'todo', [
      { fromStatusId: 'todo', toStatusId: 'doing' },
      { fromStatusId: 'doing', toStatusId: 'review' },
      { fromStatusId: 'review', toStatusId: 'doing' },
      { fromStatusId: 'review', toStatusId: 'done' },
      { fromStatusId: 'doing', toStatusId: 'todo' },
    ]);

    expect([places.todo.layer, places.doing.layer, places.review.layer, places.done.layer]).toEqual([0, 1, 2, 3]);
  });

  it('stacks branches without overlap and centres their parent between them', () => {
    const places = layout(['todo', 'doing', 'review', 'done'], 'todo', [
      { fromStatusId: 'todo', toStatusId: 'doing' },
      { fromStatusId: 'todo', toStatusId: 'review' },
      { fromStatusId: 'doing', toStatusId: 'done' },
      { fromStatusId: 'review', toStatusId: 'done' },
    ]);

    expect(places.doing.layer).toBe(1);
    expect(places.review.layer).toBe(1);
    expect(Math.abs(places.doing.row - places.review.row)).toBeGreaterThanOrEqual(1);
    expect(places.todo.row).toBeCloseTo((places.doing.row + places.review.row) / 2);
    expect(places.done.row).toBeCloseTo(places.todo.row);
  });

  it('places statuses without any path by their group', () => {
    const places = layout(['todo', 'doing', 'done'], 'todo', [
      { fromStatusId: null, toStatusId: 'doing' },
      { fromStatusId: null, toStatusId: 'done' },
    ]);

    expect([places.todo.layer, places.doing.layer, places.done.layer]).toEqual([0, 1, 2]);
  });
});

describe('nearestSpaced', () => {
  it('keeps order and spacing while staying close to the wanted rows', () => {
    expect(nearestSpaced([0, 0, 0])).toEqual([-1, 0, 1]);
    expect(nearestSpaced([0, 5])).toEqual([0, 5]);
  });
});

describe('replaceStatus', () => {
  const graph = {
    initialStatusId: 'a',
    statusIds: ['a', 'b', 'c'],
    transitions: [
      { fromStatusId: 'a', toStatusId: 'b' },
      { fromStatusId: 'b', toStatusId: 'c' },
    ],
  };

  it('moves transitions onto the replacement and drops the loops that leaves', () => {
    expect(replaceStatus(graph, 'a', 'b')).toEqual({
      initialStatusId: 'b',
      statusIds: ['b', 'c'],
      transitions: [{ fromStatusId: 'b', toStatusId: 'c' }],
    });
  });

  it('takes the transitions with it when nothing replaces the status', () => {
    expect(replaceStatus(graph, 'c', null).transitions).toEqual([{ fromStatusId: 'a', toStatusId: 'b' }]);
  });
});

describe('unreachableStatuses', () => {
  it('follows transitions from the start', () => {
    expect(
      unreachableStatuses({
        initialStatusId: 'a',
        statusIds: ['a', 'b', 'c'],
        transitions: [{ fromStatusId: 'a', toStatusId: 'b' }],
      }),
    ).toEqual(['c']);
  });

  it('counts Any status transitions', () => {
    expect(
      unreachableStatuses({
        initialStatusId: 'a',
        statusIds: ['a', 'b'],
        transitions: [{ fromStatusId: null, toStatusId: 'b' }],
      }),
    ).toEqual([]);
  });
});
