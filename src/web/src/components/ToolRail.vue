<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useSettings } from '../composables/useSettings'
import type { Tool } from '../settings'

// The tools, as tabs over the inspector. Arrow keys move between them (roving tabindex).

const { settings } = useSettings()

const items: { tool: Tool, label: string, icon: string }[] = [
  { tool: 'optimize', label: 'Optimize', icon: 'M4 9h5V4M20 9h-5V4M4 15h5v5M20 15h-5v5' },
  { tool: 'resize', label: 'Resize', icon: 'M14 4h6v6M10 20H4v-6M20 4l-7 7M4 20l7-7' },
  { tool: 'crop', label: 'Crop', icon: 'M6 2v16h16M2 6h16v16' },
  { tool: 'convert', label: 'Convert', icon: 'M4 7h14l-3-3M20 17H6l3 3' },
]

const buttons = ref<HTMLButtonElement[]>([])

function onKey(event: KeyboardEvent, index: number) {
  const steps: Record<string, number> = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }
  let next: number
  if (event.key in steps)
    next = (index + steps[event.key]! + items.length) % items.length
  else if (event.key === 'Home' || event.key === 'End')
    next = event.key === 'Home' ? 0 : items.length - 1
  else
    return
  event.preventDefault()
  settings.tool = items[next]!.tool
  void nextTick(() => buttons.value[next]?.focus())
}
</script>

<template>
  <nav class="rail" role="tablist" aria-label="Tools" aria-orientation="vertical">
    <button
      v-for="(item, index) in items"
      :id="`tab-${item.tool}`"
      :key="item.tool"
      ref="buttons"
      type="button"
      role="tab"
      aria-controls="tool-panel"
      :aria-selected="settings.tool === item.tool"
      :tabindex="settings.tool === item.tool ? 0 : -1"
      @click="settings.tool = item.tool"
      @keydown="onKey($event, index)"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true"><path :d="item.icon" /></svg>
      {{ item.label }}
    </button>
  </nav>
</template>

<style scoped lang="scss">
.rail {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 6px;
  background: var(--panel);
  border-right: 1px solid var(--rule);

  button {
    position: relative;
    display: grid;
    justify-items: center;
    gap: 4px;
    padding: 10px 2px 8px;
    border: 0;
    border-radius: 6px;
    background: none;
    color: var(--muted);
    font: 600 13px var(--font-cond);
    cursor: pointer;

    &:hover { color: var(--ink); background: var(--panel-2); }

    &[aria-selected="true"] {
      color: var(--ink);
      background: var(--panel-2);

      &::before {
        content: "";
        position: absolute;
        left: -6px;
        top: 8px;
        bottom: 8px;
        width: 3px;
        border-radius: 0 2px 2px 0;
        background: var(--flag);
      }
    }
  }

  svg {
    width: 22px;
    height: 22px;
    fill: none;
    stroke: currentColor;
    stroke-width: 1.7;
    stroke-linecap: round;
    stroke-linejoin: round;
  }
}

@media (max-width: 900px) {
  .rail {
    flex-direction: row;
    overflow-x: auto;
    border-right: 0;
    border-bottom: 1px solid var(--rule);

    button {
      flex: 1;
      min-width: 64px;

      &[aria-selected="true"]::before {
        left: 8px;
        right: 8px;
        top: auto;
        bottom: -8px;
        width: auto;
        height: 3px;
      }
    }
  }
}
</style>
