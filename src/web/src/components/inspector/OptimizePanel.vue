<script setup lang="ts">
import { useSettings } from '../../composables/useSettings'
import FormatField from './FormatField.vue'
import MaxWidthField from './MaxWidthField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
</script>

<template>
  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <FormatField id="optimize-format" v-model="settings.format" />
    <MaxWidthField id="optimize-max-width" />
    <label class="check">
      <input v-model="settings.preferJpeg" type="checkbox" :disabled="settings.format !== null">
      <span>
        Turn PNG photos into JPEG
        <small v-if="settings.format !== null">Not used when a format is chosen.</small>
        <small v-else>Only PNGs that look like photos; graphics and screenshots stay PNG.</small>
      </span>
    </label>
  </div>
</template>
