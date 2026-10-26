<script setup lang="ts">
import { computed } from 'vue'
import { usePlan } from '../../composables/usePlan'
import { useServer } from '../../composables/useServer'
import { useSettings } from '../../composables/useSettings'
import type { Quality } from '../../settings'
import NumberField from '../controls/NumberField.vue'
import SegmentedControl from '../controls/SegmentedControl.vue'

const { settings } = useSettings()
const { limits } = useServer()
const { plan } = usePlan()

const options: { value: Quality, label: string }[] = [
  { value: 'lossless', label: 'Lossless' },
  { value: 'high', label: 'High' },
  { value: 'medium', label: 'Medium' },
  { value: 'low', label: 'Low' },
]

const lossless = computed(() => settings.quality === 'lossless')

// What each preset promises, with its floor from the server (IMAGE_MASON_*_MIN_SCORE).
const hint = computed(() => {
  const floor = limits.value?.presets[settings.quality]
  const score = floor == null ? '' : `Score ${floor} or better: `
  switch (settings.quality) {
    case 'lossless':
      return 'Identical pixels; only the encoding is redone. A crop, resize or new format still re-encodes, held to a high score.'
    case 'high':
      return `${score}no visible difference when viewed normally.`
    case 'medium':
      return `${score}no visible difference even side by side.`
    case 'low':
      return `${score}small differences side by side, for the smallest files.`
  }
  return ''
})

const minScore = computed({
  get: () => settings.minScore,
  set: value => { settings.minScore = value ?? NaN },
})
</script>

<template>
  <div class="group">
    <h3>Quality</h3>
    <SegmentedControl v-model="settings.quality" :options="options" label="Quality" />
    <p class="hint">{{ hint }}</p>

    <label class="check">
      <input v-model="settings.customScore" type="checkbox" :disabled="lossless">
      <span>
        Custom minimum score
        <small>{{ lossless ? 'Lossless has no score to meet.' : 'Replaces the preset\'s floor, 0 to 100.' }}</small>
      </span>
    </label>

    <div v-if="settings.customScore && !lossless" class="score">
      <input
        v-model.number="settings.minScore"
        type="range"
        min="0"
        max="100"
        step="1"
        aria-label="Minimum score"
      >
      <NumberField
        v-model="minScore"
        decimal
        aria-label="Minimum score"
        :aria-invalid="plan.errors.minScore ? 'true' : undefined"
      />
    </div>
    <p v-if="plan.errors.minScore" class="field-error">{{ plan.errors.minScore }}</p>
  </div>
</template>

<style scoped lang="scss">
.score {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 64px;
  gap: 10px;
  align-items: center;
  margin-top: 8px;

  input[type="range"] { accent-color: var(--flag); }
}
</style>
