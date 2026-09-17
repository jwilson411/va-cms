const path = require('path');

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // The USWDS theme tokens live in ../theme (shared with the admin SPA), outside
  // this package. Turbopack (Next 16's default bundler) refuses files above its
  // root, so the root is the src/ folder that contains both packages (#160).
  turbopack: {
    root: path.join(__dirname, '..'),
  },
  sassOptions: {
    // Lets `@use "uswds-core"` / `@forward "uswds"` resolve (issue #17). Next 16's
    // sass-loader drives the modern Sass API (loadPaths); includePaths is kept for
    // the legacy API used by `next dev --webpack`.
    loadPaths: [path.join(__dirname, 'node_modules/@uswds/uswds/packages')],
    includePaths: [path.join(__dirname, 'node_modules/@uswds/uswds/packages')],
    // USWDS 3.x still uses deprecated Sass APIs internally; keep them out of the build log.
    quietDeps: true,
    silenceDeprecations: ['legacy-js-api', 'import', 'global-builtin', 'color-functions'],
  },
};

module.exports = nextConfig;
