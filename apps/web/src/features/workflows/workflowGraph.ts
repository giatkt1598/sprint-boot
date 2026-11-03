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

export interface LayerPlace {
  /** Column, counted from the left. */
  layer: number;
  /** Row inside the column; fractional when a status lines up between two others. */
  row: number;
}

const DONE = 2;
const IN_PROGRESS = 1;

/**
 * A layered (Sugiyama style) layout, as database diagram tools draw them: every transition points
 * to the right except the few that close a loop, columns are as few as the paths allow, rows are
 * ordered to cut crossings and then aligned so a chain of statuses reads as one straight line.
 * Statuses only reached from Any status go where their group belongs: to do left, done right.
 */
export function layeredLayout(
  statusIds: string[],
  initialStatusId: string,
  transitions: Graph['transitions'],
  categoryOf: (id: string) => number,
): Map<string, LayerPlace> {
  const nodes = [...new Set(statusIds)];
  const known = new Set(nodes);
  const pairs = new Map<string, [string, string]>();
  for (const { fromStatusId, toStatusId } of transitions) {
    if (fromStatusId && fromStatusId !== toStatusId && known.has(fromStatusId) && known.has(toStatusId)) {
      pairs.set(`${fromStatusId}>${toStatusId}`, [fromStatusId, toStatusId]);
    }
  }
  const outgoing = new Map(nodes.map((id) => [id, [] as string[]]));
  const incoming = new Map(nodes.map((id) => [id, 0]));
  for (const [from, to] of pairs.values()) {
    outgoing.get(from)!.push(to);
    incoming.set(to, incoming.get(to)! + 1);
  }

  // Loops are broken where a depth-first walk from the start meets a status still on its path.
  const dag = new Map<string, [string, string]>();
  const state = new Map<string, 'open' | 'closed'>();
  const discovered: string[] = [];
  const visit = (id: string) => {
    state.set(id, 'open');
    discovered.push(id);
    for (const next of outgoing.get(id)!) {
      const seen = state.get(next);
      const edge: [string, string] = seen === 'open' ? [next, id] : [id, next];
      dag.set(`${edge[0]}>${edge[1]}`, edge);
      if (!seen) {
        visit(next);
      }
    }
    state.set(id, 'closed');
  };
  const roots = [...(known.has(initialStatusId) ? [initialStatusId] : []), ...nodes.filter((id) => incoming.get(id) === 0), ...nodes];
  for (const root of roots) {
    if (!state.has(root)) {
      visit(root);
    }
  }
  const edges = [...dag.values()];
  const linked = new Set(edges.flat());

  // Longest path from the sources, so every kept transition points right.
  const layer = new Map<string, number>();
  const remaining = new Map(nodes.map((id) => [id, 0]));
  for (const [, to] of edges) {
    remaining.set(to, remaining.get(to)! + 1);
  }
  const queue = discovered.filter((id) => linked.has(id) && remaining.get(id) === 0);
  queue.forEach((id) => layer.set(id, 0));
  while (queue.length > 0) {
    const current = queue.shift()!;
    for (const [from, to] of edges) {
      if (from === current) {
        layer.set(to, Math.max(layer.get(to) ?? 0, layer.get(current)! + 1));
        remaining.set(to, remaining.get(to)! - 1);
        if (remaining.get(to) === 0) {
          queue.push(to);
        }
      }
    }
  }

  // Finished statuses that lead nowhere line up in the last column, as the end of the flow.
  const sinks = nodes.filter((id) => linked.has(id) && categoryOf(id) === DONE && !edges.some(([from]) => from === id));
  const others = nodes.filter((id) => linked.has(id) && !sinks.includes(id));
  const last = Math.max(0, ...others.map((id) => layer.get(id)! + 1), ...sinks.map((id) => layer.get(id)!));
  sinks.forEach((id) => layer.set(id, last));
  const widest = Math.max(0, ...nodes.filter((id) => linked.has(id)).map((id) => layer.get(id)!));
  for (const id of nodes.filter((item) => !linked.has(item))) {
    const category = categoryOf(id);
    const done = linked.size === 0 ? 2 : sinks.length > 0 ? last : widest + 1;
    layer.set(id, category === DONE ? done : category === IN_PROGRESS ? Math.max(1, Math.ceil(widest / 2)) : 0);
  }

  // Long transitions pass through placeholder slots, so they get a lane of their own.
  const columns: string[][] = [];
  const add = (id: string) => (columns[layer.get(id)!] ??= []).push(id);
  [...discovered, ...nodes.filter((id) => !discovered.includes(id))].forEach(add);
  const links: [string, string][] = [];
  for (const [from, to] of edges) {
    let previous = from;
    for (let step = layer.get(from)! + 1; step < layer.get(to)!; step++) {
      const lane = `lane:${from}>${to}:${step}`;
      layer.set(lane, step);
      add(lane);
      links.push([previous, lane]);
      previous = lane;
    }
    links.push([previous, to]);
  }
  for (let index = 0; index < columns.length; index++) {
    columns[index] ??= [];
  }

  const before = new Map<string, string[]>();
  const after = new Map<string, string[]>();
  for (const [from, to] of links) {
    after.set(from, [...(after.get(from) ?? []), to]);
    before.set(to, [...(before.get(to) ?? []), from]);
  }

  // Barycenter sweeps: each status moves toward the middle of its neighbours in the next column over.
  const position = new Map<string, number>();
  columns.forEach((column) => column.forEach((id, index) => position.set(id, index)));
  const mean = (values: number[], fallback: number) =>
    values.length === 0 ? fallback : values.reduce((sum, value) => sum + value, 0) / values.length;
  const sweep = (column: string[], neighbours: Map<string, string[]>) => {
    const weights = new Map(column.map((id) => [id, mean((neighbours.get(id) ?? []).map((item) => position.get(item)!), position.get(id)!)]));
    column.sort((first, second) => weights.get(first)! - weights.get(second)! || position.get(first)! - position.get(second)!);
    column.forEach((id, index) => position.set(id, index));
  };
  for (let round = 0; round < 6; round++) {
    for (let index = 1; index < columns.length; index++) {
      sweep(columns[index], before);
    }
    for (let index = columns.length - 2; index >= 0; index--) {
      sweep(columns[index], after);
    }
  }

  // Rows: as close to the neighbours as the order allows, at least one row apart.
  const row = new Map(position);
  const align = (column: string[], neighbours: Map<string, string[]>) => {
    const wanted = column.map((id) => mean((neighbours.get(id) ?? []).map((item) => row.get(item)!), row.get(id)!));
    nearestSpaced(wanted).forEach((value, index) => row.set(column[index], value));
  };
  for (let round = 0; round < 4; round++) {
    for (let index = 1; index < columns.length; index++) {
      align(columns[index], before);
    }
    for (let index = columns.length - 2; index >= 0; index--) {
      align(columns[index], after);
    }
  }

  const top = Math.min(...nodes.map((id) => row.get(id)!));
  return new Map(nodes.map((id) => [id, { layer: layer.get(id)!, row: row.get(id)! - top }]));
}

/**
 * The values closest to the wanted ones (least squares) that keep their order and stay one apart:
 * pool adjacent violators on wanted[i] - i, then add i back.
 */
export function nearestSpaced(wanted: number[]) {
  const blocks: { sum: number; count: number }[] = [];
  wanted.forEach((value, index) => {
    blocks.push({ sum: value - index, count: 1 });
    while (blocks.length > 1) {
      const last = blocks[blocks.length - 1];
      const previous = blocks[blocks.length - 2];
      if (previous.sum / previous.count <= last.sum / last.count) {
        break;
      }
      previous.sum += last.sum;
      previous.count += last.count;
      blocks.pop();
    }
  });
  return blocks.flatMap((block) => Array<number>(block.count).fill(block.sum / block.count)).map((value, index) => value + index);
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
