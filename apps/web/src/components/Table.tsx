import MuiTable from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import type { Key, ReactNode } from 'react';
import { sem } from '../theme/tokens';

export interface TableColumn<Row> {
  key: string;
  header: ReactNode;
  render: (row: Row) => ReactNode;
}

export interface TableProps<Row> {
  label: string;
  columns: TableColumn<Row>[];
  rows: Row[];
  rowKey: (row: Row) => Key;
  /** Every cell on one line; long text should be cut with ellipsis by the column itself. */
  nowrap?: boolean;
}

export function Table<Row>({ label, columns, rows, rowKey, nowrap }: TableProps<Row>) {
  const cell = nowrap ? { whiteSpace: 'nowrap' } : undefined;
  return (
    <MuiTable aria-label={label} size="small">
      <TableHead>
        <TableRow>
          {columns.map((column) => (
            <TableCell key={column.key} sx={cell}>{column.header}</TableCell>
          ))}
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
