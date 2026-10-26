import { computed, reactive, readonly, ref, shallowRef } from 'vue'
import { ApiError, postForBlob, postImage } from '../api/client'
import { type ResultStats, readStats } from '../api/types'
import { fileNameFromUrl, formatFromContentType, isHeif, resultFileName, sniffFormat } from '../files'
import { formatBytes, formatMegapixels, formatSize } from '../format'
import { type Rect, wholeImage } from '../geometry'
import type { JobPlan, SourceImage } from '../jobs'
import type { ImageFormat } from '../settings'
import { useApiKey } from './useApiKey'
import { useServer } from './useServer'

// The current image, the results made from it, and the request running now. One image at a time (docs/WEB-UI.md
// W8); a queue of images could later hold one of these per file.

export interface OpenedImage extends SourceImage {
  name: string
  blob: Blob
  size: number
  /** Object URL for <img>; revoked when the image is replaced. */
  url: string
}

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

export interface Job {
  /** "Optimizing", "Resizing". */
  working: string
  phase: 'uploading' | 'processing' | 'waiting'
  /** Fraction of the image uploaded, 0 to 1. */
  progress: number
  /** When the current phase started (Date.now()). */
  since: number
  /** While waiting: when the next attempt starts. */
  retryAt: number | null
  attempt: number
}

export interface JobError {
  message: string
  /** The HTTP status; 0 when the server couldn't be reached. */
  status: number
  /** Whether trying the same request again might work. */
  canRetry: boolean
}

/** Tries for a busy server (503) before giving up. */
export const maxAttempts = 3

const image = shallowRef<OpenedImage | null>(null)
const results = ref<Result[]>([])
const job = ref<Job | null>(null)
const error = ref<JobError | null>(null)
const openError = ref<string | null>(null)
const opening = ref(false)
/** Crop's area, in display pixels; NaN while a field is empty or not a number. */
const rect = reactive<Rect>({ left: 0, top: 0, width: 0, height: 0 })

let controller: AbortController | null = null
let nextId = 1

