import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { ADMIN_CSP_DIRECTIVES, ADMIN_SECURITY_HEADERS, buildAdminCsp } from './src/security/csp';
import { iisWebConfig } from './iis-web-config.plugin';

// #162: the same headers IIS sends in production (public/web.config), so the CSP is
// exercised via `vite preview` — which serves the real build, and the build has no
// inline scripts (modulePreload.polyfill: false below), so script-src 'self' holds.
const previewSecurityHeaders = { ...ADMIN_SECURITY_HEADERS, 'Content-Security-Policy': buildAdminCsp() };

// `vite dev` cannot use that same policy: @vitejs/plugin-react always injects an inline
// <script type="module"> (the React Fast Refresh preamble) into every served page, which
// script-src 'self' with no nonce/hash blocks outright — the SPA never renders, in any
// CSP-enforcing browser. So the dev server gets a relaxed script-src; every other
// directive — and the headers `preview`/IIS production actually send — is unchanged.
const devSecurityHeaders = {
  ...ADMIN_SECURITY_HEADERS,
  'Content-Security-Policy': buildAdminCsp({
    ...ADMIN_CSP_DIRECTIVES,
    'script-src': ["'self'", "'unsafe-inline'", "'unsafe-eval'"],
  }),
};

export default defineConfig({
  plugins: [react(), iisWebConfig()],
  build: {
    // No inline module-preload polyfill → the built index.html has no inline script,
    // which is what lets script-src stay 'self' without hashes (#162).
    modulePreload: { polyfill: false },
  },
  css: {
    preprocessorOptions: {
      scss: {
        api: 'modern-compiler',
        // Lets `@use "uswds-core"` / `@forward "uswds"` resolve (issue #17).
        loadPaths: [path.resolve(__dirname, 'node_modules/@uswds/uswds/packages')],
        // USWDS 3.x still uses deprecated Sass APIs internally; keep its noise out.
        quietDeps: true,
      },
    },
  },
  preview: { headers: previewSecurityHeaders },
  server: {
    headers: devSecurityHeaders,
    proxy: {
      // VITE_API_PROXY lets a second checkout/worktree point at an API on another port.
      '/api': process.env.VITE_API_PROXY ?? 'http://localhost:5100',
    },
  },
});
