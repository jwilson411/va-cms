/**
 * Who may open search analytics (issue #175).
 *
 * The API gates GET /api/v1/admin/search/analytics(/summary) with the CanManageSite
 * policy — SiteAdmin or SystemAdmin — because the rows are what anonymous visitors
 * typed into the search box, redacted but still not a dataset for every content role.
 * The SPA mirrors that so an Editor never sees a card that only 403s.
 */
export const SEARCH_ANALYTICS_ROLES: readonly string[] = ['SiteAdmin', 'SystemAdmin'];

/** True when at least one of the user's (unscoped) role names may read search analytics. */
export function canReadSearchAnalytics(roles: readonly string[]): boolean {
  return roles.some((r) => SEARCH_ANALYTICS_ROLES.includes(r));
}
