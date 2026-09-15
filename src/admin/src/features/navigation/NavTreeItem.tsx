/**
 * NavTreeItem — a single node in the navigation tree editor.
 * Uses native HTML5 drag events for reordering.
 * Issue #46 — BRD FR-NAV-03.
 *
 * USWDS: usa-list, usa-button, usa-icon
 * Accessibility: draggable elements have aria-grabbed, drop targets have aria-dropeffect
 */

import React, { useState } from 'react';
import type { NavTreeNode } from './types';

interface NavTreeItemProps {
  node: NavTreeNode;
  index: number;
  siblings: NavTreeNode[];
  parentId: number | null;
  /** Depth level for indentation + depth guard display. */
  depth: number;
  onDragStart: (nodeId: number) => void;
  onDrop: (targetId: number | null, targetParentId: number | null, insertIndex: number) => void;
  onEditItem: (node: NavTreeNode) => void;
  onDeleteItem: (id: number, label: string) => void;
  draggingId: number | null;
}

export function NavTreeItem({
  node,
  index,
  siblings,
  parentId,
  depth,
  onDragStart,
  onDrop,
  onEditItem,
  onDeleteItem,
  draggingId,
}: NavTreeItemProps): JSX.Element {
  const [isDragOver, setIsDragOver] = useState(false);
  const isDragging = draggingId === node.id;

  function handleDragStart(e: React.DragEvent<HTMLLIElement>): void {
    e.dataTransfer.effectAllowed = 'move';
    onDragStart(node.id);
  }

  function handleDragOver(e: React.DragEvent<HTMLLIElement>): void {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    setIsDragOver(true);
  }

  function handleDragLeave(): void {
    setIsDragOver(false);
  }

  function handleDrop(e: React.DragEvent<HTMLLIElement>): void {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(false);
    // Drop onto this item — reparent the dragged item under this node at index 0
    if (depth < 2) {
      // Only reparent if this item has room for children (depth < 2 means item depth <3)
      onDrop(node.id, parentId, index);
    } else {
      // Same level drop — reorder among siblings
      onDrop(null, parentId, index);
    }
  }

  const indentPx = depth * 24;

  return (
    <li
      draggable
      aria-grabbed={isDragging}
      role="listitem"
      data-testid={`nav-tree-item-${node.id}`}
      className={[
        'va-nav-tree-item',
        isDragging ? 'is-dragging' : '',
        isDragOver ? 'is-drag-over' : '',
      ]
        .filter(Boolean)
        .join(' ')}
      style={{ paddingLeft: `${indentPx}px` }}
      onDragStart={handleDragStart}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <div className="va-nav-tree-item__row display-flex flex-align-center padding-y-1 border-bottom border-base-lighter">
        {/* Drag handle indicator */}
        <span
          className="va-nav-drag-handle text-base-light margin-right-1"
          aria-hidden="true"
          title="Drag to reorder"
          style={{ cursor: 'grab', userSelect: 'none' }}
        >
          ⠿
        </span>

        {/* Visibility badge */}
        {!node.isVisible && (
          <span
            className="usa-tag usa-tag--gray margin-right-1"
            aria-label="Hidden item"
            title="Hidden"
          >
            Hidden
          </span>
        )}

        {/* Label + URL */}
        <span className="flex-fill">
          <strong className="display-block">{node.label}</strong>
          {node.url && (
            <span className="text-base font-mono-2xs">{node.url}</span>
          )}
        </span>

        {/* Depth indicator */}
        {depth > 0 && (
          <span
            className="usa-tag margin-right-1"
            aria-label={`Level ${depth + 1}`}
          >
            L{depth + 1}
          </span>
        )}

        {/* Actions */}
        <div className="va-nav-tree-item__actions display-flex gap-1">
          <button
            type="button"
            className="usa-button usa-button--unstyled"
            aria-label={`Edit ${node.label}`}
            onClick={() => onEditItem(node)}
          >
            Edit
          </button>
          <button
            type="button"
            className="usa-button usa-button--unstyled text-red"
            aria-label={`Delete ${node.label}`}
            onClick={() => onDeleteItem(node.id, node.label)}
          >
            Delete
          </button>
        </div>
      </div>

      {/* Nested children */}
      {node.children.length > 0 && (
        <ul
          className="usa-list usa-list--unstyled"
          role="list"
          aria-label={`Children of ${node.label}`}
        >
          {node.children.map((child, childIdx) => (
            <NavTreeItem
              key={child.id}
              node={child}
              index={childIdx}
              siblings={node.children}
              parentId={node.id}
              depth={depth + 1}
              onDragStart={onDragStart}
              onDrop={onDrop}
              onEditItem={onEditItem}
              onDeleteItem={onDeleteItem}
              draggingId={draggingId}
            />
          ))}
        </ul>
      )}
    </li>
  );
}
