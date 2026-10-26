<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

// The divider between the original (left) and the result: drag anywhere on the frame it's drawn in, or focus it and
// use the arrow keys. The model is its position, 0 to 100% of the frame from the left; the parent sets --split from
// it, which places the divider and clips the result.

const split = defineModel<number>({ required: true })

defineProps<{
  /** The change in size, shown at the divider's foot. */
  label: string
}>()

const divider = ref<HTMLElement | null>(null)
let frame: HTMLElement | null = null
let sliding = false

function splitAt(event: PointerEvent) {
  const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect()
  split.value = Math.min(100, Math.max(0, (event.clientX - bounds.left) / bounds.width * 100))
}

function onPointerDown(event: PointerEvent) {
  if (event.button !== 0)
    return
  sliding = true
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  splitAt(event)
}

function onPointerMove(event: PointerEvent) {
  if (sliding)
    splitAt(event)
}

function stopSliding() {
  sliding = false
}

// The whole frame is the drag area, not just the line.
onMounted(() => {
  frame = divider.value!.parentElement!
  frame.addEventListener('pointerdown', onPointerDown)
  frame.addEventListener('pointermove', onPointerMove)
  frame.addEventListener('pointerup', stopSliding)
  frame.addEventListener('pointercancel', stopSliding)
})

onBeforeUnmount(() => {
  frame?.removeEventListener('pointerdown', onPointerDown)
  frame?.removeEventListener('pointermove', onPointerMove)
  frame?.removeEventListener('pointerup', stopSliding)
  frame?.removeEventListener('pointercancel', stopSliding)
})

function onKey(event: KeyboardEvent) {
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
  <div
    ref="divider"
    class="divider"
    role="slider"
    tabindex="0"
    aria-label="Divider between the original and the result"
    aria-valuemin="0"
    aria-valuemax="100"
    :aria-valuenow="Math.round(split)"
    :aria-valuetext="`${Math.round(split)}% original`"
    @keydown="onKey"
  >
    <span class="flag-label">{{ label }}</span>
  </div>
</template>

<style scoped lang="scss">
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
  }
}
</style>
