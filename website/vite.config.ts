import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/postcss';
import { defineConfig, loadEnv } from 'vite';
import { fileURLToPath } from 'node:url';

export default defineConfig(({ mode }) => {
  const local = loadEnv(mode, process.cwd(), '');
  // Only trust build configuration, never a visitor-supplied Host header.
  const configuredUrl = local.VITE_SITE_URL || process.env.URL;
  let origin = '';
  if (configuredUrl) {
    const url = new URL(configuredUrl);
    if (!['http:', 'https:'].includes(url.protocol)) throw new Error('Site URL must use HTTP or HTTPS.');
    origin = url.origin;
  }

  return {
    resolve: { alias: { '@': fileURLToPath(new URL('.', import.meta.url)) } },
    css: { postcss: { plugins: [tailwindcss()] } },
    plugins: [
      react(),
      {
        name: 'vanta-metadata',
        transformIndexHtml() {
          const image = origin + '/images/vanta-logo.png';
          const tags = [
            { tag: 'meta', attrs: { property: 'og:image', content: image } },
            { tag: 'meta', attrs: { property: 'og:image:alt', content: 'Vanta Auto Clicker app logo' } },
            { tag: 'meta', attrs: { name: 'twitter:image', content: image } },
          ];
          if (origin) {
            tags.push({ tag: 'meta', attrs: { property: 'og:url', content: origin + '/' } });
          }
          return tags;
        },
      },
    ],
    build: { outDir: 'dist', emptyOutDir: true },
  };
});
