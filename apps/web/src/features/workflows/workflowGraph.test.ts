import { describe, expect, it } from 'vitest';
import { unreachableStatuses } from './workflowGraph';

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
