/**
 * AuthContext — in-memory JWT storage for the VA CMS Admin SPA.
 *
 * Acceptance criteria:
 *   - JWT stored in React state ONLY (never localStorage, never sessionStorage)
 *   - Silent refresh before token expiry via GET /api/auth/refresh
 *   - If AD account disabled: refresh returns 401 → state cleared → login redirect
 *   - Login redirects to GET /api/auth/login (which initiates AD OIDC flow)
 *   - Local dev (Auth:Mode=DevBypass): devLogin(upn) posts to /api/auth/dev-login
 *     and receives the same { accessToken, expiresIn } shape plus the refresh cookie
 */

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { setAuthToken } from '../lib/authorizedFetch';

// ── Types ────────────────────────────────────────────────────────────────────

interface AuthTokenResponse {
  accessToken: string;
  expiresIn: number;   // seconds
  tokenType: string;
}

interface AuthState {
  /** JWT access token, held in React state only — NOT localStorage or sessionStorage. */
  accessToken: string | null;
  /** ISO timestamp of when the access token expires. */
  expiresAt: Date | null;
  /** True while an auth operation (refresh, login redirect) is in progress. */
  loading: boolean;
  /**
   * CMS role names carried by the access token (section scope stripped). Read for
   * UI decisions only — the API enforces every policy itself (#155).
   */
  roles: string[];
}

/** Claim type ASP.NET uses for ClaimTypes.Role; the API's JwtService emits roles under it. */
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/**
 * Decode the role claims from a JWT payload without verifying the signature —
 * the token came from the API over the same origin and is only used to decide
 * what to render. Scoped roles look like "ContentOwner:section:7:prefix:hr/".
 */
export function rolesFromToken(token: string): string[] {
  try {
    const payload = token.split('.')[1];
    if (!payload) return [];
    const b64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = JSON.parse(atob(b64.padEnd(b64.length + ((4 - (b64.length % 4)) % 4), '='))) as Record<string, unknown>;
    const raw = json[ROLE_CLAIM];
    const list = Array.isArray(raw) ? raw : typeof raw === 'string' ? [raw] : [];
    return list
      .filter((r): r is string => typeof r === 'string')
      .map((r) => r.split(':')[0])
      .filter((r) => r.length > 0);
  } catch {
    return [];
  }
}

interface AuthContextValue extends AuthState {
  /** Initiate AD OIDC login — navigates the browser to /api/auth/login. */
  login: () => void;
  /**
   * DevBypass sign-in (local development only). Posts X-Dev-User to
   * /api/auth/dev-login; the API returns 401/404 unless Auth:Mode=DevBypass.
   */
  devLogin: (upn: string) => Promise<void>;
  /** Clear in-memory token, revoke refresh cookie, and (AzureAd mode) navigate to the AAD sign-out. */
  logout: () => Promise<void>;
  /** True if the SPA has a valid, non-expired access token. */
  isAuthenticated: boolean;
  /** Issue an authenticated fetch, automatically using the in-memory JWT. */
  authFetch: (input: RequestInfo, init?: RequestInit) => Promise<Response>;
}

// ── Constants ────────────────────────────────────────────────────────────────

/** Refresh the token this many milliseconds before it expires. */
const REFRESH_BUFFER_MS = 60_000; // 60 seconds
/** API base path — proxied in development via Vite config. */
const API_BASE = '/api';

// ── Context ──────────────────────────────────────────────────────────────────

const AuthContext = createContext<AuthContextValue | null>(null);

// ── Provider ─────────────────────────────────────────────────────────────────

