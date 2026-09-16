/**
 * API hooks for the in-app notification center (issue #38 — FR-WORKFLOW-02/03).
 *
 * The inbox is polled every POLL_INTERVAL_MS so the bell badge stays current
 * without a socket; every mutation invalidates the same query so the badge and
 * the panel never disagree.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

const NOTIFICATIONS_API = '/api/v1/notifications';
const POLL_INTERVAL_MS = 30_000;

// ── Types ─────────────────────────────────────────────────────────────────────

export type NotificationEventType = 'ReviewRequested' | 'ContentApproved' | 'ContentReturned';

export interface NotificationItem {
  id: number;
  eventType: NotificationEventType | string;
  contentEntryId: number;
  contentTitle: string;
  message: string;
  actorId: number | null;
  actorDisplayName: string | null;
  comment: string | null;
  isRead: boolean;
  readAt: string | null;
  createdAt: string;
  entrySlug: string | null;
  entryStatus: string | null;
}

export interface NotificationList {
  items: NotificationItem[];
  unreadCount: number;
}

// ── Query keys ────────────────────────────────────────────────────────────────

export const notificationKeys = {
  all: () => ['notifications'] as const,
};

// ── useNotifications ──────────────────────────────────────────────────────────

export function useNotifications(limit = 20) {
  return useQuery<NotificationList>({
    queryKey: [...notificationKeys.all(), limit],
    queryFn: async () => {
      const res = await authorizedFetch(`${NOTIFICATIONS_API}?limit=${limit}`);
      if (!res.ok) throw new Error(`Notifications fetch failed: ${res.status}`);
      return res.json() as Promise<NotificationList>;
    },
    refetchInterval: POLL_INTERVAL_MS,
    staleTime: 10 * 1000,
  });
}

// ── useMarkNotificationsRead ──────────────────────────────────────────────────

export function useMarkNotificationsRead() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, number[]>({
    mutationFn: async (ids) => {
      if (ids.length === 0) return;
      const res = await authorizedFetch(`${NOTIFICATIONS_API}/read`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids }),
      });
      if (!res.ok) throw new Error(`Mark read failed: ${res.status}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: notificationKeys.all() });
    },
  });
}

// ── useMarkAllNotificationsRead ───────────────────────────────────────────────

export function useMarkAllNotificationsRead() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, void>({
    mutationFn: async () => {
      const res = await authorizedFetch(`${NOTIFICATIONS_API}/read-all`, { method: 'POST' });
      if (!res.ok) throw new Error(`Mark all read failed: ${res.status}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: notificationKeys.all() });
    },
  });
}
