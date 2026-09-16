/**
 * /theme — USWDS / VA theme smoke-test page (issue #17).
 * Static; the header comes from the root layout.
 */

import type { Metadata } from 'next';
import { ThemeCheckTemplate } from '@/components/templates/ThemeCheckTemplate';

export const metadata: Metadata = {
  title: 'USWDS theme check',
  robots: { index: false, follow: false },
};

export default function ThemePage(): React.ReactElement {
  return <ThemeCheckTemplate />;
}
