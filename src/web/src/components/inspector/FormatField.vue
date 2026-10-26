<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useServer } from '../../composables/useServer'
import { formatName, outputFormats } from '../../format'
import { type ImageFormat, chosenFormat } from '../../settings'

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

const formats = computed(() => outputFormats(limits.value).filter(format => format !== image.value?.format))

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
