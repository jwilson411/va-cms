/**
 * NotificationBell — bell icon + unread badge in the admin top bar, with a
 * dropdown panel listing workflow notifications (issue #38, FR-WORKFLOW-02/03).
 *
 *   - Badge shows the unread count (hidden at zero); the button's accessible
 *     name carries the same number so screen readers hear it.
 *   - Each item: event description, content title, timestamp, and a link to
 *     the content entry's edit page.
 *   - Opening the panel marks every listed unread notification as read
 *     (AC: "marked as read when viewed"); "Mark all as read" clears the rest.
 *   - Escape or a click outside closes the panel; Escape returns focus to the bell.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  useMarkAllNotificationsRead,
  useMarkNotificationsRead,
  useNotifications,
  type NotificationItem,
} from './useNotifications';
import { formatRelativeTime } from './relativeTime';

const PANEL_ID = 'va-cms-notification-panel';
const PANEL_LIMIT = 20;

export function entryLink(n: NotificationItem): string {
  return `/admin/content/${n.contentEntryId}/edit`;
}

export function NotificationBell(): JSX.Element {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const bellRef = useRef<HTMLButtonElement>(null);
  const markedRef = useRef<Set<number>>(new Set());

  const { data, isLoading, isError } = useNotifications(PANEL_LIMIT);
  const markRead = useMarkNotificationsRead();
  const markAllRead = useMarkAllNotificationsRead();

  const items = data?.items ?? [];
  const unreadCount = data?.unreadCount ?? 0;

  const close = useCallback(() => setOpen(false), []);

  // Viewing the panel marks what is on screen as read. markedRef stops the same
  // ids being re-sent while the refetch that clears them is still in flight.
  useEffect(() => {
    if (!open) return;
    const unreadIds = items.filter((n) => !n.isRead && !markedRef.current.has(n.id)).map((n) => n.id);
    if (unreadIds.length === 0) return;
    unreadIds.forEach((id) => markedRef.current.add(id));
    markRead.mutate(unreadIds);
    // markRead is a stable mutation object from react-query
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, items]);

  // Close on Escape (returning focus to the bell) and on click outside.
  useEffect(() => {
    if (!open) return;

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        close();
        bellRef.current?.focus();
      }
    };
    const onPointerDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) close();
    };

    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('mousedown', onPointerDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('mousedown', onPointerDown);
    };
  }, [open, close]);

  const badgeText = unreadCount > 99 ? '99+' : String(unreadCount);
  const bellLabel =
    unreadCount === 0
      ? 'Notifications'
      : `Notifications, ${unreadCount} unread`;

  return (
    <div className="va-cms-notifications" ref={containerRef} data-testid="notification-bell">
      <button
        ref={bellRef}
        type="button"
        className="usa-button usa-button--unstyled va-cms-notifications__bell"
        aria-label={bellLabel}
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls={PANEL_ID}
        onClick={() => setOpen((o) => !o)}
      >
        <svg className="usa-icon usa-icon--size-3" aria-hidden="true" focusable="false" role="img">
          <use href="/uswds/img/sprite.svg#notifications" />
        </svg>
        {unreadCount > 0 && (
          <span className="va-cms-notifications__badge" aria-hidden="true" data-testid="notification-badge">
            {badgeText}
          </span>
        )}
      </button>

      <div
        id={PANEL_ID}
        className="va-cms-notifications__panel"
        role="region"
        aria-label="Notifications"
        hidden={!open}
      >
        <div className="va-cms-notifications__header">
          <h2 className="margin-0 font-sans-sm text-bold">Notifications</h2>
          <button
            type="button"
            className="usa-button usa-button--unstyled font-sans-3xs"
            onClick={() => markAllRead.mutate()}
            disabled={unreadCount === 0 || markAllRead.isPending}
          >
            Mark all as read
          </button>
        </div>

        {isLoading && <p className="usa-prose font-sans-3xs padding-2 margin-0">Loading notifications…</p>}
        {isError && (
          <p className="usa-prose font-sans-3xs text-error padding-2 margin-0" role="alert">
            Could not load notifications.
          </p>
        )}
        {!isLoading && !isError && items.length === 0 && (
          <p className="usa-prose font-sans-3xs padding-2 margin-0 text-base">
            You have no notifications.
          </p>
        )}

        {items.length > 0 && (
          <ul className="usa-list usa-list--unstyled va-cms-notifications__list">
            {items.map((n) => (
              <li
                key={n.id}
                className={`va-cms-notifications__item${n.isRead ? '' : ' va-cms-notifications__item--unread'}`}
                data-testid={`notification-${n.id}`}
              >
                <Link to={entryLink(n)} className="usa-link va-cms-notifications__link" onClick={close}>
                  <span className="va-cms-notifications__message">
                    {!n.isRead && <span className="usa-sr-only">Unread: </span>}
                    {n.message}
                  </span>
                  {n.comment && (
                    <span className="va-cms-notifications__comment">“{n.comment}”</span>
                  )}
                  <span className="va-cms-notifications__meta">
                    <span className="va-cms-notifications__title">{n.contentTitle}</span>
                    {' · '}
                    <time dateTime={n.createdAt} title={new Date(n.createdAt).toLocaleString()}>
                      {formatRelativeTime(n.createdAt)}
                    </time>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