export function useImage() {
  const apiKey = useApiKey()
  const { limits } = useServer()

  const hasUnsavedResults = computed(() => results.value.some(result => !result.downloaded))

  /** Checks a file and makes it the current image, replacing the last one and its results. */
  async function open(file: Blob, name: string): Promise<boolean> {
    opening.value = true
    openError.value = null
    try {
      const problem = await check(file, name)
      if (typeof problem === 'string') {
        openError.value = problem
        return false
      }

      cancel()
      release()
      image.value = { ...problem, name, blob: file, size: file.size }
      Object.assign(rect, wholeImage(problem))
      error.value = null
      return true
    } finally {
      opening.value = false
    }
  }

  /**
   * Opens the image at a web address. The server downloads it (POST /api/v1/fetch), since browsers keep pages from
   * reading most other sites' images, so its URL fetch settings decide which addresses work.
   */
  async function openUrl(address: string): Promise<boolean> {
    if (limits.value && !limits.value.urlFetch) {
      openError.value = 'This server doesn\'t fetch images by URL. Save the image, then open the file.'
      return false
    }

    opening.value = true
    openError.value = null
    let file: Blob
    try {
      file = await postForBlob(`/api/v1/fetch?url=${encodeURIComponent(address)}`, { headers: apiKey.headers() })
    } catch (failure) {
      opening.value = false
      if (failure instanceof ApiError && failure.status === 401)
        apiKey.reject()
      openError.value = failure instanceof Error ? failure.message : 'The image could not be downloaded.'
      return false
    }
    return await open(file, fileNameFromUrl(address))
  }

  /** The image's format and size, or why it can't be used. */
  async function check(file: Blob, name: string): Promise<string | SourceImage & { url: string }> {
    const max = limits.value
    if (file.size === 0)
      return `“${name}” is empty.`
    if (max && file.size > max.maxInputSize)
      return `“${name}” is ${formatBytes(file.size)}; this server accepts files up to ${formatBytes(max.maxInputSize)}.`

    const head = new Uint8Array(await file.slice(0, 64).arrayBuffer())
    const format = sniffFormat(head)
    if (!format) {
      return isHeif(head)
        ? `“${name}” is a HEIC image, which Image Mason doesn't read. Export it as JPEG first.`
        : `“${name}” isn't a JPEG, PNG, WebP or AVIF image.`
    }

    // Browsers apply the EXIF orientation, so these are the display dimensions the server's rect uses.
    const url = URL.createObjectURL(file)
    const element = new Image()
    element.src = url
    try {
      await element.decode()
    } catch {
      URL.revokeObjectURL(url)
      return `Your browser couldn't display “${name}”. The file may be damaged.`
    }

    const { naturalWidth: width, naturalHeight: height } = element
    if (max && width * height > max.maxPixels) {
      URL.revokeObjectURL(url)
      return `“${name}” is ${formatSize(width, height)} (${formatMegapixels(width * height)}); `
        + `this server accepts up to ${formatMegapixels(max.maxPixels)}.`
    }
    return { format, width, height, url }
  }

  /** Sends the current image as planned, retrying while the server is busy, and adds the result. */
  async function run(plan: JobPlan): Promise<void> {
    const source = image.value
    if (!source || job.value)
      return

    const current = new AbortController()
    controller = current
    error.value = null
    const start = (attempt: number): Job =>
      ({ working: plan.working, phase: 'uploading', progress: 0, since: Date.now(), retryAt: null, attempt })
    job.value = start(1)

    try {
      for (let attempt = 1; ; attempt++) {
        try {
          const response = await postImage(plan.path, source.blob, {
            headers: apiKey.headers(),
            signal: current.signal,
            onUploadProgress: fraction => { if (job.value) job.value.progress = fraction },
            onUploaded: () => { if (job.value) Object.assign(job.value, { phase: 'processing', progress: 1, since: Date.now() }) },
          })
          if (image.value !== source)
            return

          const format = formatFromContentType(response.blob.type || response.header('Content-Type')) ?? plan.format ?? source.format
          results.value.push({
            id: nextId++,
            path: plan.path,
            label: plan.label,
            blob: response.blob,
            url: URL.createObjectURL(response.blob),
            format,
            fileName: resultFileName(source.name, plan.label, format),
            stats: readStats(response.header),
            downloaded: false,
          })
          return
        } catch (failure) {
          if (!(failure instanceof ApiError && failure.status === 503 && attempt < maxAttempts))
            throw failure
          const seconds = Math.min(failure.retryAfter ?? 2 ** attempt, 60)
          job.value = { ...start(attempt + 1), phase: 'waiting', retryAt: Date.now() + seconds * 1000 }
          await delay(seconds * 1000, current.signal)
          job.value = start(attempt + 1)
        }
      }
    } catch (failure) {
      if (failure instanceof DOMException && failure.name === 'AbortError')
        return
      error.value = describe(failure)
      if (failure instanceof ApiError && failure.status === 401)
        apiKey.reject()
    } finally {
      if (controller === current) {
        controller = null
        job.value = null
      }
    }
  }

  function describe(failure: unknown): JobError {
    if (!(failure instanceof ApiError))
      return { message: `Something went wrong in the page: ${String(failure)}`, status: -1, canRetry: true }

    const { status, message } = failure
    switch (status) {
      case 0:
        return { message, status, canRetry: true }
      case 401:
        return { message: 'The server needs a valid API key. Enter it, then try again.', status, canRetry: true }
      case 413:
        return {
          message: `The file is larger than this server accepts${limits.value ? ` (${formatBytes(limits.value.maxInputSize)})` : ''}.`,
          status,
          canRetry: false,
        }
      case 503:
        return { message: `${message} The server was still busy after ${maxAttempts} tries.`, status, canRetry: true }
      case 504:
        return {
          message: `The server stopped after ${limits.value?.timeoutSeconds ?? 'its'} seconds. A smaller size or a lower `
            + 'quality preset finishes sooner.',
          status,
          canRetry: true,
        }
      default:
        return { message, status, canRetry: status >= 500 }
    }
  }

  function cancel() {
    controller?.abort()
  }

  function markDownloaded(id: number) {
    const result = results.value.find(r => r.id === id)
    if (result)
      result.downloaded = true
  }

  function dismissError() {
    error.value = null
    openError.value = null
  }

  /** Revokes the object URLs of the current image and its results. */
  function release() {
    if (image.value)
      URL.revokeObjectURL(image.value.url)
    for (const result of results.value)
      URL.revokeObjectURL(result.url)
    image.value = null
    results.value = []
  }

  return {
    image,
    results,
    job: readonly(job),
    error: readonly(error),
    openError: readonly(openError),
    opening: readonly(opening),
    rect,
    hasUnsavedResults,
    open,
    openUrl,
    run,
    cancel,
    markDownloaded,
    dismissError,
  }
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(new DOMException('The request was cancelled.', 'AbortError'))
    }
    signal.addEventListener('abort', onAbort, { once: true })
  })
}
