import { type ComputedRef, computed } from 'vue'
import { type JobPlan, planJob } from '../jobs'
import { useImage } from './useImage'
import { type Result, useResults } from './useResults'
import { useServer } from './useServer'
import { useSettings } from './useSettings'

// The request the settings make, and the result made with exactly those settings (the canvas shows it until a setting
// changes; running them again replaces it). Shared, so every component reads the same plan.

let shared: { plan: ComputedRef<JobPlan>, result: ComputedRef<Result | null> } | null = null

export function usePlan() {
  if (shared)
    return shared

  const { settings } = useSettings()
  const { image, rect } = useImage()
  const { results } = useResults()
  const { limits } = useServer()

  const plan = computed(() => planJob(settings, image.value, rect, limits.value))
  const result = computed(() => results.value.find(r => r.path === plan.value.path) ?? null)
  shared = { plan, result }
  return shared
}
