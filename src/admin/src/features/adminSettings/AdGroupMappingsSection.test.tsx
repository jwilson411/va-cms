/**
 * Tests for AdGroupMappingsSection (story #67).
 *
 * AC1: Section renders with heading "AD Group Mappings"
 * AC2: Add form has AD Group Name input + CMS Role select + submit button
 * AC3: Empty state and populated list both render correctly
 */

import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AdGroupMappingsSection } from './AdGroupMappingsSection';

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock('./useAdGroupMappings', () => ({
  useAdGroupMappings: vi.fn(),
  useCreateAdGroupMapping: vi.fn(),
  useDeleteAdGroupMapping: vi.fn(),
}));

import {
  useAdGroupMappings,
  useCreateAdGroupMapping,
  useDeleteAdGroupMapping,
} from './useAdGroupMappings';

function renderInProvider(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('AdGroupMappingsSection', () => {
  beforeEach(() => {
    vi.mocked(useCreateAdGroupMapping).mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useCreateAdGroupMapping>);

    vi.mocked(useDeleteAdGroupMapping).mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteAdGroupMapping>);
  });

  // AC1: Section renders
  it('renders the AD Group Mappings section heading', () => {
    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);
    expect(screen.getByRole('heading', { name: /ad group mappings/i })).toBeDefined();
  });

  // AC2: Form has required inputs
  it('renders the add mapping form with group name input and role select', () => {
    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);

    expect(screen.getByLabelText(/ad group name/i)).toBeDefined();
    expect(screen.getByLabelText(/cms role/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /add mapping/i })).toBeDefined();
  });

  // AC2: Form validation — empty group name shows error
  it('shows an error when submitting with an empty AD group name', async () => {
    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);

    const form = screen.getByRole('form', { name: /add ad group mapping/i });
    fireEvent.submit(form);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeDefined();
      expect(screen.getByText(/ad group name is required/i)).toBeDefined();
    });
  });

  // AC3: Populated list renders rows
  it('renders existing mappings in a table', () => {
    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [
        {
          id: 1,
          adGroup: 'VA-CMS-Editors',
          roleId: 2,
          roleName: 'Editor',
          createdById: 1,
          createdAt: '2026-09-01T00:00:00Z',
          updatedAt: '2026-09-01T00:00:00Z',
        },
        {
          id: 2,
          adGroup: 'VA-CMS-SiteAdmins',
          roleId: 3,
          roleName: 'SiteAdmin',
          createdById: 1,
          createdAt: '2026-09-02T00:00:00Z',
          updatedAt: '2026-09-02T00:00:00Z',
        },
      ],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);

    // Check the table body (not the select options) contains the mapped group names
    expect(screen.getByText('VA-CMS-Editors')).toBeDefined();
    expect(screen.getByText('VA-CMS-SiteAdmins')).toBeDefined();
    // Role names appear in the table — use getAllByText since "Editor" may also appear in the select
    expect(screen.getAllByText('Editor').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('SiteAdmin').length).toBeGreaterThanOrEqual(1);
  });

  // AC3: Empty state message
  it('renders empty state when no mappings exist', () => {
    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);
    expect(screen.getByText(/no mappings configured/i)).toBeDefined();
  });

  // AC2: Calls mutate on valid submit
  it('calls createMutation.mutate with trimmed group name and selected role', async () => {
    const mutateFn = vi.fn();
    vi.mocked(useCreateAdGroupMapping).mockReturnValue({
      mutate: mutateFn,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateAdGroupMapping>);

    vi.mocked(useAdGroupMappings).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useAdGroupMappings>);

    renderInProvider(<AdGroupMappingsSection />);

    const input = screen.getByLabelText(/ad group name/i);
    fireEvent.change(input, { target: { value: '  VA-CMS-Editors  ' } });

    const form = screen.getByRole('form', { name: /add ad group mapping/i });
    fireEvent.submit(form);

    await waitFor(() => {
      expect(mutateFn).toHaveBeenCalledWith(
        { adGroup: 'VA-CMS-Editors', roleId: 2 },
        expect.any(Object),
      );
    });
  });
});
