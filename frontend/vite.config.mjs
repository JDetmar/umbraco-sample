import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

// One CSS bundle at a stable path; the layout appends a content hash for caching (asp-append-version).
export default defineConfig({
  publicDir: false,
  build: {
    outDir: fileURLToPath(new URL('../src/UmbracoSample.Web/wwwroot/dist', import.meta.url)),
    emptyOutDir: true,
    cssMinify: 'lightningcss',
    rollupOptions: {
      input: fileURLToPath(new URL('src/site.css', import.meta.url)),
      output: { assetFileNames: '[name][extname]' },
    },
  },
});
