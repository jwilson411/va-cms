import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
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
  server: {
    proxy: {
      // VITE_API_PROXY lets a second checkout/worktree point at an API on another port.
      '/api': process.env.VITE_API_PROXY ?? 'http://localhost:5100',
    },
  },
});
