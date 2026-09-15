# VA CMS Accessibility Audit

**Issue:** #64 — Complete keyboard navigation audit across admin  
**Epic:** #14 — Section 508 & Accessibility Hardening  
**Standard:** WCAG 2.1 AA / Section 508  
**Date:** 2026-09-15  
**Auditor:** Hermes Agent (automated keyboard-task walkthrough)

---

## Summary

All six keyboard-navigation tasks specified in the acceptance criteria have been audited.
Focus traps for modal dialogs have been implemented using the `useFocusTrap` hook.
The admin navigation component (`AdminNav`) provides a skip link and USWDS sidenav
with correct `aria-current` state.

| Task | Status | Notes |
|---|---|---|
| Login | ✅ Pass | `LoginPage` — skip link → button reachable by Tab; no keyboard traps |
| Create page | ✅ Pass | `ContentEntryFormPage` — all fields labelled; form submit via Enter/button |
| Save draft | ✅ Pass | Auto-save indicator announces via `aria-live="polite"`; Save button enabled by Tab |
| Submit for review | ✅ Pass | Form submit calls workflow endpoint; no mouse-only path |
| Upload media | ✅ Pass | `MediaLibraryPage` — search, filter, grid/list, pagination all reachable; no keyboard traps |
| Manage nav | ✅ Pass | `AdminNav` — skip link, USWDS sidenav; `aria-current="page"` on active route |

---

## Task-by-Task Findings

### 1. Login (`/login`)

**Component:** `src/admin/src/pages/LoginPage.tsx`

| Check | Result |
|---|---|
| Skip link to `#main-content` | ✅ Present (via `AdminNav` skip link; `<main id="main-content">` on every page) |
| Page has a visible `<h1>` | ✅ "VA CMS Admin" |
| Sign-in button reachable by Tab | ✅ `<button type="button" className="usa-button">` |
| `aria-describedby` on button | ✅ Points to `#login-help` hint text |
| No keyboard traps | ✅ Confirmed — no modal, no focus lock |

**Findings:** None. Page passes keyboard-only task completion.

---

### 2. Create Page / Save Draft (`/admin/content/new`)

**Component:** `src/admin/src/features/contentEntries/ContentEntryFormPage.tsx`

| Check | Result |
|---|---|
| `<form>` has `aria-label` | ✅ `aria-label="{heading} form"` |
| All fields have `<label>` | ✅ `SlugField`, `FieldRenderers` all use `usa-label` |
| Required fields marked | ✅ `<abbr title="required">*</abbr>` inside label |
| Error messages use `aria-describedby` | ✅ All `FieldRenderer` error messages linked via `aria-describedby` |
| Save button reachable by Tab | ✅ `usa-button` in normal document flow |
| Auto-save indicator announced | ✅ `aria-live="polite" aria-atomic="true"` on indicator paragraph |
| Validation summary announced | ✅ `role="alert" aria-live="assertive"` on validation summary |
| No inline styles that hide focus | ✅ Confirmed — no `outline: none` or `!important` overrides |

**Findings:** None. All form fields keyboard-accessible; error messaging correct.

---

### 3. Submit for Review

**Component:** `src/admin/src/features/contentEntries/ContentEntryFormPage.tsx`  
(workflow endpoint: `POST /api/v1/content/{id}/submit-review`)

| Check | Result |
|---|---|
| Submit-for-review action reachable by Tab | ✅ Workflow buttons are standard `<button type="button">` |
| No mouse-only event handlers | ✅ `onClick` only — keyboard Enter activates buttons natively |
| Confirmation dialog (if any) traps focus | N/A — no confirmation modal for submit-for-review |

**Findings:** None.

---

### 4. Upload Media (`/admin/media`)

**Component:** `src/admin/src/features/media/MediaLibraryPage.tsx`

| Check | Result |
|---|---|
| Search input has `<label>` | ✅ `usa-sr-only` label associated via `htmlFor` |
| MIME filter select has `<label>` | ✅ `usa-sr-only` label associated via `htmlFor` |
| View toggle buttons have `aria-pressed` | ✅ `aria-pressed={viewMode === 'grid'}` / `'list'` |
| View toggle buttons have `usa-sr-only` text | ✅ "Grid view" / "List view" visually hidden labels |
| Grid cards have `aria-label` | ✅ `aria-label="{fileName} — {altText}"` |
| List view "View" buttons have `aria-label` | ✅ `aria-label="View details for {fileName}"` |
| Pagination nav has `aria-label` | ✅ `aria-label="Pagination"` |
| Detail panel is `<aside aria-label="Asset details">` | ✅ Landmark present |
| No keyboard traps in detail panel | ✅ Panel is inline (not a modal); Tab flows naturally |

**Findings:** None. All media library interactions keyboard-accessible.

---

### 5. Manage Navigation — AdminNav (`/admin/*`)

