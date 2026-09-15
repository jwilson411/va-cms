/**
 * Tests for the redirects feature.
 * Issue #48 — BRD FR-NAV-06.
 *
 * Test strategy:
 *   - RedirectsPage renders with mocked API hooks.
 *   - Verifies table columns, create form display, edit form display, deactivate button.
 *   - RedirectForm validates required fields and submits correctly.
 */

import React from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RedirectsPage } from './RedirectsPage';
import { RedirectForm } from './RedirectForm';

// ── Mock data ──────────────────────────────────────────────────────────────────

const mockRow = {
  id: 1,
  fromPath: '/old-path',
  toPath: '/new-path',
  statusCode: 301,
  isActive: true,
  createdById: 42,
  createdByEmail: 'alice@va.gov',
  createdByDisplayName: 'Alice',
  createdAt: '2026-09-15T00:00:00Z',
};

const mockListResponse = {
  items: [mockRow],
  totalRows: 1,
  page: 1,
  pageSize: 50,
};

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockCreate     = vi.fn();
const mockUpdate     = vi.fn();
const mockDeactivate = vi.fn();

vi.mock('./api', () => ({
  useRedirects:        () => ({ data: mockListResponse, isLoading: false, isError: false }),
  useRedirect:         () => ({ data: mockRow }),
  useCreateRedirect:   () => ({ mutate: mockCreate,     isPending: false, isError: false }),
  useUpdateRedirect:   () => ({ mutate: mockUpdate,     isPending: false, isError: false }),
  useDeactivateRedirect: () => ({ mutate: mockDeactivate, isPending: false }),
}));

function Wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

// ── RedirectsPage tests ────────────────────────────────────────────────────────

describe('RedirectsPage', () => {
  beforeEach(() => {
    mockCreate.mockReset();
    mockUpdate.mockReset();
    mockDeactivate.mockReset();
  });

  it('renders table with all required columns', () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    expect(screen.getByText('From Path')).toBeInTheDocument();
    expect(screen.getByText('To Path')).toBeInTheDocument();
    expect(screen.getByText('Code')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Created By')).toBeInTheDocument();
  });

  it('shows row data: fromPath, toPath, statusCode, createdBy', () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    expect(screen.getByText('/old-path')).toBeInTheDocument();
    expect(screen.getByText('/new-path')).toBeInTheDocument();
    expect(screen.getByText('301')).toBeInTheDocument();
    expect(screen.getByText('Alice')).toBeInTheDocument();
  });

  it('shows Deactivate button only for active rows', () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    expect(screen.getByRole('button', { name: /deactivate redirect from \/old-path/i }))
      .toBeInTheDocument();
  });

  it('shows New Redirect button and create form on click', async () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    fireEvent.click(screen.getByRole('button', { name: /new redirect/i }));
    await waitFor(() => expect(screen.getByLabelText(/from path/i)).toBeInTheDocument());
  });

  it('shows edit form when Edit is clicked', async () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    fireEvent.click(screen.getByRole('button', { name: /edit redirect/i }));
    await waitFor(() => {
      const input = screen.getByRole('textbox', { name: /from path/i });
      expect((input as HTMLInputElement).value).toBe('/old-path');
    });
  });

  it('calls deactivate mutation when Deactivate is clicked', async () => {
    render(<Wrapper><RedirectsPage /></Wrapper>);
    fireEvent.click(screen.getByRole('button', { name: /deactivate redirect from \/old-path/i }));
    await waitFor(() => expect(mockDeactivate).toHaveBeenCalledWith(1, expect.anything()));
  });
});

// ── RedirectForm tests ─────────────────────────────────────────────────────────

describe('RedirectForm', () => {
  it('renders required fields with labels', () => {
    render(<RedirectForm onSubmit={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByLabelText(/from path/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to path/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/status code/i)).toBeInTheDocument();
  });

  it('shows validation error when From Path is empty', async () => {
    render(<RedirectForm onSubmit={vi.fn()} onCancel={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /create redirect/i }));
    await waitFor(() =>
      expect(screen.getByText('From Path is required.')).toBeInTheDocument()
    );
  });

  it('shows validation error when From Path missing leading slash', async () => {
    render(<RedirectForm onSubmit={vi.fn()} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText(/from path/i), { target: { value: 'no-slash' } });
    fireEvent.change(screen.getByLabelText(/to path/i), { target: { value: '/dest' } });
    fireEvent.click(screen.getByRole('button', { name: /create redirect/i }));
    await waitFor(() =>
      expect(screen.getByText('From Path must start with /.')).toBeInTheDocument()
    );
  });

  it('shows validation error when To Path is empty', async () => {
    render(<RedirectForm onSubmit={vi.fn()} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText(/from path/i), { target: { value: '/old' } });
    fireEvent.click(screen.getByRole('button', { name: /create redirect/i }));
    await waitFor(() =>
      expect(screen.getByText('To Path is required.')).toBeInTheDocument()
    );
  });

  it('calls onSubmit with trimmed values and status code', () => {
    const onSubmit = vi.fn();
    render(<RedirectForm onSubmit={onSubmit} onCancel={vi.fn()} />);
    const inputs = screen.getAllByRole('textbox');
    // Inputs are ordered: From Path (index 0), To Path (index 1)
    fireEvent.change(inputs[0], { target: { value: '/old' } });
    fireEvent.change(inputs[1], { target: { value: '/new' } });
    const form = screen.getByTestId('redirect-form');
    fireEvent.submit(form);
    expect(onSubmit).toHaveBeenCalledWith({ fromPath: '/old', toPath: '/new', statusCode: 301 });
  });

  it('pre-fills fields from existing redirect in edit mode', () => {
    render(
      <RedirectForm
        existing={mockRow}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />
    );
    expect((screen.getByLabelText(/from path/i) as HTMLInputElement).value).toBe('/old-path');
    expect((screen.getByLabelText(/to path/i) as HTMLInputElement).value).toBe('/new-path');
    expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument();
  });

  it('calls onCancel when Cancel is clicked', () => {
    const onCancel = vi.fn();
    render(<RedirectForm onSubmit={vi.fn()} onCancel={onCancel} />);
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalled();
  });

  it('shows submit error message when submitError is set', () => {
    render(
      <RedirectForm
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
        submitError="Server error: conflict"
      />
    );
    expect(screen.getByText('Server error: conflict')).toBeInTheDocument();
  });
});
