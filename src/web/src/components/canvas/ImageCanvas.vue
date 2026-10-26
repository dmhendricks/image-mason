<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import { formatBytes, formatChange, formatSize } from '../../format'
import { type CropHandle, type Rect, dragRect, rectFits, rectWithin, wholeImage } from '../../geometry'
import DimensionLine from './DimensionLine.vue'
import EmptyCanvas from './EmptyCanvas.vue'

// The drawing board: the image with its dimension lines; the output box or crop frame to scale before a run; the
// original and the result split by a divider after one. Zoom, flip and the results strip come with the full
// comparison viewer.

/** A file is being dragged over the window. */
defineProps<{ fileOver: boolean }>()
const emit = defineEmits<{ open: [] }>()

const { image, rect, opening, openError, cancelOpen, dismissError } = useImage()
const { plan, result } = usePlan()
const { settings } = useSettings()

// Crop's view: after a crop, the board shows just the cropped area (the result, then the original through the same
// window while the area is adjusted), with the frame around it so it can be cropped further. Every run still crops
// the original, so nothing is encoded twice. The view goes back to the whole image when the area leaves it (a Shape
// button, a typed value) or another image opens.
const cropView = ref<Rect | null>(null)
watch(image, () => { cropView.value = null })
watch(() => result.value?.id, () => {
  if (result.value && settings.tool === 'crop')
    cropView.value = { ...rect }
})

/** The result the browser couldn't display (JPEG XL, in browsers without it); it can still be downloaded. */
const undisplayable = ref<number | null>(null)

/** The part of the original on the board, in image pixels. */
const view = computed<Rect | null>(() => {
  if (!image.value)
    return null
  return settings.tool === 'crop' && cropView.value && rectWithin(rect, cropView.value) ? cropView.value : wholeImage(image.value)
})

/** Positions the original so only the view shows; nothing when the view is the whole image. */
const viewStyle = computed(() => {
  const source = image.value
  const area = view.value
  if (!source || !area || (area.width === source.width && area.height === source.height))
    return undefined
  return {
    left: `${-area.left / area.width * 100}%`,
    top: `${-area.top / area.height * 100}%`,
    width: `${source.width / area.width * 100}%`,
    height: `${source.height / area.height * 100}%`,
  }
})

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

/** The result has the original's size, so the two can share the stage (not on crop, where the frame stays on top). */
const compare = computed(() => !!resultSize.value && !!image.value && settings.tool !== 'crop'
  && resultSize.value.width === image.value.width && resultSize.value.height === image.value.height)

/** What the frame measures: the result when it has a size of its own, else the part of the image in view. */
const shown = computed(() => (!compare.value && resultSize.value) || view.value)

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

// Drawn over the view, before a run and over the result after one.
const cropBox = computed(() => {
  const source = image.value
  const area = view.value
  if (!source || !area || settings.tool !== 'crop' || plan.value.errors.rect || !rectFits(rect, source))
    return null
  return {
    style: {
      left: `${(rect.left - area.left) / area.width * 100}%`,
      top: `${(rect.top - area.top) / area.height * 100}%`,
      width: `${rect.width / area.width * 100}%`,
      height: `${rect.height / area.height * 100}%`,
    },
    label: `${formatSize(rect.width, rect.height)} from ${rect.left}, ${rect.top}`,
  }
})

// The crop frame: drag inside to move it, or an edge or corner to resize it. The panel's fields edit the same
// rect, so they follow along (and are the keyboard way in).
const cropHandles: CropHandle[] = ['n', 'e', 's', 'w', 'nw', 'ne', 'sw', 'se']

// The view is fixed for the whole drag, so the frame's scale and limits don't shift under the pointer.
let cropDrag: {
  handle: CropHandle
  x: number
  y: number
  start: Rect
  bounds: Rect
  scaleX: number
  scaleY: number
} | null = null

