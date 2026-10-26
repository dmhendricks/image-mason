<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import { formatBytes, formatChange, formatSize } from '../../format'
import { rectFits } from '../../geometry'
import DimensionLine from './DimensionLine.vue'
import EmptyCanvas from './EmptyCanvas.vue'

// The drawing board: the image with its dimension lines; the output box or crop frame to scale before a run; the
// original and the result split by a divider after one. Zoom, flip and the results strip come with the full
// comparison viewer.

/** A file is being dragged over the window. */
defineProps<{ fileOver: boolean }>()
const emit = defineEmits<{ open: [] }>()

const { image, rect, openError, dismissError } = useImage()
const { plan, result } = usePlan()
const { settings } = useSettings()

// The board's content box, so the frame can be sized to fit it (never above 100%, so pixels stay honest). On
// narrow screens the page scrolls, so the board takes the image's height, up to most of the screen.
const board = ref<HTMLElement | null>(null)
const space = ref({ width: 0, height: 0 })
const narrow = window.matchMedia('(max-width: 900px)')
const observer = new ResizeObserver(([entry]) => {
  if (entry) {
    const height = narrow.matches ? window.innerHeight * 0.65 : entry.contentRect.height
    space.value = { width: entry.contentRect.width, height }
  }
})
watch(board, (element, previous) => {
  if (previous)
    observer.unobserve(previous)
  if (element)
    observer.observe(element)
})
onBeforeUnmount(() => observer.disconnect())

const resultSize = computed(() => result.value && image.value
  ? { width: result.value.stats.width || image.value.width, height: result.value.stats.height || image.value.height }
  : null)

/** The result has the original's size, so the two can share the stage. */
const compare = computed(() => !!resultSize.value && !!image.value
  && resultSize.value.width === image.value.width && resultSize.value.height === image.value.height)

/** What the frame measures: the result when it has a size of its own, else the image. */
const shown = computed(() => (!compare.value && resultSize.value) || image.value)

const frameStyle = computed(() => {
  const size = shown.value
  if (!size || space.value.width === 0)
    return {}
  const scale = Math.min(space.value.width / size.width, space.value.height / size.height, 1)
  return { width: `${size.width * scale}px`, height: `${size.height * scale}px`, '--split': `${split.value}%` }
})

// Before a run: where the output lands, drawn at the original's scale from the top left corner.
const outputBox = computed(() => {
  const output = plan.value.output
  const source = image.value
  if (result.value || !output || !source || settings.tool === 'crop')
    return null
  if (output.width === source.width && output.height === source.height)
    return null
  // An enlarged output can't be drawn past the frame; shrink the drawing, keep the numbers.
  const fit = Math.min(1, source.width / output.width, source.height / output.height)
  return {
    style: { width: `${output.width / source.width * fit * 100}%`, height: `${output.height / source.height * fit * 100}%` },
    width: `${output.width} px`,
    height: `${output.height} px`,
  }
})

const cropBox = computed(() => {
  const source = image.value
  if (result.value || !source || settings.tool !== 'crop' || plan.value.errors.rect || !rectFits(rect, source))
    return null
  return {
    style: {
      left: `${rect.left / source.width * 100}%`,
      top: `${rect.top / source.height * 100}%`,
      width: `${rect.width / source.width * 100}%`,
      height: `${rect.height / source.height * 100}%`,
    },
    label: `${formatSize(rect.width, rect.height)} from ${rect.left}, ${rect.top}`,
  }
})

// The split divider: drag anywhere on the frame, or focus it and use the arrow keys.
const split = ref(50)
let sliding = false

function splitAt(event: PointerEvent) {
  const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect()
  split.value = Math.min(100, Math.max(0, (event.clientX - bounds.left) / bounds.width * 100))
}

function onPointerDown(event: PointerEvent) {
  if (!compare.value || event.button !== 0)
    return
  sliding = true
  const frame = event.currentTarget as HTMLElement
  frame.setPointerCapture(event.pointerId)
  splitAt(event)
}

function onPointerMove(event: PointerEvent) {
  if (sliding)
    splitAt(event)
}

function stopSliding() {
  sliding = false
}

function onDividerKey(event: KeyboardEvent) {
  const steps: Record<string, number> = { ArrowLeft: -2, ArrowRight: 2, PageDown: -10, PageUp: 10 }
  if (event.key in steps)
    split.value = Math.min(100, Math.max(0, split.value + steps[event.key]!))
  else if (event.key === 'Home' || event.key === 'End')
    split.value = event.key === 'Home' ? 0 : 100
  else
    return
  event.preventDefault()
}
</script>

