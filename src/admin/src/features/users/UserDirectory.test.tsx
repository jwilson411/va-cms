/**
 * Tests for UserListPage and UserDetailPage — issue #56 (FR-USERS-03/04).
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { UserListPage } from './UserListPage';
import { UserDetailPage } from './UserDetailPage';
import * as hooks from './useUsers';

// ── Mock hooks ────────────────────────────────────────────────────────────────

vi.mock('./useUsers');

const mockUseUsers        = vi.mocked(hooks.useUsers);
const mockUseUserDetail   = vi.mocked(hooks.useUserDetail);
const mockUseRoles        = vi.mocked(hooks.useRoles);
const mockUseSections     = vi.mocked(hooks.useSections);
const mockUseAssignRole   = vi.mocked(hooks.useAssignRole);
const mockUseRevokeRole   = vi.mocked(hooks.useRevokeRole);
const mockUseDeactivate   = vi.mocked(hooks.useDeactivateUser);

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildUser(overrides: Partial<hooks.UserRow> = {}): hooks.UserRow {
  return {
    id:          1,
    email:       'alice@va.gov',
    displayName: 'Alice Admin',
    isActive:    true,
    lastLoginAt: '2026-09-10T08:00:00Z',
    createdAt:   '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function buildDetail(overrides: Partial<hooks.UserDetail> = {}): hooks.UserDetail {
  return {
    id:          1,
    email:       'alice@va.gov',
    displayName: 'Alice Admin',
    isActive:    true,
    lastLoginAt: '2026-09-10T08:00:00Z',
    createdAt:   '2026-01-01T00:00:00Z',
    roles:       [],
    ...overrides,
  };
}

function buildRole(overrides: Partial<hooks.RoleRow> = {}): hooks.RoleRow {
  return {
    id:           2,
    name:         'ContentOwner',
    displayName:  'Content Owner',
    isSystemRole: true,
    ...overrides,
  };
}

function buildRoleDetail(overrides: Partial<hooks.UserRoleDetail> = {}): hooks.UserRoleDetail {
  return {
    roleId:           2,
    roleName:         'ContentOwner',
    roleDisplayName:  'Content Owner',
    sectionId:        null,
    sectionName:      null,
    sectionSlugPrefix: null,
    ...overrides,
  };
}

// ── Default idle mutation mock ─────────────────────────────────────────────────

function idleMutation<T>(mutateFn = vi.fn()) {
  return {
    mutate: mutateFn,
    isPending: false,
  } as unknown as ReturnType<T extends (...args: unknown[]) => infer R ? () => R : never>;
}

// ─────────────────────────────────────────────────────────────────────────────
// UserListPage tests
// ─────────────────────────────────────────────────────────────────────────────

describe('UserListPage', () => {
  function setupDeactivate() {
    const mutateMock = vi.fn();
    mockUseDeactivate.mockReturnValue({
      mutate: mutateMock,
      isPending: false,
    } as unknown as ReturnType<typeof hooks.useDeactivateUser>);
    return mutateMock;
  }

  beforeEach(() => {
    mockUseUsers.mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);
    setupDeactivate();
  });

  // ── Loading state ────────────────────────────────────────────────────────

  it('shows loading indicator while fetching', () => {
    mockUseUsers.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByText(/Loading users/i)).toBeTruthy();
  });

  // ── Error state ──────────────────────────────────────────────────────────

  it('shows error alert when fetch fails', () => {
    mockUseUsers.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/Failed to load users/i)).toBeTruthy();
  });

  // ── Empty state ──────────────────────────────────────────────────────────

  it('shows empty state when no users', () => {
    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByText(/No active users found/i)).toBeTruthy();
  });

  // ── Table rendering ──────────────────────────────────────────────────────

  it('renders a row for each user', () => {
    mockUseUsers.mockReturnValue({
      data: [buildUser({ id: 1, displayName: 'Alice' }), buildUser({ id: 2, displayName: 'Bob', email: 'bob@va.gov' })],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByText('Alice')).toBeTruthy();
    expect(screen.getByText('Bob')).toBeTruthy();
  });

  it('renders user email in the row', () => {
    mockUseUsers.mockReturnValue({
      data: [buildUser({ email: 'alice@va.gov' })],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByText('alice@va.gov')).toBeTruthy();
  });

  it('shows "Never" when lastLoginAt is null', () => {
    mockUseUsers.mockReturnValue({
      data: [buildUser({ lastLoginAt: null })],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByText('Never')).toBeTruthy();
  });

  // ── Search form ──────────────────────────────────────────────────────────

  it('has a labelled search input', () => {
    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    expect(screen.getByRole('searchbox')).toBeTruthy();
  });

  // ── Deactivate flow ──────────────────────────────────────────────────────

  it('shows confirmation dialog when Deactivate is clicked', () => {
    mockUseUsers.mockReturnValue({
      data: [buildUser({ id: 1, displayName: 'Alice' })],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Deactivate Alice/i }));
    expect(screen.getByRole('alertdialog')).toBeTruthy();
    expect(screen.getByText(/Deactivate this user/i)).toBeTruthy();
  });

  it('calls deactivate.mutate when confirmed', () => {
    const mutateMock = setupDeactivate();
    mockUseUsers.mockReturnValue({
      data: [buildUser({ id: 99, displayName: 'Alice' })],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Deactivate Alice/i }));
    fireEvent.click(screen.getByRole('button', { name: /Confirm deactivate user/i }));
    expect(mutateMock).toHaveBeenCalledWith(99, expect.any(Object));
  });

  it('dismisses dialog on Cancel', () => {
    mockUseUsers.mockReturnValue({
      data: [buildUser()],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUsers>);

    render(<MemoryRouter><UserListPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Deactivate Alice Admin/i }));
    expect(screen.getByRole('alertdialog')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// UserDetailPage tests
// ─────────────────────────────────────────────────────────────────────────────

describe('UserDetailPage', () => {
  function setup(userId = '1') {
    const assignMutateMock = vi.fn();
    const revokeMutateMock = vi.fn();

    mockUseUserDetail.mockReturnValue({
      data: buildDetail(),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);

    mockUseRoles.mockReturnValue({
      data: [buildRole()],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useRoles>);

    mockUseSections.mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSections>);

    mockUseAssignRole.mockReturnValue({
      mutate: assignMutateMock,
      isPending: false,
    } as unknown as ReturnType<typeof hooks.useAssignRole>);

    mockUseRevokeRole.mockReturnValue({
      mutate: revokeMutateMock,
      isPending: false,
    } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    const rendered = render(
      <MemoryRouter initialEntries={[`/admin/users/${userId}`]}>
        <Routes>
          <Route path="/admin/users/:userId" element={<UserDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    return { rendered, assignMutateMock, revokeMutateMock };
  }

  // ── Loading / error ──────────────────────────────────────────────────────

  it('shows loading indicator while fetching', () => {
    mockUseUserDetail.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);
    mockUseRoles.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useRoles>);
    mockUseSections.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useSections>);
    mockUseAssignRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useAssignRole>);
    mockUseRevokeRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    render(<MemoryRouter initialEntries={['/admin/users/1']}><Routes><Route path="/admin/users/:userId" element={<UserDetailPage />} /></Routes></MemoryRouter>);
    expect(screen.getByText(/Loading user/i)).toBeTruthy();
  });

  it('shows error alert when fetch fails', () => {
    mockUseUserDetail.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);
    mockUseRoles.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useRoles>);
    mockUseSections.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useSections>);
    mockUseAssignRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useAssignRole>);
    mockUseRevokeRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    render(<MemoryRouter initialEntries={['/admin/users/1']}><Routes><Route path="/admin/users/:userId" element={<UserDetailPage />} /></Routes></MemoryRouter>);
    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/Failed to load user/i)).toBeTruthy();
  });

  // ── User detail rendering ────────────────────────────────────────────────

  it('renders user name, email and status', () => {
    setup();
    expect(screen.getByText('Alice Admin')).toBeTruthy();
    expect(screen.getByText('alice@va.gov')).toBeTruthy();
    expect(screen.getByText('Active')).toBeTruthy();
  });

  it('shows "No roles assigned yet" when roles array is empty', () => {
    setup();
    expect(screen.getByText(/No roles assigned yet/i)).toBeTruthy();
  });

  it('renders role rows when user has roles', () => {
    mockUseUserDetail.mockReturnValue({
      data: buildDetail({ roles: [buildRoleDetail()] }),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);
    mockUseRoles.mockReturnValue({ data: [buildRole()], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useRoles>);
    mockUseSections.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useSections>);
    mockUseAssignRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useAssignRole>);
    mockUseRevokeRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    render(<MemoryRouter initialEntries={['/admin/users/1']}><Routes><Route path="/admin/users/:userId" element={<UserDetailPage />} /></Routes></MemoryRouter>);
    // Role name appears in the table body cell (strong) — use getAllByText and check at least one is in a table cell
    const cells = screen.getAllByText('Content Owner');
    expect(cells.some((el) => el.tagName === 'STRONG')).toBe(true);
    expect(screen.getByText('Global')).toBeTruthy();
  });

  // ── Assign role form ─────────────────────────────────────────────────────

  it('renders assign role form with labelled select', () => {
    setup();
    // Use htmlFor matching — the label id is "assign-role-select"
    expect(screen.getByLabelText(/^Role/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /Assign role/i })).toBeTruthy();
  });

  it('shows validation error when no role is selected', () => {
    setup();
    fireEvent.click(screen.getByRole('button', { name: /Assign role/i }));
    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/Please select a role/i)).toBeTruthy();
  });

  it('calls assignRole.mutate on valid submit', () => {
    const { assignMutateMock } = setup();

    // Select a role (role id=2, value "2")
    fireEvent.change(screen.getByLabelText(/^Role/i), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: /Assign role/i }));

    expect(assignMutateMock).toHaveBeenCalledWith(
      { roleId: 2, sectionId: null },
      expect.any(Object),
    );
  });

  // ── Revoke role ──────────────────────────────────────────────────────────

  it('shows revoke confirmation when Remove is clicked', () => {
    mockUseUserDetail.mockReturnValue({
      data: buildDetail({ roles: [buildRoleDetail()] }),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);
    mockUseRoles.mockReturnValue({ data: [buildRole()], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useRoles>);
    mockUseSections.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useSections>);
    mockUseAssignRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useAssignRole>);
    mockUseRevokeRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    render(<MemoryRouter initialEntries={['/admin/users/1']}><Routes><Route path="/admin/users/:userId" element={<UserDetailPage />} /></Routes></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Remove ContentOwner/i }));
    expect(screen.getByRole('alertdialog')).toBeTruthy();
    // Check the heading text specifically
    expect(screen.getByRole('heading', { name: /Remove role/i })).toBeTruthy();
  });

  it('calls revokeRole.mutate when Remove role is confirmed', () => {
    const revokeMock = vi.fn();
    mockUseUserDetail.mockReturnValue({
      data: buildDetail({ roles: [buildRoleDetail()] }),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useUserDetail>);
    mockUseRoles.mockReturnValue({ data: [buildRole()], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useRoles>);
    mockUseSections.mockReturnValue({ data: [], isLoading: false, isError: false } as unknown as ReturnType<typeof hooks.useSections>);
    mockUseAssignRole.mockReturnValue({ mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useAssignRole>);
    mockUseRevokeRole.mockReturnValue({ mutate: revokeMock, isPending: false } as unknown as ReturnType<typeof hooks.useRevokeRole>);

    render(<MemoryRouter initialEntries={['/admin/users/1']}><Routes><Route path="/admin/users/:userId" element={<UserDetailPage />} /></Routes></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Remove ContentOwner/i }));
    fireEvent.click(screen.getByRole('button', { name: /Confirm remove role/i }));

    expect(revokeMock).toHaveBeenCalledWith(
      { roleId: 2, sectionId: null },
      expect.any(Object),
    );
  });
});
