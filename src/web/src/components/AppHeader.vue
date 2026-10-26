<script setup lang="ts">
import { computed } from 'vue'
import { useApiKey } from '../composables/useApiKey'
import { useImage } from '../composables/useImage'
import { useServer } from '../composables/useServer'
import { useTheme } from '../composables/useTheme'
import { formatBytes, formatSize } from '../format'

const emit = defineEmits<{ open: [], manageKey: [] }>()

const { theme, toggle } = useTheme()
const { version, limits } = useServer()
const { image } = useImage()
const apiKey = useApiKey()

const showKey = computed(() => limits.value?.apiKeyRequired || apiKey.key.value !== null)
const keyTitle = computed(() => {
  if (!apiKey.key.value)
    return 'Enter the API key'
  return apiKey.remembered.value ? 'API key saved on this device' : 'API key saved until this tab closes'
})
</script>

<template>
  <header class="top">
    <a class="brand" href="/">
      <img src="/favicon.svg" alt="" width="26" height="26">
      Image Mason
      <span v-if="version" class="version">{{ version }}</span>
    </a>

    <p v-if="image" class="crumb">
      <b :title="image.name">{{ image.name }}</b>
      <span>{{ formatSize(image.width, image.height) }}, {{ formatBytes(image.size) }}</span>
    </p>

    <nav class="end">
      <button class="button" type="button" aria-label="Open image" @click="emit('open')">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 16V4M7 9l5-5 5 5M4 20h16" /></svg>
        <span class="label">Open image</span>
      </button>
      <button
        v-if="showKey"
        class="button"
        :class="{ missing: !apiKey.key.value }"
        type="button"
        aria-label="API key"
        :title="keyTitle"
        @click="emit('manageKey')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="8" cy="15" r="4" /><path d="M11 12l9-9M17 6l3 3M15 8l2 2" /></svg>
        <span class="label">API key</span>
      </button>
      <a class="button" href="/docs" aria-label="API examples">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 7l-5 5 5 5M16 7l5 5-5 5" /></svg>
        <span class="label">API examples</span>
      </a>
      <button
        class="button"
        type="button"
        :aria-label="theme === 'dark' ? 'Switch to the light theme' : 'Switch to the dark theme'"
        :title="theme === 'dark' ? 'Light theme' : 'Dark theme'"
        @click="toggle"
      >
        <svg v-if="theme === 'dark'" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M20 14.5A8 8 0 1 1 9.5 4 6.5 6.5 0 0 0 20 14.5z" />
        </svg>
        <svg v-else viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
        </svg>
      </button>
    </nav>
  </header>
</template>

<style scoped lang="scss">
.top {
  display: flex;
  align-items: center;
  gap: 16px;
  min-width: 0;
  padding: 0 12px 0 18px;
  background: var(--panel);
  border-bottom: 1px solid var(--rule);
}

.brand {
  display: flex;
  align-items: center;
  gap: 9px;
  color: inherit;
  text-decoration: none;
  white-space: nowrap;
  font: 700 17px var(--font-cond);
}

.version {
  color: var(--muted);
  font-weight: 500;
  font-size: 14px;
}

.crumb {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
  margin: 0;
  color: var(--muted);
  white-space: nowrap;
  font-variant-numeric: tabular-nums;

  b {
    overflow: hidden;
    text-overflow: ellipsis;
    color: var(--ink);
    font-weight: 500;
  }
}

.end {
  display: flex;
  gap: 6px;
  margin-left: auto;
}

// No key saved yet on a server that needs one.
.missing { border-color: var(--flag); color: var(--flag); }

@media (max-width: 1100px) {
  .crumb span { display: none; }
}

@media (max-width: 900px) {
  .label, .version { display: none; }
  .top { gap: 10px; padding-left: 12px; }
}

@media (max-width: 480px) {
  .crumb { display: none; }
}
</style>
