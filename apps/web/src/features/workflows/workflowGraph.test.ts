import { describe, expect, it } from 'vitest';
import { arrangeStatuses, replaceStatus, unreachableStatuses } from './workflowGraph';

describe('arrangeStatuses', () => {
  it('lays a path left to right and stacks branches', () => {
    const places = arrangeStatuses(['a', 'b', 'c', 'd'], [
      { fromStatusId: 'a', toStatusId: 'b' },
      { fromStatusId: 'a', toStatusId: 'c' },
      { fromStatusId: 'c', toStatusId: 'd' },
      { fromStatusId: 'd', toStatusId: 'a' },
    ]);

    expect(Object.fromEntries(places)).toEqual({
      a: { column: 0, row: 0 },
      b: { column: 1, row: 0 },
      c: { column: 1, row: 1 },
      d: { column: 2, row: 0 },
    });
  });

  it('gives statuses without a path their own columns', () => {
    const places = arrangeStatuses(['a', 'b'], [{ fromStatusId: null, toStatusId: 'b' }]);

    expect(places.get('b')).toEqual({ column: 1, row: 0 });
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
