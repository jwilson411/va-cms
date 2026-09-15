/**
 * Tests for navigation tree utilities.
 * Issue #46 — BRD FR-NAV-03.
 */

import { describe, expect, it } from 'vitest';
import type { NavItemAdminDto, NavTreeNode } from './types';
import { buildTree, flattenForReorder, moveItem, subtreeMaxDepth } from './treeUtils';

// ── Test data ──────────────────────────────────────────────────────────────────

function makeItem(
  id: number,
  parentItemId: number | null = null,
  sortOrder = 0
): NavItemAdminDto {
  return {
    id,
    menuId: 1,
    parentItemId,
    label: `Item ${id}`,
    url: `/${id}`,
    contentEntryId: null,
    target: '_self',
    sortOrder,
    isVisible: true,
    depth: 0,
  };
}

// ── buildTree ──────────────────────────────────────────────────────────────────

describe('buildTree', () => {
  it('returns empty array for empty input', () => {
    expect(buildTree([])).toEqual([]);
  });

  it('creates root nodes when parentItemId is null', () => {
    const items = [makeItem(1), makeItem(2)];
    const tree  = buildTree(items);
    expect(tree).toHaveLength(2);
  });

  it('nests children under their parent', () => {
    const items = [makeItem(1), makeItem(2, 1)];
    const tree  = buildTree(items);
    expect(tree).toHaveLength(1);
    expect(tree[0].children).toHaveLength(1);
    expect(tree[0].children[0].id).toBe(2);
  });

  it('sorts nodes by sortOrder within each level', () => {
    const items = [makeItem(1, null, 2), makeItem(2, null, 1), makeItem(3, null, 0)];
    const tree  = buildTree(items);
    expect(tree.map((n) => n.id)).toEqual([3, 2, 1]);
  });

  it('handles three-level nesting', () => {
    const items = [makeItem(1), makeItem(2, 1), makeItem(3, 2)];
    const tree  = buildTree(items);
    expect(tree[0].children[0].children[0].id).toBe(3);
  });

  it('treats orphaned items as roots', () => {
    const items = [makeItem(2, 99)]; // parent 99 does not exist
    const tree  = buildTree(items);
    expect(tree).toHaveLength(1);
  });
});

// ── flattenForReorder ──────────────────────────────────────────────────────────

describe('flattenForReorder', () => {
  it('assigns sortOrder by position in siblings', () => {
    const tree: NavTreeNode[] = [
      { ...makeItem(1), children: [] },
      { ...makeItem(2), children: [] },
    ];
    const flat = flattenForReorder(tree);
    expect(flat.find((r) => r.id === 1)?.sortOrder).toBe(0);
    expect(flat.find((r) => r.id === 2)?.sortOrder).toBe(1);
  });

  it('assigns parentItemId correctly', () => {
    const child: NavTreeNode = { ...makeItem(2), children: [] };
    const tree: NavTreeNode[] = [{ ...makeItem(1), children: [child] }];
    const flat = flattenForReorder(tree);
    expect(flat.find((r) => r.id === 2)?.parentItemId).toBe(1);
  });

  it('sets parentItemId null for root items', () => {
    const tree: NavTreeNode[] = [{ ...makeItem(1), children: [] }];
    const flat = flattenForReorder(tree);
    expect(flat.find((r) => r.id === 1)?.parentItemId).toBeNull();
  });
});

// ── moveItem ───────────────────────────────────────────────────────────────────

describe('moveItem', () => {
  it('moves item to new position among roots', () => {
    const tree: NavTreeNode[] = [
      { ...makeItem(1, null, 0), children: [] },
      { ...makeItem(2, null, 1), children: [] },
      { ...makeItem(3, null, 2), children: [] },
    ];
    const updated = moveItem(tree, 3, null, 0);
    expect(updated.map((n) => n.id)).toEqual([3, 1, 2]);
  });

  it('returns unchanged tree when item not found', () => {
    const tree: NavTreeNode[] = [{ ...makeItem(1), children: [] }];
    const updated = moveItem(tree, 99, null, 0);
    expect(updated.map((n) => n.id)).toEqual([1]);
  });
});

// ── subtreeMaxDepth ────────────────────────────────────────────────────────────

describe('subtreeMaxDepth', () => {
  it('returns 0 for leaf node', () => {
    const node: NavTreeNode = { ...makeItem(1), children: [] };
    expect(subtreeMaxDepth(node)).toBe(0);
  });

  it('returns 1 for one level of children', () => {
    const child: NavTreeNode = { ...makeItem(2), children: [] };
    const node: NavTreeNode  = { ...makeItem(1), children: [child] };
    expect(subtreeMaxDepth(node)).toBe(1);
  });

  it('returns 2 for two levels of children', () => {
    const grandchild: NavTreeNode = { ...makeItem(3), children: [] };
    const child: NavTreeNode      = { ...makeItem(2), children: [grandchild] };
    const node: NavTreeNode       = { ...makeItem(1), children: [child] };
    expect(subtreeMaxDepth(node)).toBe(2);
  });
});
