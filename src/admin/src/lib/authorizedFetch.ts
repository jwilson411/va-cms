/**
 * authorizedFetch — fetch() that attaches the in-memory CMS JWT.
 *
 * AuthProvider publishes the current access token here (setAuthToken) whenever
 * it changes. Feature hooks and components use authorizedFetch instead of the
 * bare global fetch so every /api call carries `Authorization: Bearer …`.
 *
 * The token lives only in this module's memory — never localStorage or
 * sessionStorage — matching the AuthContext acceptance criteria. When no token
 * is set (unauthenticated, or unit tests), the call is forwarded unchanged.
 */

let currentToken: string | null = null;

export function setAuthToken(token: string | null): void {
  currentToken = token;
}

export function getAuthToken(): string | null {
  return currentToken;
}

export function authorizedFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  if (currentToken === null) return fetch(input, init);

  const headers = new Headers(init?.headers);
  if (!headers.has('Authorization')) {
    headers.set('Authorization', `Bearer ${currentToken}`);
  }
  return fetch(input, { ...init, headers });
}
