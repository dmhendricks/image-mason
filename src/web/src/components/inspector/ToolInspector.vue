<script setup lang="ts">
import { type Component, computed } from 'vue'
import { useSettings } from '../../composables/useSettings'
import type { Tool } from '../../settings'
import ConvertPanel from './ConvertPanel.vue'
import CropPanel from './CropPanel.vue'
import OptimizePanel from './OptimizePanel.vue'
import ResizePanel from './ResizePanel.vue'
import RunPanel from './RunPanel.vue'

// The current tool's settings, and the run panel under them.

const emit = defineEmits<{ enterKey: [] }>()

const { settings, reset } = useSettings()

const tools: Record<Tool, { title: string, lede: string, panel: Component }> = {
  optimize: { title: 'Optimize', lede: 'The smallest file that still looks the same.', panel: OptimizePanel },
  resize: { title: 'Resize', lede: 'Scales the image to a box. The pink box is the output, drawn to scale.', panel: ResizePanel },
  crop: { title: 'Crop', lede: 'Keeps one area of the image, inside the pink frame.', panel: CropPanel },
  convert: { title: 'Convert', lede: 'Changes the format, held to the same quality floors.', panel: ConvertPanel },
}

const current = computed(() => tools[settings.tool])
</script>

<template>
  <aside class="inspector" aria-label="Settings">
    <section id="tool-panel" class="scroll" role="tabpanel" :aria-labelledby="`tab-${settings.tool}`">
      <h2>{{ current.title }}</h2>
      <p class="lede">{{ current.lede }}</p>
      <component :is="current.panel" />
      <div class="group reset">
        <button class="text-button" type="button" @click="reset">Reset settings to defaults</button>
      </div>
    </section>
    <RunPanel @enter-key="emit('enterKey')" />
  </aside>
</template>

<style scoped lang="scss">
.inspector {
  display: flex;
  flex-direction: column;
  min-height: 0;
  background: var(--panel);
  border-left: 1px solid var(--rule);
}

.scroll {
  flex: 1;
  overflow-y: auto;
}

h2 {
  margin: 0;
  padding: 14px 16px 2px;
  font: 700 20px/1.2 var(--font-cond);
}

.lede {
  margin: 0;
  padding: 0 16px 12px;
  color: var(--muted);
}

.reset { padding-block: 10px; }

.text-button {
  padding: 0;
  border: 0;
  background: none;
  color: var(--muted);
  font-size: 13px;
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;

  &:hover { color: var(--ink); }
}

@media (max-width: 900px) {
  .inspector { border-left: 0; }
  .scroll { overflow: visible; }
}
</style>
