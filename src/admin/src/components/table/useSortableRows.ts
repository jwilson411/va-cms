import { useMemo, useState } from 'react';

/** Sort direction shared by the server-sorted and client-sorted admin tables. */
export type SortDirection = 'ASC' | 'DESC';

export type SortableValue = string | number | boolean | Date | null | undefined;

export interface UseSortableRowsOptions<T, K extends string> {
  /** Column the table is sorted by on first render. */
  initialKey: K;
  /** Defaults to ascending; pass DESC for "newest first" style tables. */
  initialDirection?: SortDirection;
  /** Maps a row + column key to the value used for comparison. */
  getValue: (row: T, key: K) => SortableValue;
}

export interface UseSortableRowsResult<T, K extends string> {
  /** Rows in their sorted order (a new array — the input is never mutated). */
  rows: T[];
  sortKey: K;
  sortDirection: SortDirection;
  toggleSort: (key: K) => void;
}

function toComparable(value: SortableValue): string | number {
  if (value instanceof Date) return value.getTime();
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (typeof value === 'number') return value;
  if (value == null) return '';
  return value;
}

/**
 * Client-side sorting for tables whose full dataset is already in memory.
 *
 * Pairs with `<SortableHeader />` so every admin list uses the same USWDS
 * sort affordance and the same ASC/DESC vocabulary as the server-sorted tables.
 */
export function useSortableRows<T, K extends string>(
  rows: T[] | undefined,
  { initialKey, initialDirection = 'ASC', getValue }: UseSortableRowsOptions<T, K>,
): UseSortableRowsResult<T, K> {
  const [sortKey, setSortKey] = useState<K>(initialKey);
  const [sortDirection, setSortDirection] = useState<SortDirection>(initialDirection);

  const toggleSort = (key: K): void => {
    if (key === sortKey) {
      setSortDirection((current) => (current === 'ASC' ? 'DESC' : 'ASC'));
    } else {
      setSortKey(key);
      setSortDirection('ASC');
    }
  };

  const sortedRows = useMemo(() => {
    const list = [...(rows ?? [])];
    list.sort((a, b) => {
      const aValue = toComparable(getValue(a, sortKey));
      const bValue = toComparable(getValue(b, sortKey));
      const comparison =
        typeof aValue === 'number' && typeof bValue === 'number'
          ? aValue - bValue
          : String(aValue).localeCompare(String(bValue), undefined, {
              numeric: true,
              sensitivity: 'base',
            });
      return sortDirection === 'ASC' ? comparison : -comparison;
    });
    return list;
  }, [rows, sortKey, sortDirection, getValue]);

  return { rows: sortedRows, sortKey, sortDirection, toggleSort };
}
