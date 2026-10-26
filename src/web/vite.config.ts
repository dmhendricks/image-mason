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

// The faces the first screen shows (Latin only). Preloading them starts their download with the page rather than after
// the CSS that names them, so on a slow link the text doesn't visibly swap fonts. Vite names the files by their
// contents, so each is found in the bundle; the build fails if one is missing (a renamed or unused face).
const firstScreenFonts = [
  /^barlow-latin-400-normal-/,
  /^barlow-semi-condensed-latin-500-normal-/,
  /^barlow-semi-condensed-latin-600-normal-/,
  /^barlow-semi-condensed-latin-700-normal-/,
]
const preloadFonts: Plugin = {
  name: 'preload-fonts',
  apply: 'build',
  transformIndexHtml(_html, context) {
    const files = Object.keys(context.bundle ?? {}).filter(file => file.endsWith('.woff2'))
    return firstScreenFonts.map(font => {
      const file = files.find(name => font.test(name.split('/').pop()!))
      if (!file)
        throw new Error(`No font in the bundle matches ${font}`)
      return { tag: 'link', attrs: { rel: 'preload', href: `/${file}`, as: 'font', type: 'font/woff2', crossorigin: '' }, injectTo: 'head' }
    })
  },
}

export default defineConfig(({ mode }) => {
  // IMAGE_MASON_URL, or localhost on the PORT that docker compose publishes (from the repository's .env).
  const env = loadEnv(mode, fileURLToPath(new URL('../..', import.meta.url)), '')
  const server = env.IMAGE_MASON_URL || `http://localhost:${env.PORT || '8080'}`

  return {
    // Absolute URLs in templates (/favicon.svg) point at the server, not at files to bundle.
    plugins: [vue({ template: { transformAssetUrls: { includeAbsolute: false } } }), woff2Only, preloadFonts],
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
