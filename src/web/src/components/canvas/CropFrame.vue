<script setup lang="ts">
import { computed, ref } from 'vue'
import { useImage } from '../../composables/useImage'
import { formatSize } from '../../format'
import { type CropHandle, type Rect, dragRect } from '../../geometry'

// The crop frame: drag inside to move it, or an edge or corner to resize it. The panel's fields edit the same rect,
// so they follow along (and are the keyboard way in). The parent positions it over the board's frame.

const props = defineProps<{
  /** The part of the image the board's frame shows, in image pixels; the area stays inside it. */
  view: Rect
}>()

const { rect } = useImage()
const box = ref<HTMLElement | null>(null)

const handles: CropHandle[] = ['n', 'e', 's', 'w', 'nw', 'ne', 'sw', 'se']

const label = computed(() => `${formatSize(rect.width, rect.height)} from ${rect.left}, ${rect.top}`)

// The view is fixed for the whole drag, so the frame's scale and limits don't shift under the pointer.
let drag: {
  handle: CropHandle
  x: number
  y: number
  start: Rect
  bounds: Rect
  scaleX: number
  scaleY: number
} | null = null

function start(event: PointerEvent, handle: CropHandle) {
  // The board's frame it's drawn in shows the view, so the frame's size on screen sets the drag's scale.
  const frame = box.value?.parentElement
  if (event.button !== 0 || !frame)
    return
  const bounds = frame.getBoundingClientRect()
  drag = {
    handle,
    x: event.clientX,
    y: event.clientY,
    start: { ...rect },
    bounds: { ...props.view },
    scaleX: props.view.width / bounds.width,
    scaleY: props.view.height / bounds.height,
  }
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function move(event: PointerEvent) {
  if (!drag)
    return
  const dx = (event.clientX - drag.x) * drag.scaleX
  const dy = (event.clientY - drag.y) * drag.scaleY
  Object.assign(rect, dragRect(drag.start, drag.handle, dx, dy, drag.bounds))
}

function stop() {
  drag = null
}
</script>

<template>
  <div
    ref="box"
    class="crop-box"
    @pointerdown="start($event, 'move')"
    @pointermove="move"
    @pointerup="stop"
    @pointercancel="stop"
  >
    <i
      v-for="handle in handles"
      :key="handle"
      :class="handle"
      aria-hidden="true"
      @pointerdown.stop="start($event, handle)"
    />
    <b class="flag-label">{{ label }}</b>
  </div>
</template>

<style scoped lang="scss">
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
    font-variant-numeric: tabular-nums;
  }
}
</style>
