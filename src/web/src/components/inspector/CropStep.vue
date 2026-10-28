<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import { type Rect, centeredRect, insetRect, rectCovers, rectFits } from '../../geometry'
import { type Shape, shapeAspect } from '../../settings'
import NumberField from '../controls/NumberField.vue'
import SegmentedControl from '../controls/SegmentedControl.vue'

// The area to keep, in the frame on the image: dragged there, or typed here. A shape sets its largest centered area
// and keeps corner drags to it; typing an area goes back to Free.

const { settings } = useSettings()
const { image, rect } = useImage()
const { plan } = usePlan()

const shapeOptions: { value: Shape, label: string }[] = [
  { value: 'free', label: 'Free' },
  { value: '1:1', label: '1:1' },
  { value: '4:3', label: '4:3' },
  { value: '3:4', label: '3:4' },
  { value: '16:9', label: '16:9' },
]

const shape = computed({
  get: () => settings.shape,
  set: (value: Shape) => {
    settings.shape = value
    const aspect = shapeAspect(value)
    if (aspect && image.value)
      Object.assign(rect, centeredRect(aspect, image.value))
  },
})

// Turned on with no area yet (the whole image, or one that doesn't fit), the frame starts inside the edges.
function setOn(event: Event) {
  settings.crop = (event.target as HTMLInputElement).checked
  const source = image.value
  if (settings.crop && source && (rectCovers(rect, source) || !rectFits(rect, source))) {
    const aspect = shapeAspect(settings.shape)
    Object.assign(rect, aspect ? centeredRect(aspect, source) : insetRect(source))
  }
}

// The four fields edit the shared rect; empty or not-a-number is NaN there, which the plan reports.
const sides = (['left', 'top', 'width', 'height'] as const).map(side => ({
  side,
  label: side[0]!.toUpperCase() + side.slice(1),
  model: computed({
    get: () => rect[side],
    set: (value: number | null) => {
      rect[side as keyof Rect] = value ?? NaN
      settings.shape = 'free'
    },
  }),
}))
</script>

<template>
  <section class="group step" :class="{ on: settings.crop }" aria-label="Crop">
    <label class="step-head">
      <input type="checkbox" :checked="settings.crop" :disabled="!image" @change="setOn">
      <b>Crop</b>
      <small>Keep part of the image</small>
    </label>

    <div v-if="settings.crop && image" class="step-body">
      <SegmentedControl v-model="shape" :options="shapeOptions" label="Shape" />
      <p class="hint">Drag the frame on the image, or its corners.</p>
      <div class="quad">
        <label v-for="field in sides" :key="field.side">
          <small>{{ field.label }}</small>
          <NumberField
            v-model="field.model.value"
            :aria-describedby="plan.errors.rect ? 'crop-area-error' : undefined"
            :aria-invalid="plan.errors.rect ? 'true' : undefined"
          />
        </label>
      </div>
      <p v-if="plan.errors.rect" id="crop-area-error" class="field-error">{{ plan.errors.rect }}</p>
    </div>
  </section>
</template>

<style scoped lang="scss">
.quad { margin-top: 10px; }
</style>
