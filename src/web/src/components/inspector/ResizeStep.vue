<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import { formatSize } from '../../format'
import NumberField from '../controls/NumberField.vue'

// Width and height, starting at the size of what's resized (the crop area, or the image). The chain button keeps the
// shape: typing one side sets the other. Unchained, the image is stretched to exactly the two.

const { settings } = useSettings()
const { image } = useImage()
const { plan } = usePlan()

type Side = 'width' | 'height'

// Each field shows its typed side, else the size it follows. Clearing one leaves it empty (NaN, which the plan
// reports) rather than filling it straight back in.
function side(name: Side, other: Side) {
  return computed({
    get: () => settings[name] ?? plan.value.output?.[name] ?? plan.value.source?.[name] ?? null,
    set: (value: number | null) => {
      settings[name] = value ?? NaN
      if (settings.lock)
        settings[other] = null
    },
  })
}
const width = side('width', 'height')
const height = side('height', 'width')

function setOn(event: Event) {
  settings.resize = (event.target as HTMLInputElement).checked
  if (settings.resize)
    resetSize()
}

function resetSize() {
  settings.width = null
  settings.height = null
}

function toggleLock() {
  const output = plan.value.output
  if (settings.lock) {
    // Unchained, both sides are kept, starting from the size shown.
    if (output && (settings.width !== null || settings.height !== null))
      Object.assign(settings, { width: output.width, height: output.height })
  } else if (settings.width !== null) {
    // Chained, the width stays and the height follows the shape.
    settings.height = null
  }
  settings.lock = !settings.lock
}

const error = computed(() => plan.value.errors.width ?? plan.value.errors.height)

const hint = computed(() => {
  const { source, output, cropped } = plan.value
  if (!source || !output)
    return null
  const of = cropped ? 'the cropped area' : 'the original'
  if (output.width === source.width && output.height === source.height)
    return { text: `Same size as ${of}. Type a new width or height.`, reset: null }

  const scale = output.width / source.width
  const stretched = Math.abs(output.width / output.height - source.width / source.height) > 0.01 * source.width / source.height
  const text = stretched ? 'Stretches to exactly this size.'
    : scale < 1 ? `Shrinks to ${Math.round(scale * 100)}% of ${of}.`
      : `Enlarges to ${Math.round(scale * 100)}% of ${of}; enlarging can't add detail.`
  return { text, reset: formatSize(source.width, source.height) }
})
</script>

<template>
  <section class="group step" :class="{ on: settings.resize }" aria-label="Resize">
    <label class="step-head">
      <input type="checkbox" :checked="settings.resize" :disabled="!image" @change="setOn">
      <b>Resize</b>
      <small>Make it smaller or larger</small>
    </label>

    <div v-if="settings.resize && image" class="step-body">
      <div class="size">
        <label>
          <small>Width</small>
          <NumberField
            v-model="width"
            unit="px"
            aria-describedby="resize-note"
            :aria-invalid="plan.errors.width ? 'true' : undefined"
          />
        </label>
        <button
          class="lock"
          type="button"
          :aria-pressed="settings.lock"
          aria-label="Keep the shape"
          :title="settings.lock
            ? 'Keeping the shape: click to set width and height separately'
            : 'Width and height are separate: click to keep the shape'"
          @click="toggleLock"
        >
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true">
            <path v-if="settings.lock" d="M8 8H6a4 4 0 0 0 0 8h2M16 8h2a4 4 0 0 1 0 8h-2M8.5 12h7" />
            <path v-else d="M8 8H6a4 4 0 0 0 0 8h2M16 8h2a4 4 0 0 1 0 8h-2M10 5.5l-1-2M14 18.5l1 2" />
          </svg>
        </button>
        <label>
          <small>Height</small>
          <NumberField
            v-model="height"
            unit="px"
            aria-describedby="resize-note"
            :aria-invalid="plan.errors.height ? 'true' : undefined"
          />
        </label>
      </div>
      <p v-if="error" id="resize-note" class="field-error">{{ error }}</p>
      <p v-else-if="hint" id="resize-note" class="hint">
        {{ hint.text }}
        <button v-if="hint.reset" class="text-button" type="button" @click="resetSize">Reset to {{ hint.reset }}</button>
      </p>
    </div>
  </section>
</template>

<style scoped lang="scss">
.size {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 32px minmax(0, 1fr);
  gap: 6px;
  align-items: end;

  small {
    display: block;
    margin-bottom: 3px;
    color: var(--muted);
    font-size: 12px;
  }
}

.lock {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  padding: 0;
  border: 1px solid var(--rule);
  border-radius: var(--radius);
  background: var(--panel);
  color: var(--muted);
  cursor: pointer;

  svg { width: 18px; height: 18px; stroke-width: 1.8; }
  &:hover { color: var(--ink); }
  &[aria-pressed="true"] { border-color: var(--flag); background: var(--flag-soft); color: var(--flag); }
}
</style>
