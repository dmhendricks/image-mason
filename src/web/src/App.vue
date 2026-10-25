<script setup lang="ts">
import AppHeader from './components/AppHeader.vue'
import ServerPanel from './components/ServerPanel.vue'
import { useServer } from './composables/useServer'

const { limits, version, error, loading, reload } = useServer()
</script>

<template>
  <div class="app">
    <AppHeader :version="version" />
    <main class="canvas">
      <ServerPanel :limits="limits" :error="error" :loading="loading" @reload="reload" />
    </main>
  </div>
</template>

<style scoped lang="scss">
.app {
  min-height: 100dvh;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
}

// The drawing board: a fine grid under everything on the canvas.
.canvas {
  display: grid;
  place-items: center;
  padding: 32px 16px;
  background-color: var(--canvas);
  background-image:
    linear-gradient(var(--grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--grid) 1px, transparent 1px);
  background-size: 24px 24px;
}
</style>
