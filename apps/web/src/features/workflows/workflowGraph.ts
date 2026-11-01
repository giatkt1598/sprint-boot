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

/** What the server does to a version when a status is deleted, so the open draft stays in step. */
export function replaceStatus<T extends Graph>(graph: T, statusId: string, replacementId: string | null): T {
  const swap = (id: string) => (id === statusId ? (replacementId ?? id) : id);
  const statusIds = [...new Set(graph.statusIds.map(swap))].filter((id) => id !== statusId);
  return {
    ...graph,
    statusIds,
    initialStatusId: graph.initialStatusId === statusId ? (replacementId ?? statusIds[0] ?? '') : graph.initialStatusId,
    transitions: graph.transitions
      .filter((item) => replacementId !== null || (item.fromStatusId !== statusId && item.toStatusId !== statusId))
      .map((item) => ({ ...item, fromStatusId: item.fromStatusId && swap(item.fromStatusId), toStatusId: swap(item.toStatusId) }))
      .filter((item) => item.fromStatusId !== item.toStatusId),
  };
}

/**
 * Columns follow the path from the start status and rows fill each column. Statuses only
 * reached from Any status have no path, so they start new columns in the order given.
 */
export function arrangeStatuses(order: string[], transitions: Graph['transitions']) {
  const places = new Map<string, { column: number; row: number }>();
  const rows = new Map<number, number>();
  const place = (id: string, column: number) => {
    const row = rows.get(column) ?? 0;
    rows.set(column, row + 1);
    places.set(id, { column, row });
  };

  for (const seed of order) {
    if (places.has(seed)) {
      continue;
    }
    place(seed, rows.size);
    const queue = [seed];
    while (queue.length > 0) {
      const current = queue.shift()!;
      for (const { fromStatusId, toStatusId } of transitions) {
        if (fromStatusId === current && order.includes(toStatusId) && !places.has(toStatusId)) {
          place(toStatusId, places.get(current)!.column + 1);
          queue.push(toStatusId);
        }
      }
    }
  }
  return places;
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
