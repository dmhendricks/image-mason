<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useServer } from '../../composables/useServer'
import { formatName } from '../../format'
import { type ImageFormat, chosenFormat, imageFormats } from '../../settings'

// The output format: "Keep" (null) sends no format, so the result stays in the input's. The input's own format
// isn't listed, since "Keep" names it, and a saved choice of that format shows as "Keep".

const model = defineModel<ImageFormat | null>({ required: true })
defineProps<{ id: string }>()

const { limits } = useServer()
const { image } = useImage()

const selected = computed({
  get: () => chosenFormat(model.value, image.value?.format),
  set: (format: ImageFormat | null) => { model.value = format },
})

const formats = computed(() =>
  (limits.value?.formats.map(f => f.name) ?? [...imageFormats]).filter((f): f is ImageFormat =>
    (imageFormats as readonly string[]).includes(f) && f !== image.value?.format))

const keepLabel = computed(() => image.value ? `Keep (${formatName(image.value.format)})` : 'Keep the same')
</script>

<template>
  <div class="prop">
    <label :for="id">Format</label>
    <span class="select">
      <select :id="id" v-model="selected" class="field">
        <option :value="null">{{ keepLabel }}</option>
        <option v-for="format in formats" :key="format" :value="format">{{ formatName(format) }}</option>
      </select>
    </span>
  </div>
</template>
