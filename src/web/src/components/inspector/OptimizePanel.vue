<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useSettings } from '../../composables/useSettings'
import { chosenFormat } from '../../settings'
import FormatField from './FormatField.vue'
import MaxWidthField from './MaxWidthField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
const { image } = useImage()

// Matches the format menu: a saved format that's the input's own shows as "Keep", which leaves this on.
const formatChosen = computed(() => chosenFormat(settings.format, image.value?.format) !== null)
</script>

<template>
  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <FormatField id="optimize-format" v-model="settings.format" />
    <MaxWidthField id="optimize-max-width" />
    <label class="check">
      <input v-model="settings.preferJpeg" type="checkbox" :disabled="formatChosen">
      <span>
        Turn PNG photos into JPEG
        <small v-if="formatChosen">Not used when a format is chosen.</small>
        <small v-else>Only PNGs that look like photos; graphics and screenshots stay PNG.</small>
      </span>
    </label>
  </div>
</template>
