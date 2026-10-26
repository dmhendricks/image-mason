<script setup lang="ts">
import { computed } from 'vue'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import type { Fit, Focus } from '../../settings'
import NumberField from '../controls/NumberField.vue'
import SegmentedControl from '../controls/SegmentedControl.vue'
import FormatField from './FormatField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
const { plan } = usePlan()

const fitOptions: { value: Fit, label: string }[] = [
  { value: 'contain', label: 'Inside' },
  { value: 'cover', label: 'Fill box' },
  { value: 'fill', label: 'Stretch' },
]

const focusOptions: { value: Focus, label: string, title: string }[] = [
  { value: 'center', label: 'Center', title: 'Keep the middle' },
  { value: 'attention', label: 'Subject', title: 'Keep the most eye-catching area: skin tones, strong colors, edges' },
  { value: 'entropy', label: 'Detail', title: 'Keep the area with the most detail' },
]

const fitHints: Record<Fit, string> = {
  contain: 'Fits inside the box, keeping the shape.',
  cover: 'Fills the box, keeping the shape, and crops what overflows.',
  fill: 'Stretches to exactly the box, changing the shape.',
}

// Both sides empty still blocks the run, but it's where every resize starts, so it reads as a hint rather than
// marking a field wrong. Values that are actually wrong (not a number, out of range) still show as errors.
const empty = computed(() => settings.width === null && settings.height === null)
const sizeError = computed(() => empty.value ? undefined : plan.value.errors.width ?? plan.value.errors.height)
</script>

<template>
  <div class="group">
    <h3>Size in pixels</h3>
    <div class="pair">
      <NumberField
        v-model="settings.width"
        placeholder="Auto"
        aria-label="Width"
        :aria-invalid="plan.errors.width && !empty ? 'true' : undefined"
      />
      <span aria-hidden="true">×</span>
      <NumberField
        v-model="settings.height"
        placeholder="Auto"
        aria-label="Height"
        :aria-invalid="plan.errors.height ? 'true' : undefined"
      />
    </div>
    <p v-if="sizeError" class="field-error">{{ sizeError }}</p>
    <p v-else-if="empty" class="hint">Enter a width, a height or both. Leave one side empty to keep the shape.</p>
    <p v-else class="hint">Leave one side empty to keep the shape.</p>
  </div>

  <div class="group">
    <h3>Fit</h3>
    <SegmentedControl v-model="settings.fit" :options="fitOptions" label="Fit" />
    <p class="hint">{{ fitHints[settings.fit] }}</p>
    <div class="prop">
      <span>Keep</span>
      <SegmentedControl
        v-model="settings.focus"
        :options="focusOptions"
        label="Which part Fill box keeps"
        :disabled="settings.fit !== 'cover'"
      />
    </div>
    <label class="check">
      <input v-model="settings.upscale" type="checkbox" :disabled="settings.fit === 'fill'">
      <span>
        Enlarge smaller images
        <small v-if="settings.fit === 'fill'">Stretch always reaches the size.</small>
      </span>
    </label>
  </div>

  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <FormatField id="resize-format" v-model="settings.format" />
  </div>
</template>
