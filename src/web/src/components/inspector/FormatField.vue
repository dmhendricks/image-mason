<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useServer } from '../../composables/useServer'
import { formatName } from '../../format'
import { type ImageFormat, imageFormats } from '../../settings'

// The output format: "Keep" (null) sends no format, so the result stays in the input's.

const model = defineModel<ImageFormat | null>({ required: true })
defineProps<{ id: string }>()

const { limits } = useServer()
const { image } = useImage()

const formats = computed(() =>
  (limits.value?.formats.map(f => f.name) ?? [...imageFormats]).filter((f): f is ImageFormat =>
    (imageFormats as readonly string[]).includes(f)))

const keepLabel = computed(() => image.value ? `Keep (${formatName(image.value.format)})` : 'Keep the same')
</script>

<template>
  <div class="prop">
    <label :for="id">Format</label>
    <select :id="id" v-model="model" class="field">
      <option :value="null">{{ keepLabel }}</option>
      <option v-for="format in formats" :key="format" :value="format">{{ formatName(format) }}</option>
    </select>
  </div>
</template>
