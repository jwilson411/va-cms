// ESLint flat config (#160): `next lint` was removed in Next 16, so lint runs
// eslint directly with the Next.js core-web-vitals + TypeScript rule sets.
import nextConfig from 'eslint-config-next';

export default [
  ...nextConfig,
  {
    ignores: ['.next/**', 'node_modules/**', 'public/uswds/**'],
  },
];
