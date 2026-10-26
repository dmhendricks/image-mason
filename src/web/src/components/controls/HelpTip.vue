<script setup lang="ts">
import { ref, useId } from 'vue'

// A (?) button that opens a short explanation. The note is a native popover (Esc and clicking elsewhere close it),
// teleported to <body> so it can sit beside a checkbox without clicks on it reaching the <label>.

defineProps<{
  /** The button's accessible name, phrased as the question it answers. */
  label: string
}>()

const id = `help-${useId()}`
const button = ref<HTMLButtonElement | null>(null)
const note = ref<HTMLElement | null>(null)

const gap = 6
const margin = 8

// Hidden until placed: toggle fires after the popover opens, so it would otherwise flash at its default spot.
function hide(event: Event) {
  if ((event as ToggleEvent).newState === 'open' && note.value)
    note.value.style.visibility = 'hidden'
}

// Below the button, or above it when there's no room, kept inside the viewport.
function place(event: Event) {
  if ((event as ToggleEvent).newState !== 'open' || !button.value || !note.value)
    return
  const anchor = button.value.getBoundingClientRect()
  const { width, height } = note.value.getBoundingClientRect()
  const below = anchor.bottom + gap
  const top = below + height + margin <= window.innerHeight ? below : Math.max(margin, anchor.top - gap - height)
  const left = Math.min(Math.max(margin, anchor.left + anchor.width / 2 - width / 2), window.innerWidth - width - margin)
  note.value.style.top = `${top}px`
  note.value.style.left = `${left}px`
  note.value.style.visibility = ''
}
</script>

<template>
  <button ref="button" class="help-tip" type="button" :aria-label="label" :popovertarget="id">
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6v.6M12 17h.01" />
    </svg>
  </button>
  <Teleport to="body">
    <div :id="id" ref="note" class="help-note" popover role="note" @beforetoggle="hide" @toggle="place">
      <slot />
    </div>
  </Teleport>
</template>

<style scoped lang="scss">
.help-tip {
  display: inline-grid;
  place-items: center;
  width: 18px;
  height: 18px;
  margin-left: 2px;
  padding: 0;
  border: 0;
  border-radius: 50%;
  background: none;
  color: var(--muted);
  vertical-align: -4px;
  cursor: pointer;

  &:hover { color: var(--ink); }

  svg {
    width: 15px;
    height: 15px;
    fill: none;
    stroke: currentColor;
    stroke-width: 2;
    stroke-linecap: round;
    stroke-linejoin: round;
  }
}

.help-note {
  position: fixed;
  inset: auto;
  width: min(300px, calc(100vw - 16px));
  margin: 0;
  padding: 10px 12px;
  border: 1px solid var(--rule);
  border-radius: var(--radius);
  // A step lighter than the panel it covers in dark mode; light mode keeps white, set apart by the shadow.
  background: light-dark(var(--panel), var(--panel-2));
  color: var(--ink);
  font-size: 13px;
  line-height: 1.45;
  box-shadow: 0 8px 24px rgb(0 0 0 / 0.2);

  :deep(p) { margin: 0; }
  :deep(p + p), :deep(p + ul), :deep(ul + p) { margin-top: 6px; }
  :deep(ul) { margin: 6px 0 0; padding-left: 18px; }
  :deep(li + li) { margin-top: 3px; }
}
</style>
