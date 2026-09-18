/**
 * Webhook management types — mirror WebhooksController DTOs.
 * Issue #54 (registration) / #168 (delivery log, redelivery).
 */

export const WEBHOOK_EVENTS = [
  'content.published',
  'content.unpublished',
  'content.archived',
  'media.uploaded',
  'navigation.updated',
  'settings.updated',
  'redirects.updated',
] as const;

export type WebhookEvent = (typeof WEBHOOK_EVENTS)[number];

export interface WebhookListItem {
  id: number;
  name: string;
  url: string;
  events: string[];
  isActive: boolean;
  createdAt: string;
}

export interface WebhookRegistrationRequest {
  name?: string;
  url: string;
  /** Optional — the server generates one when omitted. */
  secret?: string;
  events: string[];
}

export interface WebhookRegistrationResponse {
  id: number;
  name: string;
  url: string;
  /** Shown once; the API never returns it again. */
  secret: string;
  events: string[];
}

export interface WebhookDeliveryDto {
  id: number;
  webhookId: number;
  eventName: string;
  payloadJson: string;
  responseStatusCode: number | null;
  attemptNumber: number;
  deliveredAt: string;
  errorMessage: string | null;
  redeliveryOfId: number | null;
  succeeded: boolean;
}

export interface WebhookDeliveryListResponse {
  items: WebhookDeliveryDto[];
  page: number;
  pageSize: number;
  totalRows: number;
}
