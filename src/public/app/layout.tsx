import type { Metadata } from 'next';
import '@uswds/uswds/css/uswds.css';

export const metadata: Metadata = {
  title: 'VA CMS Public Site',
  description: 'USWDS-compliant VA content management system public site',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}): React.ReactElement {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
