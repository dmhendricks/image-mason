<script setup lang="ts">
import { ref, watch } from 'vue'

// A text field for whole (or, with decimal, fractional) numbers. Empty is null, and anything that isn't a number
// is NaN, so the caller can say what's wrong instead of the browser silently dropping it (as type="number" does).
// With unit, the field gets a suffix such as "px"; attributes (id, placeholder, aria-*) always go to the <input>.

defineOptions({ inheritAttrs: false })

const model = defineModel<number | null>({ required: true })
const props = defineProps<{ decimal?: boolean, unit?: string }>()

const show = (value: number | null) => value === null || Number.isNaN(value) ? '' : String(value)

function parse(text: string): number | null {
  const trimmed = text.trim()
  if (!trimmed)
    return null
  return (props.decimal ? /^\d+(\.\d+)?$/ : /^\d+$/).test(trimmed) ? Number(trimmed) : NaN
}

const text = ref(show(model.value))

// Changes from elsewhere (reset, a shape preset) replace the text; the user's own typing is left alone.
watch(model, value => {
  if (!Object.is(parse(text.value), value))
    text.value = show(value)
})

function onInput(event: Event) {
  text.value = (event.target as HTMLInputElement).value
  model.value = parse(text.value)
}
</script>

<template>
  <div v-if="unit" class="field-group">
    <input
      v-bind="$attrs"
      class="field"
      type="text"
      :value="text"
      :inputmode="decimal ? 'decimal' : 'numeric'"
      autocomplete="off"
      spellcheck="false"
      @input="onInput"
    >
    <span class="unit">{{ unit }}</span>
  </div>
  <input
    v-else
    v-bind="$attrs"
    class="field"
    type="text"
    :value="text"
    :inputmode="decimal ? 'decimal' : 'numeric'"
    autocomplete="off"
    spellcheck="false"
    @input="onInput"
  >
</template>
