<script setup lang="ts">
// A drafting dimension line: ticks at both ends and the measurement centered on the line. The parent positions it.

defineProps<{
  orientation: 'horizontal' | 'vertical'
  label: string
  /** Pink: a measured output rather than the image itself. */
  flag?: boolean
}>()
</script>

<template>
  <div class="dim" :class="[orientation, { flag }]" aria-hidden="true">
    <span>{{ label }}</span>
  </div>
</template>

<style scoped lang="scss">
.dim {
  position: absolute;
  color: var(--muted);
  font: 600 12px var(--font-cond);
  font-variant-numeric: tabular-nums;
  pointer-events: none;

  &::before {
    content: "";
    position: absolute;
    background: currentColor;
  }

  span {
    position: absolute;
    padding: 0 6px;
    background: var(--canvas);
    white-space: nowrap;
  }

  &.flag { color: var(--flag); }
}

.horizontal {
  left: 0;
  right: 0;
  height: 11px;
  border-inline: 1px solid currentColor;

  &::before { left: 0; right: 0; top: 5px; height: 1px; }
  span { left: 50%; top: 50%; translate: -50% -50%; }
}

.vertical {
  top: 0;
  bottom: 0;
  width: 11px;
  border-block: 1px solid currentColor;

  &::before { top: 0; bottom: 0; left: 5px; width: 1px; }
  span { top: 50%; left: 50%; translate: -50% -50%; rotate: -90deg; }
}
</style>
