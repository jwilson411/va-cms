/**
 * Tests for NotificationBell — issue #38 (FR-WORKFLOW-02/03).
 *
 * Covers the acceptance criteria that live in the SPA:
 *   - bell shows an unread-count badge (and none at zero)
 *   - panel lists description, content title, timestamp and a link to the entry
 *   - opening the panel marks the listed unread notifications as read
 *   - "Mark all as read" calls the read-all mutation
 *   - Escape closes the panel and returns focus to the bell
 */

import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { NotificationBell } from './NotificationBell';
import * as hooks from './useNotifications';

// Site settings (epic #141): render with the code defaults, no QueryClient needed.
import * as siteSettings from '../siteSettings/useClientSettings';

vi.mock('../siteSettings/useClientSettings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../siteSettings/useClientSettings')>();
  return {
    ...actual,
    useClientSettings: vi.fn(() => actual.buildClientSettings({}, { isLoading: false, isError: false })),
  };
});


vi.mock('./useNotifications');

const mockUseNotifications = vi.mocked(hooks.useNotifications);
const mockUseMarkRead      = vi.mocked(hooks.useMarkNotificationsRead);
const mockUseMarkAllRead   = vi.mocked(hooks.useMarkAllNotificationsRead);

function buildItem(overrides: Partial<hooks.NotificationItem> = {}): hooks.NotificationItem {
  return {
    id:               1,
    eventType:        'ReviewRequested',
    contentEntryId:   42,
    contentTitle:     'Benefits Overview',
    message:          'Alice submitted "Benefits Overview" for review',
    actorId:          7,
    actorDisplayName: 'Alice',
    comment:          null,
    isRead:           false,
    readAt:           null,
    createdAt:        new Date(Date.now() - 5 * 60_000).toISOString(),
    entrySlug:        'hr/benefits',
    entryStatus:      'InReview',
    ...overrides,
  };
}

function mockList(items: hooks.NotificationItem[], opts: { isLoading?: boolean; isError?: boolean } = {}) {
  mockUseNotifications.mockReturnValue({
    data: opts.isLoading || opts.isError ? undefined : { items, unreadCount: items.filter((i) => !i.isRead).length },
    isLoading: opts.isLoading ?? false,
    isError: opts.isError ?? false,
  } as unknown as ReturnType<typeof hooks.useNotifications>);
}

function mockMutations() {
  const markRead = vi.fn();
  const markAllRead = vi.fn();
  mockUseMarkRead.mockReturnValue({ mutate: markRead, isPending: false } as unknown as ReturnType<typeof hooks.useMarkNotificationsRead>);
  mockUseMarkAllRead.mockReturnValue({ mutate: markAllRead, isPending: false } as unknown as ReturnType<typeof hooks.useMarkAllNotificationsRead>);
  return { markRead, markAllRead };
}

function renderBell() {
  return render(
    <MemoryRouter>
      <NotificationBell />
    </MemoryRouter>,
  );
}

