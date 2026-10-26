import { type ComputedRef, computed } from 'vue'
import { type JobPlan, planJob } from '../jobs'
import { type Result, useImage } from './useImage'
import { useServer } from './useServer'
import { useSettings } from './useSettings'

// The request the current tool and settings make, and the result made with exactly those settings (the canvas shows
// it until a setting changes; running them again replaces it). Shared, so every component reads the same plan.

let shared: { plan: ComputedRef<JobPlan>, result: ComputedRef<Result | null> } | null = null

export function usePlan() {
  if (shared)
    return shared

  const { settings } = useSettings()
  const { image, results, rect } = useImage()
  const { limits } = useServer()

  const plan = computed(() => planJob(settings.tool, settings, image.value, rect, limits.value))
  const result = computed(() => results.value.find(r => r.path === plan.value.path) ?? null)
  shared = { plan, result }
  return shared
}
