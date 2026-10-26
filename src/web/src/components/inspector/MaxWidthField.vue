<script setup lang="ts">
import { computed } from 'vue'
import { usePlan } from '../../composables/usePlan'
import { useServer } from '../../composables/useServer'
import { useSettings } from '../../composables/useSettings'
import NumberField from '../controls/NumberField.vue'

// maxWidth: shrinks wider images, never enlarges. Empty uses the server's default (IMAGE_MASON_DEFAULT_MAX_WIDTH),
// which lossless skips.

defineProps<{ id: string }>()

const { settings } = useSettings()
const { limits } = useServer()
const { plan } = usePlan()

const serverDefault = computed(() =>
  settings.lossless ? 0 : limits.value?.defaultMaxWidth ?? 0)
</script>

<template>
  <div class="prop">
    <label :for="id">Max width</label>
    <NumberField
      :id="id"
      v-model="settings.maxWidth"
      unit="px"
      :placeholder="serverDefault > 0 ? `${serverDefault} (server default)` : 'No limit'"
      :aria-invalid="plan.errors.maxWidth ? 'true' : undefined"
      :aria-describedby="`${id}-hint`"
    />
  </div>
  <p v-if="plan.errors.maxWidth" class="field-error">{{ plan.errors.maxWidth }}</p>
  <p v-else :id="`${id}-hint`" class="hint">
    Wider images shrink to fit; smaller ones keep their size.<template v-if="serverDefault > 0"> 0 keeps the full width.</template>
  </p>
</template>
