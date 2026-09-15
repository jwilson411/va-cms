/**
 * Tests for NavigationEditorPage component.
 * Issue #46 — BRD FR-NAV-01, FR-NAV-03.
 *
 * Test strategy:
 *   - Renders with mocked API hooks.
 *   - Verifies tree is displayed, preview toggle works, item form renders.
 */

import React from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NavigationEditorPage } from './NavigationEditorPage';

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockMenus = [
  { id: 1, name: 'Primary Nav', handle: 'primary', createdAt: '', updatedAt: '' },
];

const mockItems = {
  menuId: 1,
  handle: 'primary',
  items: [
    {
      id: 10, menuId: 1, parentItemId: null, label: 'Home',
      url: '/', contentEntryId: null, target: '_self',
      sortOrder: 0, isVisible: true, depth: 0,
    },
    {
      id: 11, menuId: 1, parentItemId: null, label: 'About',
      url: '/about', contentEntryId: null, target: '_self',
      sortOrder: 1, isVisible: true, depth: 0,
    },
  ],
};

const mockPreview = {
  handle: 'primary',
  name: 'Primary Nav',
  items: [
    {
      id: 10, label: 'Home', url: '/', target: '_self',
      isVisible: true, depth: 0, children: [],
    },
  ],
};

vi.mock('./api', () => ({
  useNavigationMenus: () => ({ data: mockMenus, isLoading: false }),
  useMenuItems: () => ({ data: mockItems, isLoading: false }),
  useMenuPreview: (_handle: string, enabled: boolean) => ({
    data: enabled ? mockPreview : undefined,
    isLoading: false,
  }),
  useCreateItem: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateItem: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteItem: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useBulkReorder: () => ({ mutateAsync: vi.fn(), isPending: false, isError: false }),
  useCreateMenu: () => ({ mutateAsync: vi.fn() }),
  useUpdateMenu: () => ({ mutateAsync: vi.fn() }),
  useDeleteMenu: () => ({ mutateAsync: vi.fn() }),
}));

// ── Test setup ────────────────────────────────────────────────────────────────

function makeWrapper(): React.FC<{ children: React.ReactNode }> {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('NavigationEditorPage', () => {
  it('renders the page heading', () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    expect(screen.getByRole('heading', { name: /navigation menus/i })).toBeTruthy();
  });

  it('renders the menu selector dropdown', () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    expect(screen.getByTestId('menu-select')).toBeTruthy();
  });

  it('shows menu options from API', () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    const select = screen.getByTestId('menu-select') as HTMLSelectElement;
    const options = Array.from(select.options).map((o) => o.text);
    expect(options.some((t) => t.includes('Primary Nav'))).toBe(true);
  });

  it('shows nav tree after selecting a menu', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });

    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => {
      expect(screen.getByTestId('nav-tree')).toBeTruthy();
    });
  });

  it('shows nav items in the tree', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => {
      expect(screen.getByText('Home')).toBeTruthy();
      expect(screen.getByText('About')).toBeTruthy();
    });
  });

  it('shows preview panel when Preview button is clicked', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => screen.getByTestId('preview-toggle'));
    fireEvent.click(screen.getByTestId('preview-toggle'));

    await waitFor(() => {
      expect(screen.getByTestId('preview-section')).toBeTruthy();
    });
  });

  it('shows preview panel with nav-preview-panel inside', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => screen.getByTestId('preview-toggle'));
    fireEvent.click(screen.getByTestId('preview-toggle'));

    await waitFor(() => {
      expect(screen.getByTestId('nav-preview-panel')).toBeTruthy();
    });
  });

  it('hides preview panel when Preview button is clicked again', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => screen.getByTestId('preview-toggle'));
    fireEvent.click(screen.getByTestId('preview-toggle'));
    await waitFor(() => screen.getByTestId('preview-section'));
    fireEvent.click(screen.getByTestId('preview-toggle'));

    await waitFor(() => {
      expect(screen.queryByTestId('preview-section')).toBeNull();
    });
  });

  it('shows add item form when "Add item" is clicked', async () => {
    render(<NavigationEditorPage />, { wrapper: makeWrapper() });
    fireEvent.change(screen.getByTestId('menu-select'), {
      target: { value: 'primary' },
    });

    await waitFor(() => screen.getByText('+ Add item'));
    fireEvent.click(screen.getByText('+ Add item'));

    await waitFor(() => {
      expect(screen.getByTestId('nav-item-form')).toBeTruthy();
    });
  });
});

