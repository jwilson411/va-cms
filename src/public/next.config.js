const path = require('path');

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  sassOptions: {
    // Lets `@use "uswds-core"` / `@forward "uswds"` resolve (issue #17).
    includePaths: [path.join(__dirname, 'node_modules/@uswds/uswds/packages')],
    // USWDS 3.x still uses deprecated Sass APIs internally, and Next 14's
    // sass-loader drives the legacy JS API; keep both out of the build log.
    quietDeps: true,
    silenceDeprecations: ['legacy-js-api'],
  },
};

module.exports = nextConfig;
