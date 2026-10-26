<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useServer } from '../../composables/useServer'
import { formatBytes, formatMegapixels, formatName } from '../../format'
import { scoredPixelRange } from '../../jobs'

// Before an image is open: the drop target, and what this server accepts.

const emit = defineEmits<{ open: [] }>()

const { limits, error, loading, reload } = useServer()
const { opening } = useImage()

const isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent)

const formats = computed(() => {
  const names = (limits.value?.formats ?? []).map(f => formatName(f.name))
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} or ${names.at(-1)}` : names.join('')
})

// What a lossy job can take at full size, when memory, not the pixel limit, is what stops it.
const largest = computed(() => {
  if (!limits.value)
    return ''
  const all = formatMegapixels(limits.value.maxPixels)
  const range = scoredPixelRange(limits.value)
  if (!range)
    return all
  const [low, high] = range.map(pixels => Math.floor(pixels / 100_000) / 10)
  return `${all}; with a quality floor, about ${low === high ? low : `${low}–${high}`} megapixels, depending on format`
})

const floors = computed(() =>
  Object.entries(limits.value?.presets ?? {})
    .filter(([, score]) => score !== null)
    .map(([name, score]) => `${name[0]!.toUpperCase()}${name.slice(1)} ${score}+`)
    .join(', '))
</script>

<template>
  <div class="empty">
    <button class="drop" type="button" :disabled="!!opening" @click="emit('open')">
      <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 16V4M7 9l5-5 5 5M4 20h16" /></svg>
      <strong>{{ opening ? (opening.fromUrl ? 'Downloading the image…' : 'Reading the image…') : 'Open an image' }}</strong>
      <span>
        or drop it here, or paste it{{ limits?.urlFetch ? ' or its web address' : '' }} with {{ isMac ? '⌘V' : 'Ctrl+V' }}
      </span>
    </button>

    <section class="server" aria-labelledby="server-heading">
      <h2 id="server-heading">This server</h2>

      <p v-if="loading && !limits" class="note">Reading the server's limits…</p>

      <div v-else-if="error" class="alert" role="alert">
        <p>{{ error }}</p>
        <button class="button" type="button" @click="reload">Try again</button>
      </div>

      <dl v-else-if="limits" class="facts">
        <dt>Formats</dt><dd>{{ formats }}</dd>
        <dt>Largest file</dt><dd>{{ formatBytes(limits.maxInputSize) }}</dd>
        <dt>Largest image</dt><dd>{{ largest }}</dd>
        <dt>Quality floors</dt><dd>{{ floors }}</dd>
        <dt>Shared links</dt><dd>Expire after {{ limits.imageTtlMinutes }} minutes</dd>
        <dt>API key</dt><dd>{{ limits.apiKeyRequired ? 'Required' : 'Not needed' }}</dd>
      </dl>

      <p class="note">Working from scripts? The <a href="/docs">API examples</a> show every option with curl.</p>
    </section>
  </div>
</template>

<style scoped lang="scss">
.empty {
  display: grid;
  gap: 16px;
  width: min(520px, 100%);
}

// Dashed like an unfinished outline on the drawing board: the place the image goes.
.drop {
  display: grid;
  justify-items: center;
  gap: 6px;
  padding: 40px 20px 36px;
  border: 2px dashed color-mix(in srgb, var(--flag) 55%, var(--rule));
  border-radius: 8px;
  background: color-mix(in srgb, var(--panel) 85%, transparent);
  color: var(--muted);
  cursor: pointer;

  svg {
    width: 34px;
    height: 34px;
    margin-bottom: 4px;
    stroke: var(--flag);
    stroke-width: 1.6;
  }

  strong {
    color: var(--ink);
    font: 700 22px/1.2 var(--font-cond);
  }

  &:hover:not(:disabled) {
    border-color: var(--flag);
    background: var(--panel);
  }

  &:disabled { cursor: progress; }
}

.server {
  padding: 16px 20px 18px;
  border: 1px solid var(--rule);
  border-radius: 6px;
  background: var(--panel);

  h2 {
    margin: 0 0 12px;
    font: 700 17px/1.2 var(--font-cond);
  }
}

.facts {
  margin: 0;
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 6px 18px;
  font-variant-numeric: tabular-nums;

  dt { color: var(--muted); }
  dd { margin: 0; }
}

.note {
  margin: 14px 0 0;
  color: var(--muted);

  &:first-of-type:not(:last-child) { margin-top: 0; }
  a { color: inherit; }
}
</style>
