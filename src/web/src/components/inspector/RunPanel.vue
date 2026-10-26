<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useApiKey } from '../../composables/useApiKey'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useResults } from '../../composables/useResults'
import { useServer } from '../../composables/useServer'
import { formatBytes, formatChange, formatName, formatSize } from '../../format'
import { jobStatus } from '../../jobs'

const emit = defineEmits<{ enterKey: [] }>()

const { image, job, error, run, cancel, dismissError } = useImage()
const { markDownloaded } = useResults()
const { plan, result } = usePlan()
const { limits } = useServer()
const apiKey = useApiKey()

// A clock for the elapsed time and the retry countdown, ticking only while a job runs.
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | undefined
watch(() => job.value !== null, running => {
  clearInterval(timer)
  if (running) {
    now.value = Date.now()
    timer = setInterval(() => { now.value = Date.now() }, 250)
  }
}, { immediate: true })
onBeforeUnmount(() => clearInterval(timer))

// A new key answers a 401, so its message can go.
watch(apiKey.key, key => {
  if (key && error.value?.status === 401)
    dismissError()
})

const problem = computed(() => Object.values(plan.value.errors)[0] ?? null)
const canRun = computed(() => image.value !== null && job.value === null && problem.value === null)

const status = computed(() => job.value ? jobStatus(job.value, now.value) : '')

const expected = computed(() => {
  const output = plan.value.output
  if (!output)
    return null
  const format = plan.value.format ? `, ${formatName(plan.value.format)}` : ''
  return `${formatSize(output.width, output.height)}${format}`
})

// The new size counts down from the original's when a result arrives: the one bit of motion that says "smaller".
const shownSize = ref(0)
let frame = 0
watch(() => result.value?.id, () => {
  cancelAnimationFrame(frame)
  const stats = result.value?.stats
  if (!stats)
    return
  const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  if (reduced) {
    shownSize.value = stats.size
    return
  }
  const start = performance.now()
  const step = (time: number) => {
    const t = Math.min(1, (time - start) / 600)
    const eased = 1 - (1 - t) ** 3
    shownSize.value = Math.round(stats.originalSize + (stats.size - stats.originalSize) * eased)
    if (t < 1)
      frame = requestAnimationFrame(step)
  }
  frame = requestAnimationFrame(step)
}, { immediate: true })
onBeforeUnmount(() => cancelAnimationFrame(frame))

function start() {
  if (limits.value?.apiKeyRequired && !apiKey.key.value) {
    emit('enterKey')
    return
  }
  dismissError()
  void run(plan.value)
}
</script>

<template>
  <div class="run-panel">
    <template v-if="job">
      <div
        class="progress"
        :class="{ busy: job.phase !== 'uploading' }"
        role="progressbar"
        :aria-valuenow="job.phase === 'uploading' ? Math.round(job.progress * 100) : undefined"
        aria-valuemin="0"
        aria-valuemax="100"
        :aria-label="job.working"
      >
        <i :style="{ width: job.phase === 'uploading' ? `${job.progress * 100}%` : undefined }" />
      </div>
      <p class="status" aria-live="polite">{{ status }}</p>
      <button class="button wide" type="button" @click="cancel">Cancel</button>
    </template>

    <template v-else>
      <div v-if="result" class="figures">
        <span class="from">{{ formatBytes(result.stats.originalSize) }} to</span>
        <span class="to">{{ formatBytes(shownSize) }}</span>
        <span class="change" :class="{ larger: result.stats.size > result.stats.originalSize }">
          {{ formatChange(result.stats.originalSize, result.stats.size) }}
        </span>
      </div>
      <p v-else-if="expected" class="expected">Result: {{ expected }}</p>

      <div v-if="error" class="alert" role="alert">
        <p>{{ error.message }}</p>
        <div class="alert-actions">
          <button v-if="error.status === 401" class="button" type="button" @click="emit('enterKey')">Enter API key</button>
          <button v-else-if="error.canRetry" class="button" type="button" :disabled="!canRun" @click="start">Try again</button>
          <button class="button" type="button" @click="dismissError">Dismiss</button>
        </div>
      </div>
      <p v-else-if="problem && image" class="blocked">{{ problem }}</p>

      <div class="actions">
        <a
          v-if="result"
          class="button"
          :href="result.url"
          :download="result.fileName"
          @click="markDownloaded(result.id)"
        >
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v12M7 11l5 5 5-5M4 20h16" /></svg>
          Download
        </a>
        <button class="button primary" type="button" :disabled="!canRun" @click="start">
          {{ image ? plan.action : 'Open an image first' }}
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped lang="scss">
.run-panel {
  display: grid;
  gap: 10px;
  padding: 12px 16px 16px;
  border-top: 1px solid var(--rule);
  background: var(--panel);
}

.figures {
  display: flex;
  align-items: baseline;
  gap: 10px;
  font-family: var(--font-cond);
  font-variant-numeric: tabular-nums;
}

.from { color: var(--muted); font-weight: 500; }
.to { font-size: 28px; font-weight: 700; line-height: 1; }

.change {
  margin-left: auto;
  color: var(--flag);
  font-size: 20px;
  font-weight: 700;

  &.larger { color: var(--muted); }
}

.expected, .blocked, .status {
  margin: 0;
  color: var(--muted);
  font-variant-numeric: tabular-nums;
}

.blocked {
  padding-left: 8px;
  border-left: 2px solid var(--flag);
  color: var(--ink);
  font-size: 13px;
}

.actions {
  display: flex;
  gap: 8px;

  .button { height: 40px; }
  .primary { flex: 1; }
}

.wide { width: 100%; height: 40px; }

.alert-actions {
  display: flex;
  gap: 6px;
}

.progress {
  height: 6px;
  border-radius: 3px;
  background: var(--panel-2);
  overflow: hidden;

  i {
    display: block;
    height: 100%;
    background: var(--flag);
    transition: width 0.2s linear;
  }

  // Working on the server: no fraction to show, so a band sweeps across.
  &.busy i {
    width: 35%;
    animation: sweep 1.2s ease-in-out infinite;
  }
}

@keyframes sweep {
  from { translate: -100% 0; }
  to { translate: 290% 0; }
}
</style>