<template>
  <main class="canvas" aria-label="Image">
    <div v-if="openError" class="notice" role="alert">
      <p>{{ openError }}</p>
      <button class="button" type="button" @click="dismissError">Dismiss</button>
    </div>

    <div v-if="!image" class="empty-board">
      <EmptyCanvas @open="emit('open')" />
    </div>

    <div v-else ref="board" class="board">
      <div
        class="frame"
        :class="{ comparing: compare }"
        :style="frameStyle"
        @pointerdown="onPointerDown"
        @pointermove="onPointerMove"
        @pointerup="stopSliding"
        @pointercancel="stopSliding"
      >
        <div class="pic">
          <img v-if="!result || compare" :src="image.url" :alt="`${image.name}, original`">
          <img v-if="result" class="result" :src="result.url" :alt="`${image.name}, ${result.label}`">

          <span v-if="result && compare" class="tag a">Original<span>{{ formatBytes(image.size) }}</span></span>
          <span v-if="result" class="tag b">{{ result.label }}<span>{{ formatBytes(result.stats.size) }}</span></span>

          <div v-if="cropBox" class="crop-box" :style="cropBox.style">
            <i /><i /><i /><i />
            <b>{{ cropBox.label }}</b>
          </div>
        </div>

        <DimensionLine
          class="top"
          orientation="horizontal"
          :label="`${shown!.width} px`"
          :flag="!!result && !compare"
        />
        <DimensionLine
          class="left"
          orientation="vertical"
          :label="`${shown!.height} px`"
          :flag="!!result && !compare"
        />

        <div v-if="outputBox" class="output-box" :style="outputBox.style">
          <DimensionLine class="below" orientation="horizontal" :label="outputBox.width" flag />
          <DimensionLine class="beside" orientation="vertical" :label="outputBox.height" flag />
        </div>

        <div
          v-if="result && compare"
          class="divider"
          role="slider"
          tabindex="0"
          aria-label="Divider between the original and the result"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-valuenow="Math.round(split)"
          :aria-valuetext="`${Math.round(split)}% original`"
          @keydown="onDividerKey"
        >
          <span>{{ formatChange(result.stats.originalSize, result.stats.size) }}</span>
        </div>
      </div>
    </div>

    <div v-if="fileOver" class="drop-overlay" aria-hidden="true">
      <span>Drop to open</span>
    </div>
  </main>
</template>

<style scoped lang="scss">
.canvas {
  position: relative;
  display: grid;
  min-height: 0;
  overflow: hidden;
  background-color: var(--canvas);
  background-image:
    linear-gradient(var(--grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--grid) 1px, transparent 1px);
  background-size: 24px 24px;
}

.empty-board {
  display: grid;
  place-items: center;
  padding: 32px 16px;
  overflow-y: auto;
}

// Room on the top and left for the dimension lines, and below and right for the output box's.
.board {
  display: grid;
  place-items: center;
  min-height: 0;
  padding: 56px 48px 44px 72px;
}

.frame {
  position: relative;
  touch-action: none;
  user-select: none;

  &.comparing { cursor: ew-resize; }
}

.pic {
  position: absolute;
  inset: 0;
  overflow: hidden;
  // Checkerboard under transparent images.
  background: repeating-conic-gradient(var(--panel-2) 0 25%, var(--panel) 0 50%) 0 0 / 16px 16px;
  box-shadow: 0 0 0 1px var(--rule), 0 6px 24px rgb(0 0 0 / 0.18);

  img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    display: block;
    pointer-events: none;
  }
}

.comparing .result { clip-path: inset(0 0 0 var(--split)); }

.top { top: -30px; }
.left { left: -34px; }

.tag {
  position: absolute;
  top: 8px;
  padding: 2px 8px;
  border-radius: 3px;
  background: rgb(0 0 0 / 0.66);
  color: #fff;
  font: 600 12px var(--font-cond);
  font-variant-numeric: tabular-nums;
  pointer-events: none;

  &.a { left: 8px; }
  &.b { right: 8px; }

  span {
    margin-left: 8px;
    font-weight: 500;
    opacity: 0.85;
  }
}

.divider {
  position: absolute;
  top: -10px;
  bottom: -10px;
  left: var(--split);
  width: 30px;
  margin-left: -15px;
  cursor: ew-resize;

  &::before {
    content: "";
    position: absolute;
    left: 14px;
    top: 0;
    bottom: 0;
    width: 2px;
    background: var(--flag);
  }

  span {
    position: absolute;
    bottom: -12px;
    left: 50%;
    translate: -50% 0;
    padding: 1px 7px;
    border-radius: 3px;
    background: var(--flag);
    color: var(--flag-ink);
    font: 600 12px var(--font-cond);
    white-space: nowrap;
  }
}

.output-box {
  position: absolute;
  left: 0;
  top: 0;
  border: 2px solid var(--flag);
  background: color-mix(in srgb, var(--flag) 14%, transparent);
  pointer-events: none;

  .below { top: auto; bottom: -24px; }
  .beside { left: auto; right: -26px; }
}

.crop-box {
  position: absolute;
  outline: 2px solid var(--flag);
  box-shadow: 0 0 0 9999px rgb(0 0 0 / 0.55);
  pointer-events: none;

  i {
    position: absolute;
    width: 10px;
    height: 10px;
    background: var(--flag);

    &:nth-of-type(1) { left: -5px; top: -5px; }
    &:nth-of-type(2) { right: -5px; top: -5px; }
    &:nth-of-type(3) { left: -5px; bottom: -5px; }
    &:nth-of-type(4) { right: -5px; bottom: -5px; }
  }

  b {
    position: absolute;
    left: 8px;
    top: 8px;
    padding: 1px 7px;
    border-radius: 3px;
    background: var(--flag);
    color: var(--flag-ink);
    font: 600 12px var(--font-cond);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
}

.notice {
  position: absolute;
  z-index: 2;
  top: 12px;
  left: 50%;
  translate: -50% 0;
  display: flex;
  align-items: center;
  gap: 12px;
  width: max-content;
  max-width: calc(100% - 32px);
  padding: 8px 8px 8px 14px;
  border-radius: 6px;
  background: var(--flag-soft);
  box-shadow: 0 4px 16px rgb(0 0 0 / 0.16);

  p { margin: 0; }
}

.drop-overlay {
  position: absolute;
  inset: 10px;
  z-index: 3;
  display: grid;
  place-items: center;
  border: 2px dashed var(--flag);
  border-radius: 8px;
  background: color-mix(in srgb, var(--canvas) 80%, transparent);
  pointer-events: none;

  span {
    padding: 6px 14px;
    border-radius: 4px;
    background: var(--flag);
    color: var(--flag-ink);
    font: 700 18px var(--font-cond);
  }
}

@media (max-width: 900px) {
  .board { padding: 44px 32px 40px 48px; }
}
</style>
