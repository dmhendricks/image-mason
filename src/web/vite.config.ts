import { fileURLToPath } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { type Plugin, loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

// Paths the dev server forwards to a running Image Mason (the Docker container).
const serverPaths = ['/api', '/images', '/docs', '/health', '/favicon.svg', '/favicon.ico', '/apple-touch-icon.png']

// Fontsource lists a WOFF fallback after each WOFF2 font. Every browser the interface supports reads WOFF2, so the
// fallback is dropped before Vite resolves the URLs, and the WOFF files never reach the bundle.
const woff2Only: Plugin = {
  name: 'woff2-only',
  enforce: 'pre',
  transform(code, id) {
    if (id.includes('/@fontsource/') && id.endsWith('.css'))
      return code.replaceAll(/,\s*url\([^)]+\.woff\) format\('woff'\)/g, '')
  },
}

export default defineConfig(({ mode }) => {
  // IMAGE_MASON_URL, or localhost on the PORT that docker compose publishes (from the repository's .env).
  const env = loadEnv(mode, fileURLToPath(new URL('../..', import.meta.url)), '')
  const server = env.IMAGE_MASON_URL || `http://localhost:${env.PORT || '8080'}`

  return {
    // Absolute URLs in templates (/favicon.svg) point at the server, not at files to bundle.
    plugins: [vue({ template: { transformAssetUrls: { includeAbsolute: false } } }), woff2Only],
    // Favicons are served by the API, so nothing is copied as-is.
    publicDir: false,
    build: {
      outDir: 'dist',
      emptyOutDir: true,
    },
    server: {
      proxy: Object.fromEntries(serverPaths.map(path => [path, server])),
    },
    test: {
      environment: 'jsdom',
    },
  }
})
