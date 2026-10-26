<script setup lang="ts" generic="T extends string">
// A row of mutually exclusive buttons (aria-pressed), bound with v-model.

const model = defineModel<T>({ required: true })

defineProps<{
  options: { value: T, label: string, title?: string }[]
  label: string
  disabled?: boolean
}>()
</script>

<template>
  <div class="seg" role="group" :aria-label="label">
    <button
      v-for="option in options"
      :key="option.value"
      type="button"
      :aria-pressed="model === option.value"
      :disabled="disabled"
      :title="option.title"
      @click="model = option.value"
    >
      {{ option.label }}
    </button>
  </div>
</template>
