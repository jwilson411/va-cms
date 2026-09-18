/**
 * Tests for the webhooks feature (issues #54 / #168).
 *   - WebhooksPage lists webhooks, opens the registration form, shows the one-time secret.
 *   - DeliveryLog renders attempts with status, redelivery lineage and a Redeliver action.
 *   - WebhookForm validates URL scheme and event selection before calling the API.
 */

import React from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { WebhooksPage } from './WebhooksPage';
import { WebhookForm } from './WebhookForm';
import { WebhookApiError } from './api';

// ── Mock data ──────────────────────────────────────────────────────────────────

const hook = {
  id: 7,
  name: 'Public site revalidate',
  url: 'https://www.va.gov/api/revalidate',
  events: ['content.published', 'settings.updated'],
  isActive: true,
  createdAt: '2026-09-15T00:00:00Z',
};

const deliveries = {
  items: [
    { id: 12, webhookId: 7, eventName: 'content.published', payloadJson: '{"handle":"abc"}', responseStatusCode: 200,
      attemptNumber: 1, deliveredAt: '2026-09-16T10:00:00Z', errorMessage: null, redeliveryOfId: 11, succeeded: true },
    { id: 11, webhookId: 7, eventName: 'content.published', payloadJson: '{"handle":"abc"}', responseStatusCode: null,
      attemptNumber: 3, deliveredAt: '2026-09-16T09:00:00Z', errorMessage: "Refused: Host 'www.va.gov' resolves to the private address 10.0.0.1",
      redeliveryOfId: null, succeeded: false },
  ],
  page: 1, pageSize: 25, totalRows: 2,
};

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockRegister  = vi.fn();
const mockDelete    = vi.fn();
const mockRedeliver = vi.fn();
let listState: { data?: typeof hook[]; isLoading: boolean; isError: boolean; error?: unknown } =
  { data: [hook], isLoading: false, isError: false };

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>();
  return {
    ...actual,
    useWebhooks:           () => listState,
    useWebhookDeliveries:  () => ({ data: deliveries, isLoading: false, isError: false }),
    useRegisterWebhook:    () => ({ mutate: mockRegister, isPending: false, isError: false, reset: vi.fn() }),
    useDeleteWebhook:      () => ({ mutate: mockDelete, isPending: false }),
    useRedeliver:          () => ({ mutate: mockRedeliver, isPending: false }),
  };
});

function Wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  vi.clearAllMocks();
  listState = { data: [hook], isLoading: false, isError: false };
});

// ── WebhooksPage ───────────────────────────────────────────────────────────────

describe('WebhooksPage', () => {
  it('lists registered webhooks with url, events and status', () => {
    render(<WebhooksPage />, { wrapper: Wrapper });
    expect(screen.getByRole('heading', { level: 1, name: 'Webhooks' })).toBeInTheDocument();
    expect(screen.getByText('Public site revalidate')).toBeInTheDocument();
    expect(screen.getByText('https://www.va.gov/api/revalidate')).toBeInTheDocument();
    expect(screen.getByText('content.published, settings.updated')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('opens the registration form and shows the secret once after success', async () => {
    mockRegister.mockImplementation((_values, opts) =>
      opts.onSuccess({ id: 8, name: 'n', url: 'https://hooks.va.gov/x', secret: 'deadbeef', events: ['content.published'] }));

    render(<WebhooksPage />, { wrapper: Wrapper });
    fireEvent.click(screen.getByRole('button', { name: '+ Register webhook' }));
    fireEvent.change(screen.getByLabelText(/Endpoint URL/), { target: { value: 'https://hooks.va.gov/x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Register webhook' }));

    await waitFor(() => expect(mockRegister).toHaveBeenCalled());
    expect(mockRegister.mock.calls[0][0]).toEqual({ name: undefined, url: 'https://hooks.va.gov/x', secret: undefined, events: ['content.published'] });
    expect(screen.getByTestId('webhook-secret')).toHaveTextContent('deadbeef');
    expect(screen.getByText(/will not be shown again/)).toBeInTheDocument();
  });

  it('shows the delivery log with status tags, redelivery lineage and a Redeliver button', () => {
    render(<WebhooksPage />, { wrapper: Wrapper });
    fireEvent.click(screen.getByRole('button', { name: 'Show deliveries for Public site revalidate' }));

    expect(screen.getByRole('heading', { level: 2, name: /Deliveries — Public site revalidate/ })).toBeInTheDocument();
    expect(screen.getByText('HTTP 200')).toBeInTheDocument();
    expect(screen.getByText('No response')).toBeInTheDocument();
    expect(screen.getByText('redelivery of #11')).toBeInTheDocument();
    expect(screen.getByText(/resolves to the private address 10.0.0.1/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Redeliver delivery 11' }));
    expect(mockRedeliver).toHaveBeenCalledWith(11, expect.anything());

    fireEvent.click(screen.getByRole('button', { name: 'Show payload of delivery 12' }));
    expect(screen.getByText('{"handle":"abc"}')).toBeInTheDocument();
  });

  it('removes a webhook', () => {
    render(<WebhooksPage />, { wrapper: Wrapper });
    fireEvent.click(screen.getByRole('button', { name: 'Remove webhook Public site revalidate' }));
    expect(mockDelete).toHaveBeenCalledWith(7, expect.anything());
  });

  it('explains a 403 instead of a generic failure', () => {
    listState = { isLoading: false, isError: true, error: new WebhookApiError(403, 'Forbidden') };
    render(<WebhooksPage />, { wrapper: Wrapper });
    expect(screen.getByRole('alert')).toHaveTextContent('Developer role');
    expect(screen.getByRole('button', { name: '+ Register webhook' })).toBeDisabled();
  });
});

// ── WebhookForm ────────────────────────────────────────────────────────────────

describe('WebhookForm', () => {
  it('rejects a non-http(s) URL and an empty event list before submitting', () => {
    const onSubmit = vi.fn();
    render(<WebhookForm onSubmit={onSubmit} onCancel={() => {}} isSubmitting={false} submitError={null} />);

    fireEvent.change(screen.getByLabelText(/Endpoint URL/), { target: { value: 'ftp://hooks.va.gov' } });
    fireEvent.click(screen.getByRole('button', { name: 'Register webhook' }));
    expect(screen.getByRole('alert')).toHaveTextContent('https://');
    expect(onSubmit).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/Endpoint URL/), { target: { value: 'https://hooks.va.gov' } });
    fireEvent.click(screen.getByLabelText('content.published'));   // untick the default
    fireEvent.click(screen.getByRole('button', { name: 'Register webhook' }));
    expect(screen.getByRole('alert')).toHaveTextContent('at least one event');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('surfaces the API validation message', () => {
    render(<WebhookForm onSubmit={() => {}} onCancel={() => {}} isSubmitting={false}
      submitError="Host 'evil.example' is not on the webhooks.allowedHosts list." />);
    expect(screen.getByRole('alert')).toHaveTextContent('webhooks.allowedHosts');
  });
});
