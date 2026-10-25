import { fileURLToPath } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

// Paths the dev server forwards to a running Image Mason (the Docker container).
const serverPaths = ['/api', '/images', '/docs', '/health', '/favicon.svg', '/favicon.ico', '/apple-touch-icon.png']

export default defineConfig(({ mode }) => {
  // IMAGE_MASON_URL, or localhost on the PORT that docker compose publishes (from the repository's .env).
  const env = loadEnv(mode, fileURLToPath(new URL('../..', import.meta.url)), '')
  const server = env.IMAGE_MASON_URL || `http://localhost:${env.PORT || '8080'}`

  return {
    // Absolute URLs in templates (/favicon.svg) point at the server, not at files to bundle.
    plugins: [vue({ template: { transformAssetUrls: { includeAbsolute: false } } })],
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
