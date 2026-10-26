<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../composables/useImage'
import { usePlan } from '../composables/usePlan'
import { useServer } from '../composables/useServer'
import { formatBytes, formatMegapixels, formatSize } from '../format'
import { describeMethod } from '../methods'

// The numbers behind what's on the canvas: the original, the result, its score and how it was made.

const { image } = useImage()
const { result } = usePlan()
const { limits } = useServer()

const seconds = computed(() => {
  const ms = result.value?.stats.durationMs ?? 0
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
})
</script>

<template>
  <footer class="status">
    <template v-if="image">
      <span>Original <b>{{ formatBytes(image.size) }}</b> {{ formatSize(image.width, image.height) }}</span>
      <template v-if="result">
        <span>Result <b>{{ formatBytes(result.stats.size) }}</b> {{ formatSize(result.stats.width, result.stats.height) }}</span>
        <span v-if="result.stats.score !== null">
          Score <b>{{ result.stats.score.toFixed(1) }}</b><template v-if="result.stats.minScore !== null"> (floor {{ result.stats.minScore }})</template>
        </span>
        <span v-else>Score <b>exact</b></span>
        <span :title="result.stats.method">Made with <b>{{ describeMethod(result.stats.method) }}</b></span>
        <span>in <b>{{ seconds }}</b></span>
      </template>
    </template>
    <span v-else>No image open</span>

    <span v-if="limits" class="end">
      Up to {{ formatBytes(limits.maxInputSize) }} and {{ formatMegapixels(limits.maxPixels) }} per image
    </span>
  </footer>
</template>

<style scoped lang="scss">
.status {
  display: flex;
  align-items: center;
  gap: 22px;
  min-width: 0;
  padding: 0 14px;
  background: var(--panel);
  border-top: 1px solid var(--rule);
  color: var(--muted);
  font: 500 13px var(--font-cond);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow-x: auto;

  b { color: var(--ink); font-weight: 600; }
}

.end { margin-left: auto; }

@media (max-width: 900px) {
  .status {
    flex-wrap: wrap;
    gap: 4px 16px;
    padding: 8px 14px;
    white-space: normal;
  }

  .end { margin-left: 0; }
}
</style>
