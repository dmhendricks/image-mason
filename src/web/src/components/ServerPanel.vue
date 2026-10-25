<script setup lang="ts">
import { computed } from 'vue'
import type { Limits } from '../api/types'
import { formatBytes, formatMegapixels } from '../format'

const props = defineProps<{
  limits: Limits | null
  error: string | null
  loading: boolean
}>()

defineEmits<{ reload: [] }>()

const formatNames: Record<string, string> = { jpeg: 'JPEG', png: 'PNG', webp: 'WebP', avif: 'AVIF' }

const formats = computed(() =>
  props.limits?.formats.map(f => formatNames[f.name] ?? f.name.toUpperCase()).join(', ') ?? '')

const floors = computed(() =>
  Object.entries(props.limits?.presets ?? {})
    .filter(([, score]) => score !== null)
    .map(([name, score]) => `${name[0]!.toUpperCase()}${name.slice(1)} ${score}+`)
    .join(', '))
</script>

<template>
  <section class="panel" aria-labelledby="server-heading">
    <h1 id="server-heading">This server</h1>
    <p class="lede">
      The image tools are being built. Until then, the <a href="/docs">API examples</a> show every option.
    </p>

    <p v-if="loading && !limits" class="status">Reading the server's limits…</p>

    <div v-else-if="error" class="status error" role="alert">
      <p>{{ error }}</p>
      <button type="button" @click="$emit('reload')">Try again</button>
    </div>

    <dl v-else-if="limits" class="facts">
      <dt>Formats</dt><dd>{{ formats }}</dd>
      <dt>Largest file</dt><dd>{{ formatBytes(limits.maxInputSize) }}</dd>
      <dt>Largest image</dt><dd>{{ formatMegapixels(limits.maxPixels) }}</dd>
      <dt>Quality floors</dt><dd>{{ floors }}</dd>
      <dt>Shared links</dt><dd>Work for {{ limits.imageTtlMinutes }} minutes</dd>
      <dt>Images from a URL</dt><dd>{{ limits.urlFetch ? 'Allowed' : 'Off' }}</dd>
      <dt>API key</dt><dd>{{ limits.apiKeyRequired ? 'Required' : 'Not needed' }}</dd>
    </dl>
  </section>
</template>

<style scoped lang="scss">
.panel {
  width: min(520px, 100%);
  padding: 20px 22px 22px;
  background: var(--panel);
  border: 1px solid var(--rule);
  border-radius: 6px;
  box-shadow: 0 6px 24px rgb(0 0 0 / 0.12);
}

h1 {
  margin: 0;
  font: 700 22px/1.2 var(--font-cond);
}

.lede {
  margin: 4px 0 18px;
  color: var(--muted);

  a { color: inherit; }
}

.facts {
  margin: 0;
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 8px 18px;
  font-variant-numeric: tabular-nums;

  dt { color: var(--muted); }
  dd { margin: 0; }
}

.status {
  margin: 0;
  color: var(--muted);

  p { margin: 0 0 10px; }
}

.error {
  padding: 10px 12px;
  border-radius: var(--radius);
  background: var(--flag-soft);
  color: var(--ink);

  button {
    height: 32px;
    padding: 0 12px;
    border: 0;
    border-radius: var(--radius);
    background: var(--flag);
    color: var(--flag-ink);
    font: 600 14px var(--font-cond);
    cursor: pointer;
  }
}
</style>
