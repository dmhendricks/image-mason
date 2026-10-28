<script setup lang="ts">
import { usePlan } from '../../composables/usePlan'
import { useSettings } from '../../composables/useSettings'
import NumberField from '../controls/NumberField.vue'

// Sent as width: shrinks wider images, never enlarges. Empty keeps the size.

defineProps<{ id: string }>()

const { settings } = useSettings()
const { plan } = usePlan()
</script>

<template>
  <div class="prop">
    <label :for="id">Max width</label>
    <NumberField
      :id="id"
      v-model="settings.maxWidth"
      unit="px"
      placeholder="No limit"
      :aria-invalid="plan.errors.maxWidth ? 'true' : undefined"
      :aria-describedby="`${id}-note`"
    />
  </div>
  <!-- The error or the hint, whichever shows, describes the field. -->
  <p v-if="plan.errors.maxWidth" :id="`${id}-note`" class="field-error">{{ plan.errors.maxWidth }}</p>
  <p v-else :id="`${id}-note`" class="hint">
    Wider images shrink to fit; smaller ones keep their size.
  </p>
</template>
