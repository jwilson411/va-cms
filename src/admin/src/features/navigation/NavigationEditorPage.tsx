/**
 * NavigationEditorPage — admin drag-and-drop navigation menu builder.
 * Issue #46 — BRD FR-NAV-01, FR-NAV-03.
 *
 * Acceptance criteria:
 *   - Menu items shown in tree; drag-and-drop reorders and reparents.
 *   - Supports up to 3 levels deep.
 *   - Preview button shows menu render before saving.
 *
 * USWDS: usa-card, usa-button, usa-select, usa-alert, usa-modal
 */

import React, { useCallback, useState } from 'react';
import {
  useBulkReorder,
  useCreateItem,
  useDeleteItem,
  useMenuItems,
  useMenuPreview,
  useNavigationMenus,
  useUpdateItem,
} from './api';
import { NavItemForm } from './NavItemForm';
import { NavPreviewPanel } from './NavPreviewPanel';
import { NavTreeItem } from './NavTreeItem';
import type { NavTreeNode, UpsertItemRequest } from './types';
import { buildTree, flattenForReorder, moveItem } from './treeUtils';
import { RowActions } from '../../components/table';

export function NavigationEditorPage(): JSX.Element {
  const { data: menus, isLoading: menusLoading } = useNavigationMenus();

  const [selectedHandle, setSelectedHandle] = useState<string>('');
  const [showPreview, setShowPreview] = useState(false);
  const [editingItem, setEditingItem] = useState<NavTreeNode | null>(null);
  const [addingItem, setAddingItem] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [draggingId, setDraggingId] = useState<number | null>(null);
  const [localTree, setLocalTree] = useState<NavTreeNode[] | null>(null);
  const [pendingReorder, setPendingReorder] = useState(false);
  const [deleteConfirmId, setDeleteConfirmId] = useState<number | null>(null);
  const [deleteConfirmLabel, setDeleteConfirmLabel] = useState('');

  const { data: itemsData, isLoading: itemsLoading } =
    useMenuItems(selectedHandle);

  const { data: previewData, isLoading: previewLoading } = useMenuPreview(
    selectedHandle,
    showPreview
  );

  const createItem = useCreateItem(selectedHandle);
  const updateItem = useUpdateItem(
    selectedHandle,
    editingItem?.id ?? 0
  );
  const deleteItem = useDeleteItem(
    selectedHandle,
    deleteConfirmId ?? 0
  );
  const bulkReorder = useBulkReorder(selectedHandle);

  // Derive current tree: prefer local (pending drag) over server data
  const tree: NavTreeNode[] = localTree ??
    (itemsData ? buildTree(itemsData.items) : []);

  function handleMenuChange(handle: string): void {
    setSelectedHandle(handle);
    setLocalTree(null);
    setShowPreview(false);
    setEditingItem(null);
    setAddingItem(false);
  }

  // ── Drag and drop ─────────────────────────────────────────────────────────

  function handleDragStart(nodeId: number): void {
    setDraggingId(nodeId);
    if (!localTree) setLocalTree(buildTree(itemsData?.items ?? []));
  }

  function handleDrop(
    targetId: number | null,
    targetParentId: number | null,
    insertIndex: number
  ): void {
    if (draggingId == null) return;
    const updated = moveItem(
      localTree ?? buildTree(itemsData?.items ?? []),
      draggingId,
      targetId ?? targetParentId,
      insertIndex
    );
    setLocalTree(updated);
    setDraggingId(null);
    setPendingReorder(true);
  }

  const handleSaveReorder = useCallback(async () => {
    if (!localTree) return;
    const reorderPayload = flattenForReorder(localTree);
    await bulkReorder.mutateAsync(reorderPayload);
    setLocalTree(null);
    setPendingReorder(false);
  }, [localTree, bulkReorder]);

  function handleDiscardReorder(): void {
    setLocalTree(null);
    setPendingReorder(false);
  }

  // ── Item CRUD ─────────────────────────────────────────────────────────────

  async function handleItemSubmit(req: UpsertItemRequest): Promise<void> {
    setFormError(null);
    try {
      if (editingItem) {
        await updateItem.mutateAsync(req);
        setEditingItem(null);
      } else {
        await createItem.mutateAsync(req);
        setAddingItem(false);
      }
      setLocalTree(null);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'An error occurred.';
      setFormError(msg);
    }
  }

  function handleDeleteRequest(id: number, label: string): void {
    setDeleteConfirmId(id);
    setDeleteConfirmLabel(label);
  }

  async function handleDeleteConfirm(): Promise<void> {
    if (deleteConfirmId == null) return;
    try {
      await deleteItem.mutateAsync();
      setDeleteConfirmId(null);
      setLocalTree(null);
    } catch (err: unknown) {
      // swallow — display nothing; error will be visible through refetch
    }
  }

  // ── Parent options for item form ──────────────────────────────────────────

  function buildParentOptions(
    nodes: NavTreeNode[],
    excludeId: number | null,
    depth: number
  ): Array<{ id: number | null; label: string }> {
    const opts: Array<{ id: number | null; label: string }> = [];
    for (const node of nodes) {
      if (node.id === excludeId) continue; // cannot parent to self
      if (depth < 2) {
        // Can only be a parent if depth < 2 (max child depth = 2)
        opts.push({ id: node.id, label: '  '.repeat(depth) + node.label });
        opts.push(...buildParentOptions(node.children, excludeId, depth + 1));
      }
    }
    return opts;
  }

  const parentOptions = buildParentOptions(tree, editingItem?.id ?? null, 0);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <main id="main-content" className="margin-y-4">
      <h1>Navigation Menus</h1>

      {menusLoading && <p>Loading menus…</p>}

      {/* ── Menu selector ──────────────────────────────────────────────── */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor="menu-select">
          Select menu
        </label>
        <select
          className="usa-select"
          id="menu-select"
          data-testid="menu-select"
          value={selectedHandle}
          onChange={(e) => handleMenuChange(e.target.value)}
        >
          <option value="">— Choose a menu —</option>
          {(menus ?? []).map((m) => (
            <option key={m.handle} value={m.handle}>
              {m.name} ({m.handle})
            </option>
          ))}
        </select>
      </div>

      {selectedHandle && (
        <>
          {/* ── Toolbar ────────────────────────────────────────────────── */}
          <RowActions className="flex-align-center margin-bottom-2">
            <button
              type="button"
              className="usa-button"
              onClick={() => { setAddingItem(true); setEditingItem(null); setFormError(null); }}
              disabled={addingItem}
            >
              + Add item
            </button>

            <button
              type="button"
              className="usa-button usa-button--outline"
              onClick={() => setShowPreview((v) => !v)}
              aria-pressed={showPreview}
              aria-expanded={showPreview}
              data-testid="preview-toggle"
            >
              {showPreview ? 'Hide preview' : 'Preview menu'}
            </button>

            {pendingReorder && (
              <>
                <button
                  type="button"
                  className="usa-button"
                  onClick={() => void handleSaveReorder()}
                  disabled={bulkReorder.isPending}
                >
                  {bulkReorder.isPending ? 'Saving…' : 'Save order'}
                </button>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  onClick={handleDiscardReorder}
                >
                  Discard
                </button>
              </>
            )}
          </RowActions>

          {/* ── Reorder save error ─────────────────────────────────────── */}
          {bulkReorder.isError && (
            <div className="usa-alert usa-alert--error usa-alert--slim" role="alert">
              <div className="usa-alert__body">
                <p className="usa-alert__text">
                  Failed to save order. Please try again.
                </p>
              </div>
            </div>
          )}

          {/* ── Tree ───────────────────────────────────────────────────── */}
          {itemsLoading && <p>Loading navigation items…</p>}

          {!itemsLoading && tree.length === 0 && (
            <p className="text-base font-sans-sm">
              No items yet. Click <strong>+ Add item</strong> to get started.
            </p>
          )}

          {tree.length > 0 && (
            <div
              className="usa-card__body border border-base-lighter radius-md padding-2"
              data-testid="nav-tree"
            >
              <ul
                className="usa-list usa-list--unstyled"
                role="list"
                aria-label="Navigation tree"
              >
                {tree.map((node, idx) => (
                  <NavTreeItem
                    key={node.id}
                    node={node}
                    index={idx}
                    siblings={tree}
                    parentId={null}
                    depth={0}
                    onDragStart={handleDragStart}
                    onDrop={handleDrop}
                    onEditItem={(n) => {
                      setEditingItem(n);
                      setAddingItem(false);
                      setFormError(null);
                    }}
                    onDeleteItem={handleDeleteRequest}
                    draggingId={draggingId}
                  />
                ))}
              </ul>
            </div>
          )}

          {/* ── Item form ──────────────────────────────────────────────── */}
          {(addingItem || editingItem) && (
            <div className="margin-top-3">
              <NavItemForm
                item={editingItem}
                parentOptions={parentOptions}
                onSubmit={(req) => void handleItemSubmit(req)}
                onCancel={() => {
                  setEditingItem(null);
                  setAddingItem(false);
                  setFormError(null);
                }}
                isLoading={createItem.isPending || updateItem.isPending}
                error={formError}
              />
            </div>
          )}

          {/* ── Delete confirmation ────────────────────────────────────── */}
          {deleteConfirmId !== null && (
            <div
              className="usa-modal"
              role="dialog"
              aria-modal="true"
              aria-labelledby="delete-confirm-title"
              data-testid="delete-confirm-dialog"
            >
              <div className="usa-modal__content">
                <div className="usa-modal__main">
                  <h2 id="delete-confirm-title" className="usa-modal__heading">
                    Delete navigation item?
                  </h2>
                  <div className="usa-prose">
                    <p>
                      Delete <strong>{deleteConfirmLabel}</strong> and all its
                      child items? This cannot be undone.
                    </p>
                  </div>
                  <div className="usa-modal__footer">
                    <ul className="usa-button-group">
                      <li className="usa-button-group__item">
                        <button
                          type="button"
                          className="usa-button usa-button--secondary"
                          onClick={() => void handleDeleteConfirm()}
                          disabled={deleteItem.isPending}
                        >
                          {deleteItem.isPending ? 'Deleting…' : 'Yes, delete'}
                        </button>
                      </li>
                      <li className="usa-button-group__item">
                        <button
                          type="button"
                          className="usa-button usa-button--unstyled padding-105 text-center"
                          onClick={() => setDeleteConfirmId(null)}
                        >
                          Cancel
                        </button>
                      </li>
                    </ul>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── Preview ────────────────────────────────────────────────── */}
          {showPreview && (
            <div className="margin-top-3" data-testid="preview-section">
              {previewLoading ? (
                <p>Loading preview…</p>
              ) : previewData ? (
                <NavPreviewPanel
                  menuName={previewData.name}
                  items={previewData.items}
                />
              ) : (
                <p className="text-base">No preview available.</p>
              )}
            </div>
          )}
        </>
      )}
    </main>
  );
}
