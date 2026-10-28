<script setup lang="ts">
import { useImage } from '../../composables/useImage'
import { useSettings } from '../../composables/useSettings'
import { wholeImage } from '../../geometry'
import CropStep from './CropStep.vue'
import FormatGroup from './FormatGroup.vue'
import QualityGroup from './QualityGroup.vue'
import ResizeStep from './ResizeStep.vue'
import RunPanel from './RunPanel.vue'

// One transform: Quality and Format always, Resize and Crop as steps turned on with their checkboxes, and the run
// panel under them.

const emit = defineEmits<{ enterKey: [] }>()

const { settings, reset } = useSettings()
const { image, rect } = useImage()

function resetAll() {
  reset()
  if (image.value)
    Object.assign(rect, wholeImage(image.value))
}
</script>

<template>
  <aside class="inspector" aria-label="Settings">
    <section class="scroll" aria-labelledby="settings-heading">
      <h2 id="settings-heading">Transform</h2>
      <p class="lede">The smallest file that still looks the same. Resize or crop it too, if you like.</p>
      <QualityGroup />
      <FormatGroup v-model="settings.format" />
      <ResizeStep />
      <CropStep />
      <div class="group reset">
        <button class="text-button" type="button" @click="resetAll">Reset settings to defaults</button>
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

@media (max-width: 900px) {
  .inspector { border-left: 0; }
  .scroll { overflow: visible; }
}
</style>
