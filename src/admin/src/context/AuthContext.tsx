/**
 * AuthContext — in-memory JWT storage for the VA CMS Admin SPA.
 *
 * Acceptance criteria:
 *   - JWT stored in React state ONLY (never localStorage, never sessionStorage)
 *   - Silent refresh before token expiry via GET /api/auth/refresh
 *   - If AD account disabled: refresh returns 401 → state cleared → login redirect
 *   - Login redirects to GET /api/auth/login (which initiates AD OIDC flow)
 */

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';

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
}

interface AuthContextValue extends AuthState {
  /** Initiate AD OIDC login — navigates the browser to /api/auth/login. */
  login: () => void;
  /** Clear in-memory token and revoke refresh cookie. */
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
      setState({
        // Token stored ONLY in React state — no localStorage, no sessionStorage.
        accessToken: data.accessToken,
        expiresAt,
        loading: false,
      });
      scheduleRefresh(expiresAt);
    },
    [scheduleRefresh],
  );

  const clearAuth = useCallback(() => {
    if (refreshTimerRef.current !== null)
      clearTimeout(refreshTimerRef.current);
    setState({ accessToken: null, expiresAt: null, loading: false });
  }, []);

  // ── Silent refresh ─────────────────────────────────────────────────────

  const silentRefresh = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: 'GET',
        credentials: 'include', // send httpOnly refresh cookie
      });

      if (res.status === 401) {
        // AD account disabled or refresh token expired — force re-login.
        clearAuth();
        login();
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
    // Navigate to the API's login endpoint — it will redirect to Azure AD.
    window.location.href = `${API_BASE}/auth/login`;
  }, []);

  const logout = useCallback(async () => {
    try {
      await fetch(`${API_BASE}/auth/logout`, {
        method: 'POST',
        credentials: 'include',
      });
    } catch {
      // Best-effort — clear local state regardless.
    }
    clearAuth();
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