interface AuthProviderProps {
  children: React.ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps): JSX.Element {
  const [state, setState] = useState<AuthState>({
    accessToken: null,
    expiresAt: null,
    loading: true,
    roles: [],
  });
  const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── Helpers ────────────────────────────────────────────────────────────

  const scheduleRefresh = useCallback((expiresAt: Date) => {
    if (refreshTimerRef.current !== null)
      clearTimeout(refreshTimerRef.current);

    const msUntilExpiry = expiresAt.getTime() - Date.now();
    const msUntilRefresh = Math.max(msUntilExpiry - REFRESH_BUFFER_MS, 0);

    refreshTimerRef.current = setTimeout(() => {
      silentRefresh();
    }, msUntilRefresh);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const applyTokenResponse = useCallback(
    (data: AuthTokenResponse) => {
      const expiresAt = new Date(Date.now() + data.expiresIn * 1000);
      setAuthToken(data.accessToken); // publish for authorizedFetch()
      setState({
        // Token stored ONLY in React state — no localStorage, no sessionStorage.
        accessToken: data.accessToken,
        expiresAt,
        loading: false,
        roles: rolesFromToken(data.accessToken),
      });
      scheduleRefresh(expiresAt);
    },
    [scheduleRefresh],
  );

  const clearAuth = useCallback(() => {
    if (refreshTimerRef.current !== null)
      clearTimeout(refreshTimerRef.current);
    setAuthToken(null);
    setState({ accessToken: null, expiresAt: null, loading: false, roles: [] });
  }, []);

  // ── Silent refresh ─────────────────────────────────────────────────────

  const silentRefresh = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: 'GET',
        credentials: 'include', // send httpOnly refresh cookie
      });

      if (res.status === 401) {
        // AD account disabled or refresh token expired. Clear state only —
        // <ProtectedRoute> owns the redirect to login, so mounting the provider
        // on /login itself doesn't bounce straight back to /api/auth/login.
        clearAuth();
        return;
      }

      if (!res.ok) throw new Error(`Refresh failed: ${res.status}`);

      const data: AuthTokenResponse = await res.json();
      applyTokenResponse(data);
    } catch {
      // Network error during refresh — clear state so the SPA prompts login.
      clearAuth();
    }
  }, [applyTokenResponse, clearAuth]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Initialisation: attempt silent refresh on mount ────────────────────

  useEffect(() => {
    silentRefresh();
    return () => {
      if (refreshTimerRef.current !== null)
        clearTimeout(refreshTimerRef.current);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Public API ─────────────────────────────────────────────────────────

  const login = useCallback(() => {
    // Navigate to the API's login endpoint — it will redirect to Azure AD (or to
    // /login in DevBypass). Pass the page we were on so sign-in can return to it.
    const here = `${window.location.pathname}${window.location.search}`;
    const returnUrl = here !== '/' && !here.startsWith('/login') ? `?returnUrl=${encodeURIComponent(here)}` : '';
    window.location.href = `${API_BASE}/auth/login${returnUrl}`;
  }, []);

  const devLogin = useCallback(
    async (upn: string) => {
      setState((prev) => ({ ...prev, loading: true }));
      try {
        const res = await fetch(`${API_BASE}/auth/dev-login`, {
          method: 'POST',
          headers: { 'X-Dev-User': upn },
          credentials: 'include', // receive httpOnly refresh cookie
        });
        if (!res.ok) throw new Error(`Dev login failed: ${res.status}`);
        const data: AuthTokenResponse = await res.json();
        applyTokenResponse(data);
      } catch (err) {
        clearAuth();
        throw err;
      }
    },
    [applyTokenResponse, clearAuth],
  );

  const logout = useCallback(async () => {
    // In AzureAd mode the API answers { signOutUrl } pointing at GET /api/auth/signout;
    // navigating there lets the browser complete the Azure AD end-session round trip
    // (a fetch cannot). Any other answer just clears local state.
    let signOutUrl: string | null = null;
    try {
      const res = await fetch(`${API_BASE}/auth/logout`, {
        method: 'POST',
        credentials: 'include',
      });
      if (res.ok && res.status !== 204) {
        const body = (await res.json().catch(() => null)) as { signOutUrl?: string | null } | null;
        signOutUrl = body?.signOutUrl ?? null;
      }
    } catch {
      // Best-effort — clear local state regardless.
    }
    clearAuth();
    if (signOutUrl) window.location.assign(signOutUrl);
  }, [clearAuth]);

  const authFetch = useCallback(
    async (input: RequestInfo, init?: RequestInit): Promise<Response> => {
      const headers = new Headers(init?.headers);
      if (state.accessToken) {
        headers.set('Authorization', `Bearer ${state.accessToken}`);
      }
      return fetch(input, { ...init, headers });
    },
    [state.accessToken],
  );

  const value: AuthContextValue = {
    ...state,
    login,
    devLogin,
    logout,
    isAuthenticated: state.accessToken !== null,
    authFetch,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// ── Hook ─────────────────────────────────────────────────────────────────────

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an <AuthProvider>');
  return ctx;
}
