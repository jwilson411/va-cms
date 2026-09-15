/**
 * Navigation tree utilities.
 * Issue #46 — BRD FR-NAV-03.
 */

import type { NavItemAdminDto, NavTreeNode, ReorderItemRequest } from './types';

/** Build nested tree from flat API response, ordered by sortOrder. */
export function buildTree(items: NavItemAdminDto[]): NavTreeNode[] {
  const nodeMap = new Map<number, NavTreeNode>();

  for (const item of items) {
    nodeMap.set(item.id, { ...item, children: [] });
  }

  const roots: NavTreeNode[] = [];
  for (const [, node] of nodeMap) {
    if (node.parentItemId == null) {
      roots.push(node);
    } else {
      const parent = nodeMap.get(node.parentItemId);
      if (parent) {
        parent.children.push(node);
      } else {
        // Orphaned item — treat as root
        roots.push(node);
      }
    }
  }

  // Sort each level by sortOrder
  sortChildren(roots);
  return roots;
}

function sortChildren(nodes: NavTreeNode[]): void {
  nodes.sort((a, b) => a.sortOrder - b.sortOrder);
  for (const node of nodes) sortChildren(node.children);
}

/** Flatten a tree into a ReorderItemRequest array with updated sortOrder values. */
export function flattenForReorder(
  nodes: NavTreeNode[],
  parentId: number | null = null
): ReorderItemRequest[] {
  const result: ReorderItemRequest[] = [];
  nodes.forEach((node, idx) => {
    result.push({ id: node.id, parentItemId: parentId, sortOrder: idx });
    result.push(...flattenForReorder(node.children, node.id));
  });
  return result;
}

/**
 * Move an item to a new parent and position.
 * Returns a new tree with the move applied (immutable).
 */
export function moveItem(
  tree: NavTreeNode[],
  itemId: number,
  newParentId: number | null,
  newIndex: number
): NavTreeNode[] {
  // Deep clone
  const cloned = JSON.parse(JSON.stringify(tree)) as NavTreeNode[];

  // Remove the item from its current position
  let removed: NavTreeNode | null = null;

  function removeFromTree(nodes: NavTreeNode[]): boolean {
    for (let i = 0; i < nodes.length; i++) {
      if (nodes[i].id === itemId) {
        [removed] = nodes.splice(i, 1);
        return true;
      }
      if (removeFromTree(nodes[i].children)) return true;
    }
    return false;
  }

  removeFromTree(cloned);
  if (!removed) return cloned;

  // Insert into new position
  function insertIntoTree(nodes: NavTreeNode[]): boolean {
    if (newParentId == null) {
      nodes.splice(newIndex, 0, removed!);
      return true;
    }
    for (const node of nodes) {
      if (node.id === newParentId) {
        node.children.splice(newIndex, 0, removed!);
        return true;
      }
      if (insertIntoTree(node.children)) return true;
    }
    return false;
  }

  insertIntoTree(cloned);
  return cloned;
}

/** Compute the maximum depth of an item + its subtree in the current tree. */
export function subtreeMaxDepth(node: NavTreeNode): number {
  if (node.children.length === 0) return 0;
  return 1 + Math.max(...node.children.map(subtreeMaxDepth));
}

/** Compute the depth of an item's ancestor chain in the tree. */
export function ancestorDepth(
  tree: NavTreeNode[],
  parentId: number | null
): number {
  if (parentId == null) return 0;
  let depth = 0;
  function find(nodes: NavTreeNode[], id: number): number {
    for (const node of nodes) {
      if (node.id === id) return depth;
      depth++;
      const result = find(node.children, id);
      if (result !== -1) return result;
      depth--;
    }
    return -1;
  }
  return find(tree, parentId) + 1;
}
