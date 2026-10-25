<script setup lang="ts">
import { useTheme } from '../composables/useTheme'

defineProps<{ version: string | null }>()

const { theme, toggle } = useTheme()
</script>

<template>
  <header class="top">
    <a class="brand" href="/">
      <img src="/favicon.svg" alt="" width="26" height="26">
      Image Mason
      <span v-if="version" class="version">{{ version }}</span>
    </a>

    <nav class="end">
      <a class="button" href="/docs">API examples</a>
      <button
        class="button icon"
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
  height: 48px;
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

  @media (max-width: 480px) { display: none; }
}

.end {
  margin-left: auto;
  display: flex;
  gap: 6px;
}

.button {
  display: inline-flex;
  align-items: center;
  height: 32px;
  padding: 0 10px;
  border: 1px solid var(--rule);
  border-radius: var(--radius);
  background: var(--panel);
  color: var(--muted);
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;

  &:hover { color: var(--ink); }
}

.icon svg {
  width: 17px;
  height: 17px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
}
</style>