function startCrop(event: PointerEvent, handle: CropHandle) {
  const frame = (event.currentTarget as HTMLElement).closest<HTMLElement>('.frame')
  const area = view.value
  if (event.button !== 0 || !area || !frame)
    return
  const bounds = frame.getBoundingClientRect()
  cropDrag = {
    handle,
    x: event.clientX,
    y: event.clientY,
    start: { ...rect },
    bounds: { ...area },
    scaleX: area.width / bounds.width,
    scaleY: area.height / bounds.height,
  }
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function dragCrop(event: PointerEvent) {
  if (!cropDrag)
    return
  const dx = (event.clientX - cropDrag.x) * cropDrag.scaleX
  const dy = (event.clientY - cropDrag.y) * cropDrag.scaleY
  Object.assign(rect, dragRect(cropDrag.start, cropDrag.handle, dx, dy, cropDrag.bounds))
}

function stopCrop() {
  cropDrag = null
}

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
    <div v-if="opening?.fromUrl" class="notice" role="status">
      <p>Downloading “{{ opening.name }}”…</p>
      <button class="button" type="button" @click="cancelOpen">Cancel</button>
    </div>

    <div v-else-if="openError" class="notice" role="alert">
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
          <img v-if="!result || compare" :src="image.url" :alt="`${image.name}, original`" :style="viewStyle">
          <img
            v-if="result && undisplayable !== result.id"
            class="result"
            :src="result.url"
            :alt="`${image.name}, ${result.label}`"
            @error="undisplayable = result.id"
          >
          <p v-if="result && undisplayable === result.id" class="no-preview">
            {{ result.format === 'jxl' ? "Your browser can't display JPEG XL." : "Your browser couldn't display the result." }}
            Download it to view it.
          </p>

          <span v-if="result && compare" class="tag a">Original<span>{{ formatBytes(image.size) }}</span></span>
          <span v-if="result" class="tag b">{{ result.label }}<span>{{ formatBytes(result.stats.size) }}</span></span>

          <div v-if="cropBox" class="crop-shade" :style="cropBox.style" />
        </div>

        <!-- Outside .pic, which clips the shade, so handles on the image's edges stay whole and grabbable. -->
        <div
          v-if="cropBox"
          class="crop-box"
          :style="cropBox.style"
          @pointerdown="startCrop($event, 'move')"
          @pointermove="dragCrop"
          @pointerup="stopCrop"
          @pointercancel="stopCrop"
        >
          <i
            v-for="handle in cropHandles"
            :key="handle"
            :class="handle"
            aria-hidden="true"
            @pointerdown.stop="startCrop($event, handle)"
          />
          <b>{{ cropBox.label }}</b>
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
          v-if="result && compare && undisplayable !== result.id"
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

.no-preview {
  position: absolute;
  top: 50%;
  right: 16px;
  left: 16px;
  width: fit-content;
  margin: 0 auto;
  translate: 0 -50%;
  padding: 8px 12px;
  border-radius: 3px;
  background: rgb(0 0 0 / 0.66);
  color: #fff;
  font: 500 14px var(--font-cond);
  text-align: center;
  pointer-events: none;
}

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

// Dims everything outside the area; .pic clips it to the image.
.crop-shade {
  position: absolute;
  box-shadow: 0 0 0 9999px rgb(0 0 0 / 0.55);
  pointer-events: none;
}

.crop-box {
  position: absolute;
  outline: 2px solid var(--flag);
  cursor: move;

  // Grab areas wider than what's drawn: 12px strips along the edges, 22px squares at the corners (drawn 12px).
  i {
    position: absolute;
    --grab: 12px;

    &.n, &.s { left: 11px; right: 11px; height: var(--grab); cursor: ns-resize; }
    &.e, &.w { top: 11px; bottom: 11px; width: var(--grab); cursor: ew-resize; }
    &.n { top: calc(var(--grab) / -2); }
    &.s { bottom: calc(var(--grab) / -2); }
    &.e { right: calc(var(--grab) / -2); }
    &.w { left: calc(var(--grab) / -2); }

    &.nw, &.ne, &.sw, &.se {
      width: 22px;
      height: 22px;

      &::after {
        content: "";
        position: absolute;
        inset: 5px;
        background: var(--flag);
      }
    }
    &.nw { left: -11px; top: -11px; cursor: nwse-resize; }
    &.se { right: -11px; bottom: -11px; cursor: nwse-resize; }
    &.ne { right: -11px; top: -11px; cursor: nesw-resize; }
    &.sw { left: -11px; bottom: -11px; cursor: nesw-resize; }
  }

  b {
    pointer-events: none;
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
