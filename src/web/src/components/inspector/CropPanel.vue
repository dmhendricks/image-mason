<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import { formatSize } from '../../format'
import { type Rect, centeredRect, wholeImage } from '../../geometry'
import NumberField from '../controls/NumberField.vue'
import FormatField from './FormatField.vue'
import MaxWidthField from './MaxWidthField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
const { image, rect } = useImage()
const { plan } = usePlan()

const shapes = [
  { label: '1:1', aspect: 1 },
  { label: '4:3', aspect: 4 / 3 },
  { label: '3:4', aspect: 3 / 4 },
  { label: '16:9', aspect: 16 / 9 },
]

function useShape(aspect: number | null) {
  if (image.value)
    Object.assign(rect, aspect === null ? wholeImage(image.value) : centeredRect(aspect, image.value))
}

// The four fields edit the shared rect; empty or not-a-number is NaN there, which the plan reports.
const sides = (['left', 'top', 'width', 'height'] as const).map(side => ({
  side,
  label: side[0]!.toUpperCase() + side.slice(1),
  model: computed({
    get: () => rect[side],
    set: (value: number | null) => { rect[side as keyof Rect] = value ?? NaN },
  }),
}))
</script>

<template>
  <div class="group">
    <h3>Shape</h3>
    <div class="seg shapes" role="group" aria-label="Shape">
      <button type="button" :disabled="!image" @click="useShape(null)">Whole</button>
      <button v-for="shape in shapes" :key="shape.label" type="button" :disabled="!image" @click="useShape(shape.aspect)">
        {{ shape.label }}
      </button>
    </div>
    <p class="hint">Sets the largest area of that shape, centered.</p>
  </div>

  <div class="group">
    <h3>Area in pixels</h3>
    <div class="quad">
      <label v-for="field in sides" :key="field.side">
        <small>{{ field.label }}</small>
        <NumberField
          v-model="field.model.value"
          :disabled="!image"
          :aria-invalid="plan.errors.rect ? 'true' : undefined"
        />
      </label>
    </div>
    <p v-if="plan.errors.rect" class="field-error">{{ plan.errors.rect }}</p>
    <p v-else-if="image" class="hint">Measured on the image as shown, {{ formatSize(image.width, image.height) }}.</p>
    <p v-else class="hint">Open an image to set the area.</p>
  </div>

  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <FormatField id="crop-format" v-model="settings.format" />
    <MaxWidthField id="crop-max-width" />
  </div>
</template>

<style scoped lang="scss">
.shapes button:active:not(:disabled) { background: var(--ink); color: var(--panel); }
</style>
