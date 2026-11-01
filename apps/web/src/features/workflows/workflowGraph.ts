export interface DraftTransition {
  /** Only for the editor; the server has no use for it. */
  uid: number;
  name: string;
  /** Null means Any status. */
  fromStatusId: string | null;
  toStatusId: string;
  rules: unknown[];
}

interface Graph {
  initialStatusId: string;
  statusIds: string[];
  transitions: Pick<DraftTransition, 'fromStatusId' | 'toStatusId'>[];
}

/** Statuses no issue could ever reach from the start, the same rule the server applies on publish. */
export function unreachableStatuses({ initialStatusId, statusIds, transitions }: Graph) {
  const reached = new Set([initialStatusId]);
  const queue = [initialStatusId];
  while (queue.length > 0) {
    const current = queue.pop();
    for (const transition of transitions) {
      if ((transition.fromStatusId === null || transition.fromStatusId === current) && !reached.has(transition.toStatusId)) {
        reached.add(transition.toStatusId);
        queue.push(transition.toStatusId);
      }
    }
  }
  return statusIds.filter((id) => !reached.has(id));
}
