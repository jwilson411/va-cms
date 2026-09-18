/**
 * Webhook management API hooks — TanStack Query v5.
 * Issue #54 (registration) / #168 (delivery log, redelivery).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';
import type {
  WebhookDeliveryDto,
  WebhookDeliveryListResponse,
  WebhookListItem,
  WebhookRegistrationRequest,
  WebhookRegistrationResponse,
} from './types';

const BASE = '/api/v1/webhooks';

export class WebhookApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'WebhookApiError';
  }
}

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await authorizedFetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options?.headers ?? {}),
    },
  });
  if (!res.ok) {
    // The API answers validation failures as { error } and framework errors as ProblemDetails.
    let message = `Request failed (${res.status})`;
    try {
      const body = (await res.json()) as { error?: string; detail?: string; title?: string };
      message = body.error ?? body.detail ?? body.title ?? message;
    } catch {
      /* non-JSON body */
    }
    throw new WebhookApiError(res.status, message);
  }
  if (res.status === 204) return undefined as unknown as T;
  return res.json() as Promise<T>;
}

// ── Queries ───────────────────────────────────────────────────────────────────

export function useWebhooks() {
  return useQuery<WebhookListItem[]>({
    queryKey: ['webhooks'],
    queryFn: () => apiFetch<WebhookListItem[]>(BASE),
  });
}

export function useWebhookDeliveries(webhookId: number | null, page: number, pageSize = 25) {
  return useQuery<WebhookDeliveryListResponse>({
    queryKey: ['webhooks', webhookId, 'deliveries', page, pageSize],
    queryFn: () =>
      apiFetch<WebhookDeliveryListResponse>(
        `${BASE}/${webhookId}/deliveries?page=${page}&pageSize=${pageSize}`,
      ),
    enabled: webhookId !== null && webhookId > 0,
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

export function useRegisterWebhook() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: WebhookRegistrationRequest) =>
      apiFetch<WebhookRegistrationResponse>(BASE, { method: 'POST', body: JSON.stringify(req) }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['webhooks'] }),
  });
}

export function useDeleteWebhook() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiFetch<void>(`${BASE}/${id}`, { method: 'DELETE' }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['webhooks'] }),
  });
}

export function useRedeliver(webhookId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (deliveryId: number) =>
      apiFetch<WebhookDeliveryDto>(`${BASE}/${webhookId}/deliveries/${deliveryId}/redeliver`, {
        method: 'POST',
      }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: ['webhooks', webhookId, 'deliveries'] }),
  });
}
