import MuiTable from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import { ArrowDown } from 'lucide-react';
import { useCallback, useMemo, type Key, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { sem } from '../theme/tokens';

export type SortDirection = 'asc' | 'desc';

export interface TableSort {
  key: string;
  direction: SortDirection;
}

export interface TableColumn<Row> {
  key: string;
  header: ReactNode;
  render: (row: Row) => ReactNode;
  /** The header becomes a button that sorts by this column; the table only reports the click. */
  sortable?: boolean;
}

export interface TableProps<Row> {
  label: string;
  columns: TableColumn<Row>[];
  rows: Row[];
  rowKey: (row: Row) => Key;
  /** Every cell on one line; long text should be cut with ellipsis by the column itself. */
  nowrap?: boolean;
  sort?: TableSort | null;
  onSort?: (key: string) => void;
}

const SortIcon = (props: { className?: string }) => <ArrowDown size={14} strokeWidth={1.5} aria-hidden {...props} />;

export function Table<Row>({ label, columns, rows, rowKey, nowrap, sort, onSort }: TableProps<Row>) {
  const cell = nowrap ? { whiteSpace: 'nowrap' } : undefined;
  return (
    <MuiTable aria-label={label} size="small">
      <TableHead>
        <TableRow>
          {columns.map((column) => {
            const active = sort?.key === column.key;
            return (
              <TableCell key={column.key} sx={cell} sortDirection={active ? sort.direction : false}>
                {column.sortable && onSort ? (
                  <TableSortLabel
                    active={active}
                    direction={active ? sort.direction : 'asc'}
                    onClick={() => onSort(column.key)}
                    IconComponent={SortIcon}
                  >
                    {column.header}
                  </TableSortLabel>
                ) : (
                  column.header
                )}
              </TableCell>
            );
          })}
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={rowKey(row)} sx={{ '&:hover': { backgroundColor: sem('bg-hover') } }}>
            {columns.map((column) => (
              <TableCell key={column.key} sx={cell}>{column.render(row)}</TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </MuiTable>
  );
}

/**
 * The sort of a list page, kept in the address (`sort`, `dir`) so it survives a reload and can be shared.
 * A new column starts ascending; choosing the same column again flips the direction.
 */
export function useTableSort() {
  const [params, setParams] = useSearchParams();
  const key = params.get('sort');
  const direction: SortDirection = params.get('dir') === 'desc' ? 'desc' : 'asc';
  const sort = useMemo<TableSort | null>(() => (key ? { key, direction } : null), [key, direction]);

  const toggle = useCallback(
    (next: string) =>
      setParams(
        (previous) => {
          const updated = new URLSearchParams(previous);
          updated.set('dir', previous.get('sort') === next && previous.get('dir') !== 'desc' ? 'desc' : 'asc');
          updated.set('sort', next);
          return updated;
        },
        { replace: true },
      ),
    [setParams],
  );

  return { sort, toggle };
}

/** Compares values for a client-side sort: text by locale, empty values always last. */
export function compareValues(a: string | number | null | undefined, b: string | number | null | undefined, direction: SortDirection) {
  const missingA = a === null || a === undefined || a === '';
  const missingB = b === null || b === undefined || b === '';
  if (missingA || missingB) {
    return missingA === missingB ? 0 : missingA ? 1 : -1;
  }
  const result = typeof a === 'number' && typeof b === 'number' ? a - b : String(a).localeCompare(String(b), undefined, { sensitivity: 'base', numeric: true });
  return direction === 'asc' ? result : -result;
}
