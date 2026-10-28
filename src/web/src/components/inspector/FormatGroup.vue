<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useServer } from '../../composables/useServer'
import { formatName, outputFormats } from '../../format'
import { type ImageFormat, chosenFormat } from '../../settings'

// The output format: "Keep" (null) sends no format, so the result stays in the input's. The input's own format
// isn't listed, since "Keep" names it, and a saved choice of that format shows as "Keep".

const model = defineModel<ImageFormat | null>({ required: true })

const { limits } = useServer()
const { image } = useImage()

const selected = computed({
  get: () => chosenFormat(model.value, image.value?.format),
  set: (format: ImageFormat | null) => { model.value = format },
})

const formats = computed(() => outputFormats(limits.value).filter(format => format !== image.value?.format))

const keepLabel = computed(() => image.value ? `Keep (${formatName(image.value.format)})` : 'Keep the same')

const hints: Record<ImageFormat, string> = {
  jpeg: 'Opens everywhere. Best for photos; no transparency.',
  png: 'Lossless, with transparency. Best for graphics and screenshots; large for photos.',
  webp: 'Usually smaller than JPEG, with transparency. Every current browser shows it.',
  avif: 'Usually the smallest, with transparency, but the slowest to make.',
  jxl: 'As small as AVIF, with transparency, and Lossless repacks a JPEG about 20% smaller. Not every browser shows it yet.',
}
</script>

<template>
  <div class="group">
    <h3><label for="format">Format</label></h3>
    <span class="select">
      <select id="format" v-model="selected" class="field" :aria-describedby="selected ? 'format-hint' : undefined">
        <option :value="null">{{ keepLabel }}</option>
        <option v-for="format in formats" :key="format" :value="format">{{ formatName(format) }}</option>
      </select>
    </span>
    <p v-if="selected" id="format-hint" class="hint">{{ hints[selected] }}</p>
  </div>
</template>
