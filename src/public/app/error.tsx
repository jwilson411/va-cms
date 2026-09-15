/**
 * app/error.tsx — Next.js 14 App Router global error boundary.
 *
 * Issue #61 — BRD FR-ERR-02
 * AC: 500 page uses USWDS Alert (error) with a friendly plain-language message.
 * AC: Includes Banner and Identifier.
 * AC: Passes axe-core with zero critical violations.
 *
 * Next.js requires error boundaries to be Client Components ('use client').
 * The ServerErrorTemplate itself is a Server Component — this boundary
 * wraps it with client-side error catching, passing static empty navigation
 * since we cannot safely call async fetch in an error boundary.
 *
 * Per Next.js 14 spec: error.tsx receives { error, reset } props.
 * We render the full USWDS error page; reset() is available but not
 * exposed in the UI per the AC scope.
 */

'use client';

import React from 'react';
import { ServerErrorTemplate } from '@/components/templates/ServerErrorTemplate';

interface ErrorPageProps {
  error: Error & { digest?: string };
  reset: () => void;
}

export default function ErrorPage({ error }: ErrorPageProps): React.ReactElement {
  // Log to console in development; in production this feeds server-side logging.
  if (process.env.NODE_ENV !== 'production') {
    // eslint-disable-next-line no-console
    console.error('[ErrorBoundary]', error);
  }

  // Navigation cannot be fetched asynchronously inside a Client Component
  // error boundary. Render with empty nav so the page still works.
  return <ServerErrorTemplate navigation={[]} />;
}
