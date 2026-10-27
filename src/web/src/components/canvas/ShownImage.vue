<script setup lang="ts">
import { ref, watchPostEffect } from 'vue'

// An image on the board: its screen-sized preview when it has one, so a large image isn't held decoded at full size,
// else the file itself. full shows the file anyway, for a view that enlarges part of the image past the preview.

const props = defineProps<{ url: string, preview: ImageBitmap | null, alt: string, full?: boolean }>()

const canvas = ref<HTMLCanvasElement | null>(null)

watchPostEffect(() => {
  const element = canvas.value
  const context = element?.getContext('2d')
  if (element && context && props.preview) {
    context.clearRect(0, 0, element.width, element.height)
    context.drawImage(props.preview, 0, 0)
  }
})
</script>

<template>
  <canvas
    v-if="preview && !full"
    ref="canvas"
    :width="preview.width"
    :height="preview.height"
    role="img"
    :aria-label="alt"
  />
  <img v-else :src="url" :alt="alt">
</template>