// ── NavPreviewPanel ───────────────────────────────────────────────────────────

import { NavPreviewPanel } from './NavPreviewPanel';

describe('NavPreviewPanel', () => {
  it('renders menu name in heading', () => {
    render(
      <NavPreviewPanel
        menuName="Primary Nav"
        items={[]}
      />
    );
    expect(screen.getByText(/Primary Nav/)).toBeTruthy();
  });

  it('renders items', () => {
    const items = [
      {
        id: 1, label: 'Home', url: '/', target: '_self',
        isVisible: true, depth: 0, children: [],
      },
    ];
    render(<NavPreviewPanel menuName="Test" items={items} />);
    expect(screen.getByText('Home')).toBeTruthy();
  });

  it('shows hidden items with reduced opacity', () => {
    const items = [
      {
        id: 1, label: 'Hidden Item', url: '/', target: '_self',
        isVisible: false, depth: 0, children: [],
      },
    ];
    render(<NavPreviewPanel menuName="Test" items={items} />);
    const li = screen.getByText('Hidden Item').closest('li');
    expect(li?.className).toContain('opacity-50');
  });
});

// ── NavItemForm ───────────────────────────────────────────────────────────────

import { NavItemForm } from './NavItemForm';

describe('NavItemForm', () => {
  const noop = () => undefined;

  it('renders "Add navigation item" heading when item is null', () => {
    render(
      <NavItemForm
        item={null}
        parentOptions={[]}
        onSubmit={noop}
        onCancel={noop}
        isLoading={false}
        error={null}
      />
    );
    expect(screen.getByText('Add navigation item')).toBeTruthy();
  });

  it('renders "Edit navigation item" heading when item is provided', () => {
    const node = {
      id: 1, menuId: 1, parentItemId: null, label: 'Home',
      url: '/', contentEntryId: null, target: '_self',
      sortOrder: 0, isVisible: true, depth: 0, children: [],
    };
    render(
      <NavItemForm
        item={node}
        parentOptions={[]}
        onSubmit={noop}
        onCancel={noop}
        isLoading={false}
        error={null}
      />
    );
    expect(screen.getByText('Edit navigation item')).toBeTruthy();
  });

  it('shows validation error when label is empty on submit', () => {
    render(
      <NavItemForm
        item={null}
        parentOptions={[]}
        onSubmit={noop}
        onCancel={noop}
        isLoading={false}
        error={null}
      />
    );
    const form = screen.getByTestId('nav-item-form').querySelector('form')!;
    fireEvent.submit(form);
    expect(screen.getByText('Label is required.')).toBeTruthy();
  });

  it('shows API error when error prop is set', () => {
    render(
      <NavItemForm
        item={null}
        parentOptions={[]}
        onSubmit={noop}
        onCancel={noop}
        isLoading={false}
        error="Navigation items cannot be nested more than 3 levels deep."
      />
    );
    expect(
      screen.getByText('Navigation items cannot be nested more than 3 levels deep.')
    ).toBeTruthy();
  });

  it('calls onCancel when Cancel is clicked', () => {
    const onCancel = vi.fn();
    render(
      <NavItemForm
        item={null}
        parentOptions={[]}
        onSubmit={noop}
        onCancel={onCancel}
        isLoading={false}
        error={null}
      />
    );
    fireEvent.click(screen.getByText('Cancel'));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('calls onSubmit with trimmed label when form is valid', () => {
    const onSubmit = vi.fn();
    render(
      <NavItemForm
        item={null}
        parentOptions={[]}
        onSubmit={onSubmit}
        onCancel={noop}
        isLoading={false}
        error={null}
      />
    );
    const labelInput = screen.getByLabelText(/Label/i) as HTMLInputElement;
    fireEvent.change(labelInput, { target: { value: '  Contact  ' } });
    const form = screen.getByTestId('nav-item-form').querySelector('form')!;
    fireEvent.submit(form);
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ label: 'Contact' })
    );
  });
});
