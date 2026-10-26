import { computed, ref } from 'vue'
import type { ResultStats } from '../api/types'
import type { ImageFormat } from '../settings'

// The results made from the current image, in the order they were made. Each holds its file as an object URL, revoked
// when the result is replaced or the image is (useImage clears them).

export interface Result {
  id: number
  /** The request path and query: the same settings give the same path. */
  path: string
  label: string
  blob: Blob
  url: string
  format: ImageFormat
  fileName: string
  stats: ResultStats
  downloaded: boolean
}

const results = ref<Result[]>([])
let nextId = 1

export function useResults() {
  const hasUnsavedResults = computed(() => results.value.some(result => !result.downloaded))

  /**
   * Adds a new result. The same settings give the same path, and only the newest result for a path is shown, so it
   * replaces the older one rather than leaving a hidden result that counts as not downloaded.
   */
  function add(made: Omit<Result, 'id' | 'downloaded'>) {
    const result: Result = { id: nextId++, ...made, downloaded: false }
    const index = results.value.findIndex(r => r.path === result.path)
    if (index === -1) {
      results.value.push(result)
    } else {
      URL.revokeObjectURL(results.value[index]!.url)
      results.value[index] = result
    }
  }

  function markDownloaded(id: number) {
    const result = results.value.find(r => r.id === id)
    if (result)
      result.downloaded = true
  }

  /** Revokes every result's object URL and forgets them. */
  function clear() {
    for (const result of results.value)
      URL.revokeObjectURL(result.url)
    results.value = []
  }

  return {
    results,
    hasUnsavedResults,
    add,
    markDownloaded,
    clear,
  }
}
