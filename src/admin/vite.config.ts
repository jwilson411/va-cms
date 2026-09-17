import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { ADMIN_SECURITY_HEADERS, buildAdminCsp } from './src/security/csp';
import { iisWebConfig } from './iis-web-config.plugin';

// #162: the same headers IIS sends in production (public/web.config), so the CSP is
// exercised during development instead of only after deployment.
const securityHeaders = { ...ADMIN_SECURITY_HEADERS, 'Content-Security-Policy': buildAdminCsp() };

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
  preview: { headers: securityHeaders },
  server: {
    headers: securityHeaders,
    proxy: {
      // VITE_API_PROXY lets a second checkout/worktree point at an API on another port.
      '/api': process.env.VITE_API_PROXY ?? 'http://localhost:5100',
    },
  },
});