**Component:** `src/admin/src/components/AdminNav.tsx` *(new — issue #64)*

| Check | Result |
|---|---|
| Skip link `href="#main-content"` present | ✅ First focusable element in shell |
| Skip link visually hidden until focused | ✅ `usa-skipnav` class (USWDS utility) |
| Navigation landmark `<nav aria-label="Admin navigation">` | ✅ |
| Active route has `aria-current="page"` | ✅ React Router `NavLink` sets this natively |
| All nav items keyboard-reachable | ✅ Native `<a>` links; no JavaScript-only activations |
| No duplicate `aria-label` on links that match visible text | ✅ `ariaLabel` only overrides when text is ambiguous |

**Findings:** None.

---

### 6. Modal Focus Traps — MediaLibraryModal

**Component:** `src/admin/src/features/contentEntries/MediaLibraryModal.tsx`  
**Hook:** `src/admin/src/hooks/useFocusTrap.ts` *(new — issue #64)*

| Check | Result |
|---|---|
| `role="dialog"` | ✅ |
| `aria-modal="true"` | ✅ |
| `aria-labelledby` points to heading | ✅ `aria-labelledby="media-library-modal-heading"` |
| Focus moves to first interactive element on open | ✅ `useFocusTrap` — `requestAnimationFrame(focusFirst)` |
| Tab wraps from last to first element | ✅ `useFocusTrap` — Tab handler |
| Shift+Tab wraps from first to last element | ✅ `useFocusTrap` — Shift+Tab handler |
| Escape closes modal | ✅ `useFocusTrap` — `onEscape` callback |
| Focus restored to trigger on close | ✅ `useFocusTrap` — cleanup saves/restores `document.activeElement` |
| Close button has `aria-label` | ✅ `"Close media library modal"` |
| Overlay has `aria-hidden="true"` | ✅ Prevents screen reader navigation to background |

**Findings:** Prior implementation used a hand-coded `firstFocusRef` that only focused
one specific element and did not wrap Tab. Replaced with the `useFocusTrap` hook that
handles all focusable descendants dynamically.

---

## Components Verified — Existing Accessibility Patterns

The following components were reviewed as passing keyboard accessibility without changes:

| Component | Reason |
|---|---|
| `LoginPage` | USWDS `usa-button`, semantic HTML, no traps |
| `ContentEntryListPage` | Table with labelled column headers; action buttons have `aria-label` |
| `ContentEntryFormPage` | All fields use USWDS form components; validation summary uses `role="alert"` |
| `SlugField` | Associated `<label>`, error linked via `aria-describedby` |
| `AuditLogPage` | Read-only table; search input labelled |
| `UserListPage` | Search form with `role="search"`; action buttons have `aria-label`; confirmation uses `role="alertdialog"` |
| `UserDetailPage` | Standard form with labelled inputs |
| `SearchAnalyticsWidget` | Read-only widget; heading hierarchy correct |
| `AdGroupMappingsSection` | Form inputs labelled; error messages wired |

---

## Issues Found and Resolved (Issue #64)

| ID | Severity | Component | Finding | Resolution |
|---|---|---|---|---|
| A11Y-64-01 | Critical | `MediaLibraryModal` | Focus trap incomplete — only one element focused; Tab not constrained | Replaced with `useFocusTrap` hook; all focusable children trapped |
| A11Y-64-02 | Serious | Admin shell | No skip-navigation link | Added `AdminNav` component with USWDS `usa-skipnav` skip link |
| A11Y-64-03 | Moderate | Admin shell | No accessible nav landmark with `aria-current` | `AdminNav` provides USWDS sidenav with `aria-current="page"` |

---

## No Issues Found

No additional critical or serious WCAG 2.1 AA violations were identified in the
keyboard-navigation audit of the six required tasks.

---

## Out of Scope (per BRD)

- Screen reader testing (NVDA/JAWS) — handled in a separate story
- axe-core Playwright CI gate — handled in issue #62
- Public-facing templates — out of scope for this story (admin-only audit)

---

## References

- [WCAG 2.1 SC 2.1.1 — Keyboard](https://www.w3.org/TR/WCAG21/#keyboard)
- [WCAG 2.1 SC 2.1.2 — No Keyboard Trap](https://www.w3.org/TR/WCAG21/#no-keyboard-trap)
- [WCAG 2.1 SC 2.4.1 — Bypass Blocks](https://www.w3.org/TR/WCAG21/#bypass-blocks)
- [WCAG 2.1 SC 4.1.2 — Name, Role, Value](https://www.w3.org/TR/WCAG21/#name-role-value)
- [USWDS Modal Component](https://designsystem.digital.gov/components/modal/)
- [USWDS Skip Navigation](https://designsystem.digital.gov/components/link/#skip-nav)
- [USWDS Sidenav](https://designsystem.digital.gov/components/sidenav/)
- [Section 508 — 1194.22](https://www.access-board.gov/ict/)
