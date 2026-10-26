<script setup lang="ts">
import { computed } from 'vue'
import { useServer } from '../../composables/useServer'
import { useSettings } from '../../composables/useSettings'
import { formatName } from '../../format'
import { type ImageFormat, imageFormats } from '../../settings'
import SegmentedControl from '../controls/SegmentedControl.vue'
import MaxWidthField from './MaxWidthField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
const { limits } = useServer()

const options = computed(() =>
  (limits.value?.formats.map(f => f.name) ?? [...imageFormats])
    .filter((f): f is ImageFormat => (imageFormats as readonly string[]).includes(f))
    .map(value => ({ value, label: formatName(value) })))

const hints: Record<ImageFormat, string> = {
  jpeg: 'Opens everywhere. Best for photos; no transparency.',
  png: 'Lossless, with transparency. Best for graphics and screenshots; large for photos.',
  webp: 'Usually smaller than JPEG, with transparency. Every current browser shows it.',
  avif: 'Usually the smallest, with transparency, but the slowest to make.',
}
</script>

<template>
  <div class="group">
    <h3>Format</h3>
    <SegmentedControl v-model="settings.convertTo" :options="options" label="Format" />
    <p class="hint">{{ hints[settings.convertTo] }}</p>
  </div>

  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <MaxWidthField id="convert-max-width" />
  </div>
</template>
