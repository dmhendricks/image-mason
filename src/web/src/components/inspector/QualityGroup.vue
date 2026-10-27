<script setup lang="ts">
import { computed } from 'vue'
import { usePlan } from '../../composables/usePlan'
import { useServer } from '../../composables/useServer'
import { useSettings } from '../../composables/useSettings'
import { type Quality, effectiveQuality } from '../../settings'
import HelpTip from '../controls/HelpTip.vue'
import NumberField from '../controls/NumberField.vue'
import SegmentedControl from '../controls/SegmentedControl.vue'

// The switch picks how much visible difference is allowed: a preset's floor, or a custom score. Lossless sits
// apart as a checkbox, since it allows none at all; while it's on, the switch keeps its choice for later.

type Level = Quality | 'custom'

const { settings } = useSettings()
const { limits } = useServer()
const { plan } = usePlan()

const options: { value: Level, label: string }[] = [
  { value: 'high', label: 'High' },
  { value: 'medium', label: 'Medium' },
  { value: 'low', label: 'Low' },
  { value: 'custom', label: 'Custom' },
]

// Until a preset is chosen, the server's default is shown (and sent).
const quality = computed(() => effectiveQuality(settings.quality, limits.value?.defaultQuality))

const level = computed<Level>({
  get: () => settings.customScore ? 'custom' : quality.value,
  set: value => {
    settings.customScore = value === 'custom'
    if (value !== 'custom')
      settings.quality = value
  },
})

// What each choice promises, with the preset's floor from the server (IMAGE_MASON_*_MIN_SCORE).
const hint = computed(() => {
  if (settings.lossless)
    return 'Not used while Lossless is on.'
  if (settings.customScore)
    return 'Results must score at least this, from 0 to 100. 90 and up is hard to tell from the original.'

  const floor = limits.value?.presets[quality.value]
  const score = floor == null ? '' : `Score ${floor} or better: `
  switch (quality.value) {
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
    <SegmentedControl v-model="level" :options="options" label="Quality" :disabled="settings.lossless" />
    <p class="hint">{{ hint }}</p>

    <div v-if="settings.customScore && !settings.lossless" class="score">
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
        :aria-describedby="plan.errors.minScore ? 'min-score-error' : undefined"
      />
    </div>
    <p v-if="plan.errors.minScore" id="min-score-error" class="field-error">{{ plan.errors.minScore }}</p>

    <label class="check">
      <input v-model="settings.lossless" type="checkbox">
      <span>
        Lossless
        <HelpTip label="What does Lossless keep?">
          <p>
            Keeps the pixels exactly as they are and only redoes the file's encoding. Expect smaller savings than
            High, Medium or Low: often a few percent for JPEG, more for PNG.
          </p>
          <p>
            JPEG has no lossless mode, so a JPEG that's cropped, resized or converted from another format is
            re-encoded and held to a high score instead.
          </p>
        </HelpTip>
      </span>
    </label>
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
