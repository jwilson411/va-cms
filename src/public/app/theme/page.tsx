/**
 * /theme — USWDS / VA theme smoke-test page (issue #17).
 * Static; the header comes from the root layout.
 *
 * Development-only (#172): a production build answers 404 here. The page is a
 * component gallery for theme work, not veteran-facing content, and the public
 * host should expose nothing that is not.
 */

import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { ThemeCheckTemplate } from '@/components/templates/ThemeCheckTemplate';

export const metadata: Metadata = {
  title: 'USWDS theme check',
  robots: { index: false, follow: false },
};

export default function ThemePage(): React.ReactElement {
  // `next dev` only — a production build prerenders this route as 404.
  if (process.env.NODE_ENV !== 'development') notFound();
  return <ThemeCheckTemplate />;
}
