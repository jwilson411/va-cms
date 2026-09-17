/**
 * Tests for SiteSettingsSection (issue #143, epic #141).
 *
 *   - Groups settings by category with the right input per data type.
 *   - Bool toggles save immediately; text/number/json save via the Save button.
 *   - Reset is offered only for overridden values.
 *   - API validation errors are surfaced next to the field.
 */

import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { SiteSettingsSection } from './SiteSettingsSection';
import type { SiteSettingDto } from './useSiteSettingsAdmin';

vi.mock('./useSiteSettingsAdmin', () => ({
  useSiteSettingsAdmin: vi.fn(),
  useSetSiteSetting: vi.fn(),
  useResetSiteSetting: vi.fn(),
}));

import { useSiteSettingsAdmin, useSetSiteSetting, useResetSiteSetting } from './useSiteSettingsAdmin';

function dto(partial: Partial<SiteSettingDto> & Pick<SiteSettingDto, 'key' | 'dataType' | 'category'>): SiteSettingDto {
  return {
    value: null,
    defaultValue: 'x',
    effectiveValue: 'x',
    isOverridden: false,
    scope: 'Server',
    description: `About ${partial.key}`,
    sortOrder: 0,
    updatedById: null,
    updatedByName: null,
    updatedAt: '2026-09-16T00:00:00Z',
    ...partial,
  };
}

const ITEMS: SiteSettingDto[] = [
  dto({ key: 'features.webhooks', dataType: 'bool', category: 'Features', defaultValue: 'true', effectiveValue: 'true' }),
  dto({ key: 'api.maxPageSize', dataType: 'int', category: 'Api', defaultValue: '200', effectiveValue: '200' }),
  dto({
    key: 'site.title', dataType: 'string', category: 'Site', scope: 'Public',
    defaultValue: 'Department of Veterans Affairs', value: 'VA Benefits', effectiveValue: 'VA Benefits',
    isOverridden: true, updatedByName: 'Alice Smith',
  }),
  dto({ key: 'webhooks.retryDelaysSeconds', dataType: 'json', category: 'Webhooks', defaultValue: '[5,25]', effectiveValue: '[5,25]' }),
];

function renderInProvider(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('SiteSettingsSection', () => {
  const setMutate = vi.fn();
  const resetMutate = vi.fn();

  beforeEach(() => {
    setMutate.mockReset();
    resetMutate.mockReset();
    vi.mocked(useSiteSettingsAdmin).mockReturnValue({
      data: { items: ITEMS, snapshotLoadedAtUtc: '2026-09-16T00:00:00Z' },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useSiteSettingsAdmin>);
    vi.mocked(useSetSiteSetting).mockReturnValue({ mutate: setMutate, isPending: false } as unknown as ReturnType<typeof useSetSiteSetting>);
    vi.mocked(useResetSiteSetting).mockReturnValue({ mutate: resetMutate, isPending: false } as unknown as ReturnType<typeof useResetSiteSetting>);
  });

  it('renders one fieldset per category in the canonical order with typed inputs', () => {
    renderInProvider(<SiteSettingsSection />);
    expect(screen.getByRole('heading', { name: /site settings/i })).toBeDefined();

    const legends = screen.getAllByText(/^(Site|Features|Webhooks|Api)$/).map((el) => el.textContent);
    expect(legends).toEqual(['Site', 'Features', 'Webhooks', 'Api']);

    expect(screen.getByLabelText('features.webhooks')).toHaveProperty('type', 'checkbox');
    expect(screen.getByLabelText('api.maxPageSize')).toHaveProperty('type', 'number');
    expect(screen.getByLabelText('site.title')).toHaveProperty('type', 'text');
    expect(screen.getByLabelText('webhooks.retryDelaysSeconds').tagName).toBe('TEXTAREA');
  });

  it('bool toggles save immediately', () => {
    renderInProvider(<SiteSettingsSection />);
    const checkbox = screen.getByLabelText('features.webhooks') as HTMLInputElement;
    expect(checkbox.checked).toBe(true);

    fireEvent.click(checkbox);

    expect(setMutate).toHaveBeenCalledWith({ key: 'features.webhooks', value: 'false' }, expect.anything());
  });

  it('text values save only when dirty, via Save or Enter', () => {
    renderInProvider(<SiteSettingsSection />);
    const row = screen.getByTestId('setting-row-api.maxPageSize');
    const save = within(row).getByRole('button', { name: /save api.maxPageSize/i }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);

    fireEvent.change(within(row).getByLabelText('api.maxPageSize'), { target: { value: '50' } });
    expect(save.disabled).toBe(false);
    fireEvent.click(save);

    expect(setMutate).toHaveBeenCalledWith({ key: 'api.maxPageSize', value: '50' }, expect.anything());
  });

  it('offers reset only for overridden settings and shows who changed it', () => {
    renderInProvider(<SiteSettingsSection />);
    const titleRow = screen.getByTestId('setting-row-site.title');
    expect(within(titleRow).getByText(/changed by Alice Smith/)).toBeDefined();
    fireEvent.click(within(titleRow).getByRole('button', { name: /reset site.title to default/i }));
    expect(resetMutate).toHaveBeenCalledWith('site.title', expect.anything());

    const apiRow = screen.getByTestId('setting-row-api.maxPageSize');
    expect(within(apiRow).queryByRole('button', { name: /reset/i })).toBeNull();
  });

  it('surfaces a validation error from the API beside the field', async () => {
    setMutate.mockImplementation((_vars, opts?: { onError?: (e: Error) => void }) => {
      opts?.onError?.(new Error("'api.maxPageSize' must be a whole number."));
    });
    renderInProvider(<SiteSettingsSection />);
    const row = screen.getByTestId('setting-row-api.maxPageSize');
    fireEvent.change(within(row).getByLabelText('api.maxPageSize'), { target: { value: '12' } });
    fireEvent.click(within(row).getByRole('button', { name: /save api.maxPageSize/i }));

    await waitFor(() => expect(within(row).getByRole('alert').textContent).toMatch(/whole number/));
    expect(within(row).getByLabelText('api.maxPageSize').getAttribute('aria-describedby')).toContain('status');
  });

  it('shows loading and error states', () => {
    vi.mocked(useSiteSettingsAdmin).mockReturnValue({ data: undefined, isLoading: true, isError: false } as unknown as ReturnType<typeof useSiteSettingsAdmin>);
    const { unmount } = renderInProvider(<SiteSettingsSection />);
    expect(screen.getByText(/loading settings/i)).toBeDefined();
    unmount();

    vi.mocked(useSiteSettingsAdmin).mockReturnValue({ data: undefined, isLoading: false, isError: true } as unknown as ReturnType<typeof useSiteSettingsAdmin>);
    renderInProvider(<SiteSettingsSection />);
    expect(screen.getByRole('alert').textContent).toMatch(/failed to load settings/i);
  });
});
