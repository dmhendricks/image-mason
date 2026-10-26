import { computed, readonly, ref } from 'vue'
import { ApiError, getJson } from '../api/client'
import type { Health, Limits } from '../api/types'

// What the server accepts (GET /api/v1/limits) and its version (GET /health). Both are public. Loaded once and
// shared by every component.

const limits = ref<Limits | null>(null)
const version = ref<string | null>(null)
const error = ref<string | null>(null)
const loading = ref(false)
let started = false

async function load() {
  loading.value = true
  error.value = null
  const [limitsResult, healthResult] = await Promise.allSettled([
    getJson<Limits>('/api/v1/limits'),
    getJson<Health>('/health'),
  ])
  loading.value = false

  if (limitsResult.status === 'fulfilled')
    limits.value = limitsResult.value
  else
    error.value = limitsResult.reason instanceof ApiError ? limitsResult.reason.message : 'Could not read the server\'s limits.'

  // The version is a nicety: leave it out rather than fail.
  if (healthResult.status === 'fulfilled')
    version.value = healthResult.value.build.version
}

export function useServer() {
  if (!started) {
    started = true
    void load()
  }
  return {
    limits: computed(() => limits.value),
    version: readonly(version),
    error: readonly(error),
    loading: readonly(loading),
    reload: load,
  }
}