describe('NotificationBell', () => {
  beforeEach(() => {
    mockList([]);
    mockMutations();
  });

  // ── AC1: badge ─────────────────────────────────────────────────────────────

  it('shows no badge and a plain label when there is nothing unread', () => {
    renderBell();
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument();
    expect(screen.queryByTestId('notification-badge')).not.toBeInTheDocument();
  });

  it('shows the unread count in the badge and the accessible name', () => {
    mockList([buildItem({ id: 1 }), buildItem({ id: 2 }), buildItem({ id: 3, isRead: true })]);
    renderBell();
    expect(screen.getByTestId('notification-badge')).toHaveTextContent('2');
    expect(screen.getByRole('button', { name: 'Notifications, 2 unread' })).toBeInTheDocument();
  });

  it('caps the badge at 99+', () => {
    mockUseNotifications.mockReturnValue({
      data: { items: [buildItem()], unreadCount: 150 },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useNotifications>);
    renderBell();
    expect(screen.getByTestId('notification-badge')).toHaveTextContent('99+');
  });

  // ── AC2: panel contents ────────────────────────────────────────────────────

  it('is closed until the bell is clicked, then lists description, title, timestamp and link', () => {
    mockList([
      buildItem({ id: 1 }),
      buildItem({
        id: 2,
        eventType: 'ContentReturned',
        contentEntryId: 43,
        contentTitle: 'Returned Page',
        message: 'Bob returned "Returned Page" to draft',
        comment: 'Fix the heading',
        isRead: true,
      }),
    ]);
    renderBell();

    const bell = screen.getByRole('button', { name: /Notifications/ });
    expect(bell).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();

    fireEvent.click(bell);
    expect(bell).toHaveAttribute('aria-expanded', 'true');

    const panel = screen.getByRole('region', { name: 'Notifications' });
    const first = within(panel).getByTestId('notification-1');
    expect(first).toHaveTextContent('Alice submitted "Benefits Overview" for review');
    expect(first).toHaveTextContent('Benefits Overview');
    expect(first).toHaveTextContent('5 min ago');
    expect(within(first).getByRole('link')).toHaveAttribute('href', '/admin/content/42/edit');
    expect(first).toHaveClass('va-cms-notifications__item--unread');

    const second = within(panel).getByTestId('notification-2');
    expect(second).toHaveTextContent('“Fix the heading”');
    expect(within(second).getByRole('link')).toHaveAttribute('href', '/admin/content/43/edit');
    expect(second).not.toHaveClass('va-cms-notifications__item--unread');
  });

  it('shows an empty state when there are no notifications', () => {
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(screen.getByText('You have no notifications.')).toBeInTheDocument();
  });

  it('shows an error when the inbox cannot be loaded', () => {
    mockList([], { isError: true });
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(screen.getByRole('alert')).toHaveTextContent('Could not load notifications.');
  });

  // ── AC5: marked read when viewed ───────────────────────────────────────────

  it('marks the listed unread notifications read when the panel is opened', () => {
    mockList([buildItem({ id: 1 }), buildItem({ id: 2, isRead: true }), buildItem({ id: 3 })]);
    const { markRead } = mockMutations();
    renderBell();

    expect(markRead).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(markRead).toHaveBeenCalledTimes(1);
    expect(markRead).toHaveBeenCalledWith([1, 3]);
  });

  it('does not call mark-read when everything listed is already read', () => {
    mockList([buildItem({ id: 1, isRead: true })]);
    const { markRead } = mockMutations();
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(markRead).not.toHaveBeenCalled();
  });

  it('"Mark all as read" calls the read-all mutation and is disabled at zero unread', () => {
    mockList([buildItem({ id: 1 })]);
    const { markAllRead } = mockMutations();
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));

    const markAll = screen.getByRole('button', { name: 'Mark all as read' });
    expect(markAll).toBeEnabled();
    fireEvent.click(markAll);
    expect(markAllRead).toHaveBeenCalledTimes(1);
  });

  it('disables "Mark all as read" when nothing is unread', () => {
    mockList([buildItem({ id: 1, isRead: true })]);
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(screen.getByRole('button', { name: 'Mark all as read' })).toBeDisabled();
  });

  // ── keyboard / dismissal ───────────────────────────────────────────────────

  it('closes on Escape and returns focus to the bell', () => {
    mockList([buildItem()]);
    renderBell();
    const bell = screen.getByRole('button', { name: /Notifications/ });
    fireEvent.click(bell);
    expect(screen.getByRole('region', { name: 'Notifications' })).toBeInTheDocument();

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();
    expect(bell).toHaveFocus();
  });

  it('closes when clicking outside the panel', () => {
    mockList([buildItem()]);
    render(
      <MemoryRouter>
        <p data-testid="outside">elsewhere</p>
        <NotificationBell />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    expect(screen.getByRole('region', { name: 'Notifications' })).toBeInTheDocument();

    fireEvent.mouseDown(screen.getByTestId('outside'));
    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();
  });

  it('closes when a notification link is followed', () => {
    mockList([buildItem()]);
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
    fireEvent.click(within(screen.getByTestId('notification-1')).getByRole('link'));
    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();
  });

  it('renders nothing while features.notifications is off (epic #141)', () => {
    vi.mocked(siteSettings.useClientSettings).mockReturnValueOnce(
      siteSettings.buildClientSettings({ 'features.notifications': 'false' }, { isLoading: false, isError: false }),
    );
    mockUseNotifications.mockReturnValue({ data: undefined, isLoading: false, isError: false } as never);
    render(
      <MemoryRouter>
        <NotificationBell />
      </MemoryRouter>,
    );
    expect(screen.queryByTestId('notification-bell')).toBeNull();
    expect(mockUseNotifications).toHaveBeenCalledWith(undefined, { enabled: false });
  });
});
