<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useApiKey } from '../../composables/useApiKey'
import { useImage } from '../../composables/useImage'
import { usePlan } from '../../composables/usePlan'
import { useResults } from '../../composables/useResults'
import { useServer } from '../../composables/useServer'
import { formatBytes, formatChange, formatName, formatSize } from '../../format'
import { jobAnnouncement, jobStatus, resultAnnouncement } from '../../jobs'

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

// What the run will make: "Result: WebP, 200 × 200, cropped".
const expected = computed(() => {
  const { output, format, cropped } = plan.value
  if (!output || !format)
    return null
  return { format: formatName(format), size: formatSize(output.width, output.height), cropped }
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

// Run and Cancel replace each other, so focus would be lost with the one that goes. When it was in the panel, it
// moves to the one that appears: Cancel while the job runs, then Run (or the panel, while Run is disabled).
const panel = ref<HTMLElement | null>(null)
const runButton = ref<HTMLButtonElement | null>(null)
const cancelButton = ref<HTMLButtonElement | null>(null)

// For screen readers, one region that stays on the page: each phase once (not the ticking status line), then the
// result, or that the job was cancelled. Errors announce themselves (role="alert").
const announcement = ref('')
let resultBefore: number | undefined

watch(() => job.value && jobAnnouncement(job.value), text => {
  if (text)
    announcement.value = text
})

watch(() => job.value !== null, async running => {
  const active = document.activeElement
  const hadFocus = !active || active === document.body || !!panel.value?.contains(active)
  if (running)
    resultBefore = result.value?.id

  await nextTick()
  if (hadFocus) {
    const target = running ? cancelButton.value : runButton.value
    if (target && !target.disabled)
      target.focus()
    else
      panel.value?.focus()
  }

  if (running)
    return
  if (error.value)
    announcement.value = ''
  else if (result.value && result.value.id !== resultBefore)
    announcement.value = resultAnnouncement(result.value.label, result.value.stats.originalSize, result.value.stats.size)
  else
    announcement.value = 'Cancelled.'
})
</script>

<template>
  <div ref="panel" class="run-panel" tabindex="-1">
    <p class="visually-hidden" role="status">{{ announcement }}</p>

    <template v-if="job">
      <div
        class="progress"
        :class="{ busy: job.phase !== 'uploading' }"
        role="progressbar"
        :aria-valuenow="job.phase === 'uploading' ? Math.round(job.progress * 100) : undefined"
        aria-valuemin="0"
        aria-valuemax="100"
        aria-label="Transforming"
      >
        <i :style="{ width: job.phase === 'uploading' ? `${job.progress * 100}%` : undefined }" />
      </div>
      <p class="status">{{ status }}</p>
      <button ref="cancelButton" class="button wide" type="button" @click="cancel">Cancel</button>
    </template>

    <template v-else>
      <div v-if="result" class="figures">
        <span class="from">{{ formatBytes(result.stats.originalSize) }} to</span>
        <span class="to">{{ formatBytes(shownSize) }}</span>
        <span class="change" :class="{ larger: result.stats.size > result.stats.originalSize }">
          {{ formatChange(result.stats.originalSize, result.stats.size) }}
        </span>
      </div>
      <p v-else-if="expected" class="expected">
        Result: <b>{{ expected.format }}</b>, <b>{{ expected.size }}</b>{{ expected.cropped ? ', cropped' : '' }}
      </p>

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
        <button ref="runButton" class="button primary" type="button" :disabled="!canRun" @click="start">
          {{ image ? 'Transform' : 'Open an image first' }}
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

  // Only focused by script, when the button that had focus goes away.
  &:focus { outline: none; }
}

// Read by screen readers, not shown.
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
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

.expected b { color: var(--ink); font-weight: 500; }

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
