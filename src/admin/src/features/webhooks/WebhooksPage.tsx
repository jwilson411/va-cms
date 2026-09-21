/**
 * WebhooksPage — register, list and remove webhook subscribers, and inspect the
 * delivery log of each one with a redeliver action.
 * Issue #54 — BRD FR-DEV-07; issue #168 — delivery log + redelivery (epic #152).
 *
 * Needs the Developer role (API policy CanDevelop); a 403 renders as a notice.
 */

import React, { useState } from 'react';
import {
  useDeleteWebhook,
  useRedeliver,
  useRegisterWebhook,
  useWebhookDeliveries,
  useWebhooks,
  WebhookApiError,
} from './api';
import { WebhookForm } from './WebhookForm';
import type { WebhookDeliveryDto, WebhookListItem, WebhookRegistrationResponse } from './types';
import { AdminPagination, RowActions, SortableHeader, useSortableRows } from '../../components/table';

const DELIVERY_PAGE_SIZE = 25;

type WebhookSortKey = 'name' | 'url' | 'events' | 'isActive' | 'createdAt';

function webhookSortValue(row: WebhookListItem, key: WebhookSortKey): string | boolean {
  if (key === 'events') return row.events.join(', ');
  return row[key];
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

export function WebhooksPage() {
  const [showCreate, setShowCreate]     = useState(false);
  const [registered, setRegistered]     = useState<WebhookRegistrationResponse | null>(null);
  const [selectedId, setSelectedId]     = useState<number | null>(null);
  const [actionError, setActionError]   = useState<string | null>(null);

  const { data: webhooks, isLoading, isError, error } = useWebhooks();
  const registerMutation = useRegisterWebhook();
  const deleteMutation   = useDeleteWebhook();

  const {
    rows: sortedWebhooks,
    sortKey,
    sortDirection,
    toggleSort,
  } = useSortableRows<WebhookListItem, WebhookSortKey>(webhooks, {
    initialKey: 'createdAt',
    initialDirection: 'DESC',
    getValue: webhookSortValue,
  });

  function handleRegister(values: Parameters<typeof registerMutation.mutate>[0]) {
    setActionError(null);
    registerMutation.mutate(values, {
      onSuccess: (res) => { setShowCreate(false); setRegistered(res); },
    });
  }

  function handleDelete(row: WebhookListItem) {
    setActionError(null);
    deleteMutation.mutate(row.id, {
      onSuccess: () => { if (selectedId === row.id) setSelectedId(null); },
      onError:   (e) => setActionError(errorMessage(e)),
    });
  }

  const forbidden = isError && error instanceof WebhookApiError && error.status === 403;

  return (
    <main id="main-content" className="padding-y-4">
      <div className="grid-row grid-gap">
        <div className="grid-col-12">
          <div className="display-flex flex-align-center flex-justify margin-bottom-3">
            <h1 className="margin-0 font-heading-xl">Webhooks</h1>
            <button type="button" className="usa-button"
              onClick={() => { setShowCreate(true); setRegistered(null); setActionError(null); }}
              disabled={forbidden}>
              + Register webhook
            </button>
          </div>

          <p className="usa-intro font-body-sm">
            Subscribers are called over https with an <code>X-CMS-Signature</code> HMAC. Destinations must be
            on the <code>webhooks.allowedHosts</code> site setting; every delivery re-checks the host and the
            address it resolves to, and redirects are never followed.
          </p>

          {actionError && (
            <div className="usa-alert usa-alert--error usa-alert--slim margin-bottom-2" role="alert">
              <div className="usa-alert__body"><p className="usa-alert__text">{actionError}</p></div>
            </div>
          )}

          {registered && (
            <div className="usa-alert usa-alert--success margin-bottom-3" role="status" data-testid="webhook-secret-notice">
              <div className="usa-alert__body">
                <h2 className="usa-alert__heading">Webhook #{registered.id} registered</h2>
                <p className="usa-alert__text">
                  Signing secret — copy it now, it will not be shown again:
                </p>
                <p><code className="font-code-md" data-testid="webhook-secret">{registered.secret}</code></p>
                <button type="button" className="usa-button usa-button--unstyled" onClick={() => setRegistered(null)}>
                  Dismiss
                </button>
              </div>
            </div>
          )}

          {showCreate && (
            <div className="bg-base-lightest padding-3 border-1px border-base-light radius-md margin-bottom-3">
              <h2 className="font-heading-md margin-top-0">Register webhook</h2>
              <WebhookForm
                onSubmit={handleRegister}
                onCancel={() => { setShowCreate(false); registerMutation.reset(); }}
                isSubmitting={registerMutation.isPending}
                submitError={registerMutation.isError ? errorMessage(registerMutation.error) : null}
              />
            </div>
          )}

          {isLoading && <p className="usa-body">Loading…</p>}
          {isError && (
            <div className="usa-alert usa-alert--error usa-alert--slim" role="alert">
              <div className="usa-alert__body">
                <p className="usa-alert__text">
                  {forbidden
                    ? 'Webhook management needs the Developer role.'
                    : `Failed to load webhooks: ${errorMessage(error)}`}
                </p>
              </div>
            </div>
          )}

          {webhooks && (
            <div className="usa-table-container--scrollable" tabIndex={0}>
              <table className="usa-table usa-table--borderless width-full">
                <caption className="usa-sr-only">Registered webhooks</caption>
                <thead>
                  <tr>
                    <SortableHeader label="Name" field="name" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
                    <SortableHeader label="URL" field="url" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
                    <SortableHeader label="Events" field="events" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
                    <SortableHeader label="Status" field="isActive" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
                    <SortableHeader label="Registered" field="createdAt" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
                    <th scope="col">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedWebhooks.length === 0 && (
                    <tr><td colSpan={6} className="text-italic text-base">No webhooks registered.</td></tr>
                  )}
                  {sortedWebhooks.map((row) => (
                    <tr key={row.id}>
                      <td>{row.name}</td>
                      <td><code className="font-code-sm">{row.url}</code></td>
                      <td>{row.events.join(', ')}</td>
                      <td>
                        <span className={`usa-tag ${row.isActive ? 'bg-success-dark' : 'bg-base'}`}>
                          {row.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                      <td>{new Date(row.createdAt).toLocaleDateString()}</td>
                      <td>
                        <RowActions>
                          <button type="button" className="usa-button usa-button--unstyled"
                            aria-expanded={selectedId === row.id}
                            aria-controls="webhook-delivery-log"
                            aria-label={`${selectedId === row.id ? 'Hide' : 'Show'} deliveries for ${row.name}`}
                            onClick={() => setSelectedId(selectedId === row.id ? null : row.id)}>
                            {selectedId === row.id ? 'Hide deliveries' : 'Deliveries'}
                          </button>
                          {row.isActive && (
                            <button type="button" className="usa-button usa-button--unstyled text-error"
                              onClick={() => handleDelete(row)}
                              disabled={deleteMutation.isPending}
                              aria-label={`Remove webhook ${row.name}`}>
                              Remove
                            </button>
                          )}
                        </RowActions>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {selectedId !== null && webhooks && (
            <DeliveryLog
              webhook={webhooks.find((w) => w.id === selectedId) ?? null}
              webhookId={selectedId}
            />
          )}
        </div>
      </div>
    </main>
  );
}

// ── Delivery log ──────────────────────────────────────────────────────────────

interface DeliveryLogProps {
  webhook: WebhookListItem | null;
  webhookId: number;
}

export function DeliveryLog({ webhook, webhookId }: DeliveryLogProps) {
  const [page, setPage]           = useState(1);
  const [expanded, setExpanded]   = useState<number | null>(null);
  const [notice, setNotice]       = useState<string | null>(null);

  const { data, isLoading, isError, error } = useWebhookDeliveries(webhookId, page, DELIVERY_PAGE_SIZE);
  const redeliver = useRedeliver(webhookId);

  function handleRedeliver(d: WebhookDeliveryDto) {
    setNotice(null);
    redeliver.mutate(d.id, {
      onSuccess: (res) => setNotice(
        res.succeeded
          ? `Redelivered delivery #${d.id}: HTTP ${res.responseStatusCode}.`
          : `Redelivery of #${d.id} failed: ${res.errorMessage ?? `HTTP ${res.responseStatusCode}`}`,
      ),
      onError: (e) => setNotice(`Redelivery of #${d.id} failed: ${errorMessage(e)}`),
    });
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalRows / data.pageSize)) : 1;

  return (
    <section id="webhook-delivery-log" aria-labelledby="delivery-log-heading" className="margin-top-4">
      <h2 id="delivery-log-heading" className="font-heading-lg">
        Deliveries — {webhook?.name ?? `webhook #${webhookId}`}
      </h2>

      {notice && (
        <div className="usa-alert usa-alert--info usa-alert--slim margin-bottom-2" role="status">
          <div className="usa-alert__body"><p className="usa-alert__text">{notice}</p></div>
        </div>
      )}

      {isLoading && <p className="usa-body">Loading deliveries…</p>}
      {isError && (
        <div className="usa-alert usa-alert--error usa-alert--slim" role="alert">
          <div className="usa-alert__body"><p className="usa-alert__text">Failed to load deliveries: {errorMessage(error)}</p></div>
        </div>
      )}

      {data && (
        <>
          <div className="usa-table-container--scrollable" tabIndex={0}>
            <table className="usa-table usa-table--borderless usa-table--compact width-full">
              <caption className="usa-sr-only">Delivery attempts, newest first</caption>
              <thead>
                <tr>
                  <th scope="col">When</th>
                  <th scope="col">Event</th>
                  <th scope="col">Attempt</th>
                  <th scope="col">Result</th>
                  <th scope="col">Detail</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.items.length === 0 && (
                  <tr><td colSpan={6} className="text-italic text-base">No deliveries yet.</td></tr>
                )}
                {data.items.map((d) => (
                  <React.Fragment key={d.id}>
                    <tr data-testid={`delivery-${d.id}`}>
                      <td>{new Date(d.deliveredAt).toLocaleString()}</td>
                      <td><code className="font-code-sm">{d.eventName}</code></td>
                      <td>
                        {d.attemptNumber}
                        {d.redeliveryOfId !== null && (
                          <span className="usa-tag bg-base-light text-ink margin-left-1">redelivery of #{d.redeliveryOfId}</span>
                        )}
                      </td>
                      <td>
                        <span className={`usa-tag ${d.succeeded ? 'bg-success-dark' : 'bg-error-dark'}`}>
                          {d.responseStatusCode !== null ? `HTTP ${d.responseStatusCode}` : 'No response'}
                        </span>
                      </td>
                      <td className="font-body-xs">{d.errorMessage ?? '—'}</td>
                      <td>
                        <RowActions>
                          <button type="button" className="usa-button usa-button--unstyled"
                            aria-expanded={expanded === d.id}
                            onClick={() => setExpanded(expanded === d.id ? null : d.id)}
                            aria-label={`${expanded === d.id ? 'Hide' : 'Show'} payload of delivery ${d.id}`}>
                            Payload
                          </button>
                          {webhook?.isActive && (
                            <button type="button" className="usa-button usa-button--unstyled"
                              onClick={() => handleRedeliver(d)}
                              disabled={redeliver.isPending}
                              aria-label={`Redeliver delivery ${d.id}`}>
                              Redeliver
                            </button>
                          )}
                        </RowActions>
                      </td>
                    </tr>
                    {expanded === d.id && (
                      <tr>
                        <td colSpan={6}>
                          <pre className="font-code-xs bg-base-lightest padding-1 margin-0 overflow-x-auto">{d.payloadJson}</pre>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          </div>

          <AdminPagination
            page={page}
            totalPages={totalPages}
            onPage={setPage}
            ariaLabel="Delivery pagination"
            totalRows={data.totalRows}
            itemLabel="deliveries"
          />
        </>
      )}
    </section>
  );
}
