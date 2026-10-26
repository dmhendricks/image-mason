<script setup lang="ts">
import { computed } from 'vue'
import { useImage } from '../../composables/useImage'
import { useSettings } from '../../composables/useSettings'
import { chosenFormat } from '../../settings'
import HelpTip from '../controls/HelpTip.vue'
import FormatField from './FormatField.vue'
import MaxWidthField from './MaxWidthField.vue'
import QualityGroup from './QualityGroup.vue'

const { settings } = useSettings()
const { image } = useImage()

// Matches the format menu: a saved format that's the input's own shows as "Keep", which leaves this on.
const formatChosen = computed(() => chosenFormat(settings.format, image.value?.format) !== null)

// The server only looks for photos in PNG inputs. Shown with no image open, so it can be set ahead of time.
const forPng = computed(() => !image.value || image.value.format === 'png')
</script>

<template>
  <QualityGroup />

  <div class="group">
    <h3>Output</h3>
    <FormatField id="optimize-format" v-model="settings.format" />
    <MaxWidthField id="optimize-max-width" />
    <label v-if="forPng" class="check">
      <input v-model="settings.preferJpeg" type="checkbox" :disabled="formatChosen">
      <span>
        Turn PNG photos into JPEG
        <small>
          <template v-if="formatChosen">Only used when Format is set to Keep.</template>
          <template v-else>Only PNGs that look like photos; graphics and screenshots stay PNG.</template>
          <HelpTip label="What counts as a photo?">
            <p>A PNG becomes a JPEG only when it has all of these:</p>
            <ul>
              <li><strong>Full color.</strong> RGB, not a palette or grayscale PNG, and not interlaced.</li>
              <li><strong>No transparency.</strong> Every pixel is fully opaque, since JPEG can't store transparency.</li>
              <li>
                <strong>Lots of colors.</strong> At least 4,096 different colors across a grid of up to 128 × 128
                sampled pixels (fewer for small images).
              </li>
            </ul>
            <p>
              Photos have smooth gradients and many shades. Screenshots, logos and diagrams use a few flat colors,
              which PNG stores more sharply and often smaller.
            </p>
          </HelpTip>
        </small>
      </span>
    </label>
  </div>
</template>
