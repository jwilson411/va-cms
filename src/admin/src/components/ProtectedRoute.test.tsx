/**
 * ProtectedRoute / NoAccessPage tests (#155).
 *
 *   - A signed-in user with no CMS role sees the "no access" page, not the children.
 *   - A user with at least one role sees the children.
 *   - rolesFromToken decodes the ASP.NET role claim (string or array, scope stripped).
 */
import { render, screen, waitFor } from '@testing-library/react';
import { AuthProvider, rolesFromToken } from '../context/AuthContext';
import { ProtectedRoute } from './ProtectedRoute';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** Build an unsigned JWT with the given payload — only the payload segment is read. */
function fakeJwt(payload: Record<string, unknown>): string {
  const b64 = (o: object): string =>
    btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64(payload)}.sig`;
}

function mockRefresh(token: string): void {
  vi.spyOn(window, 'fetch').mockResolvedValue({
    ok: true,
    status: 200,
    json: () => Promise.resolve({ accessToken: token, expiresIn: 900, tokenType: 'Bearer' }),
  } as Response);
}

describe('rolesFromToken', () => {
  it('reads an array of role claims and strips section scope', () => {
    const token = fakeJwt({ [ROLE_CLAIM]: ['Editor', 'ContentOwner:section:7:prefix:hr/'] });
    expect(rolesFromToken(token)).toEqual(['Editor', 'ContentOwner']);
  });

  it('reads a single string role claim', () => {
    expect(rolesFromToken(fakeJwt({ [ROLE_CLAIM]: 'SystemAdmin' }))).toEqual(['SystemAdmin']);
  });

  it('returns [] for a token without roles or for garbage', () => {
    expect(rolesFromToken(fakeJwt({ sub: 'x' }))).toEqual([]);
    expect(rolesFromToken('not.a.jwt')).toEqual([]);
    expect(rolesFromToken('')).toEqual([]);
  });
});

describe('ProtectedRoute', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the no-access page for a signed-in user with no CMS role', async () => {
    mockRefresh(fakeJwt({ sub: 'x' }));

    render(
      <AuthProvider>
        <ProtectedRoute>
          <p>secret dashboard</p>
        </ProtectedRoute>
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect(screen.getByText('No access to the CMS')).toBeTruthy();
    expect(screen.queryByText('secret dashboard')).toBeNull();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy();
  });

  it('renders children for a user with a role', async () => {
    mockRefresh(fakeJwt({ [ROLE_CLAIM]: 'ReadOnly' }));

    render(
      <AuthProvider>
        <ProtectedRoute>
          <p>secret dashboard</p>
        </ProtectedRoute>
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByText('secret dashboard')).toBeTruthy());
  });
});
