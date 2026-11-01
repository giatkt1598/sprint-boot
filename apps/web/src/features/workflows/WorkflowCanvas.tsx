import ButtonBase from '@mui/material/ButtonBase';
import {
  BaseEdge,
  EdgeLabelRenderer,
  Handle,
  MarkerType,
  Position,
  getStraightPath,
  type Edge,
  type EdgeProps,
  type Node,
  type NodeProps,
} from '@xyflow/react';
import { Badge } from '../../components/Badge';
import { font, radius, sem, type SemanticToken } from '../../theme/tokens';

export const ANY = 'any';

const PARALLEL_GAP = 28;
const SIDES = [
  { id: 'left', position: Position.Left },
  { id: 'right', position: Position.Right },
  { id: 'top', position: Position.Top },
  { id: 'bottom', position: Position.Bottom },
];
const DOTS: Record<number, SemanticToken> = { 0: 'status-todo-text', 1: 'status-progress-text', 2: 'status-done-text' };

export interface StatusNodeData extends Record<string, unknown> {
  name: string;
  category: number;
  isStart: boolean;
  isBroken: boolean;
  startLabel: string;
}

export interface AnyNodeData extends Record<string, unknown> {
  label: string;
  active: boolean;
}

export interface TransitionEdgeData extends Record<string, unknown> {
  /** Sideways shift that keeps transitions between the same two statuses apart. */
  offset: number;
  dashed: boolean;
  onSelect: () => void;
}

const handles = SIDES.map((side) => (
  <Handle key={side.id} id={side.id} type="source" position={side.position} className="opacity-0 group-hover:opacity-100" />
));

function StatusNode({ data, selected }: NodeProps<Node<StatusNodeData>>) {
  const emphasised = data.isBroken || selected;

  return (
    <div
      data-testid={`status-node-${data.name}`}
      className="group relative flex items-center gap-2 px-3"
      style={{
        width: 152,
        height: 44,
        fontSize: font.size.md,
        fontWeight: font.weight.medium,
        color: sem('text-primary'),
        backgroundColor: sem('bg-surface'),
        border: `${emphasised ? 2 : 1}px solid ${sem(data.isBroken ? 'feedback-danger-text' : selected ? 'border-focus' : 'border-strong')}`,
        borderRadius: radius.control,
      }}
    >
      {handles}
      {data.isStart && (
        <span className="absolute left-0" style={{ top: -32 }}>
          <Badge tone="success">{data.startLabel}</Badge>
        </span>
      )}
      <span aria-hidden className="shrink-0" style={{ width: 8, height: 8, borderRadius: '50%', backgroundColor: sem(DOTS[data.category] ?? 'status-todo-text') }} />
      <span className="truncate">{data.name}</span>
    </div>
  );
}

function AnyNode({ data }: NodeProps<Node<AnyNodeData>>) {
  return (
    <div
      className="group relative flex items-center justify-center"
      style={{
        width: 152,
        height: 44,
        fontSize: font.size.md,
        fontWeight: font.weight.medium,
        color: sem('text-secondary'),
        backgroundColor: sem('bg-surface'),
        border: `1px dashed ${sem(data.active ? 'border-focus' : 'border-strong')}`,
        borderRadius: radius.control,
      }}
    >
      {handles}
      {data.label}
    </div>
  );
}

function TransitionEdge({ id, sourceX, sourceY, targetX, targetY, label, selected, markerEnd, data }: EdgeProps<Edge<TransitionEdgeData>>) {
  const offset = data?.offset ?? 0;
  const length = Math.hypot(targetX - sourceX, targetY - sourceY) || 1;
  const shiftX = (-(targetY - sourceY) / length) * offset;
  const shiftY = ((targetX - sourceX) / length) * offset;
  const [path, labelX, labelY] = getStraightPath({
    sourceX: sourceX + shiftX,
    sourceY: sourceY + shiftY,
    targetX: targetX + shiftX,
    targetY: targetY + shiftY,
  });
  const stroke = sem(selected ? 'border-focus' : 'text-secondary');

  return (
    <>
      <BaseEdge id={id} path={path} markerEnd={markerEnd} style={{ stroke, strokeWidth: selected ? 2 : 1, strokeDasharray: data?.dashed ? '4 4' : undefined }} />
      {label && <EdgeLabelRenderer>
        <ButtonBase
          className="nodrag nopan absolute px-2"
          aria-pressed={Boolean(selected)}
          onClick={data?.onSelect}
          sx={{
            transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
            pointerEvents: 'all',
            height: 24,
            fontSize: font.size.xs,
            color: sem(selected ? 'text-accent' : 'text-secondary'),
            backgroundColor: sem('bg-surface'),
            border: `1px solid ${sem(selected ? 'border-focus' : 'border-default')}`,
            borderRadius: `${radius.control}px`,
          }}
        >
          {label}
        </ButtonBase>
      </EdgeLabelRenderer>}
    </>
  );
}

export const nodeTypes = { status: StatusNode, any: AnyNode };
export const edgeTypes = { transition: TransitionEdge };
export const arrow = { type: MarkerType.ArrowClosed, color: sem('text-secondary') };

/** Leaves through the side facing the other status, so lines run the way the designer drew them. */
export const facingSides = (from: { x: number; y: number }, to: { x: number; y: number }) =>
  Math.abs(to.x - from.x) >= Math.abs(to.y - from.y)
    ? to.x >= from.x
      ? { sourceHandle: 'right', targetHandle: 'left' }
      : { sourceHandle: 'left', targetHandle: 'right' }
    : to.y >= from.y
      ? { sourceHandle: 'bottom', targetHandle: 'top' }
      : { sourceHandle: 'top', targetHandle: 'bottom' };

export const parallelOffset = (index: number, count: number, reversed: boolean) =>
  (index - (count - 1) / 2) * PARALLEL_GAP * (reversed ? -1 : 1);
