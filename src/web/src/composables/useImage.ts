import { reactive, readonly, ref, shallowRef } from 'vue'
import {
  ApiError,
  type ImageResponse,
  type PostImageOptions,
  deleteStoredImage,
  postForBlob,
  postImage,
  uploadImage,
} from '../api/client'
import { retryWhileBusy } from '../api/retry'
import { readStats } from '../api/types'
import {
  type Measured,
  fileNameFromUrl,
  formatFromContentType,
  isHeif,
  limitProblem,
  loadImage,
  measure,
  resultFileName,
  sniffFormat,
} from '../files'
import { type Rect, type Size, centeredRect, insetRect, scaledRect, wholeImage } from '../geometry'
import { type Job, type JobError, type JobPlan, type SourceImage, describeFailure } from '../jobs'
import { type Settings, shapeAspect } from '../settings'
import { useApiKey } from './useApiKey'
import { useResults } from './useResults'
import { useServer } from './useServer'
import { useSettings } from './useSettings'

// The current image and the request running now; its results are in useResults. One image at a time (docs/WEB-UI.md
// W8); a queue of images could later hold one of these per file.

export interface OpenedImage extends SourceImage {
  name: string
  blob: Blob
  size: number
  /** Object URL for <img>; revoked when the image is replaced. */
  url: string
  /** Shown instead of url when the image is much larger than the screen; closed when the image is replaced. */
  preview: ImageBitmap | null
  /** The web address it was opened from, so the server can download it again instead of the browser uploading it. */
  address?: string
}

/** An image being read, or downloaded by the server first. */
export interface Opening {
  /** The file name, or the one taken from the web address. */
  name: string
  /** Whether the server is downloading it (openUrl), which can take a while and be cancelled. */
  fromUrl: boolean
}

const image = shallowRef<OpenedImage | null>(null)
const job = ref<Job | null>(null)
const error = ref<JobError | null>(null)
const openError = ref<string | null>(null)
const opening = shallowRef<Opening | null>(null)
/** Crop's area, in display pixels; NaN while a field is empty or not a number. */
const rect = reactive<Rect>({ left: 0, top: 0, width: 0, height: 0 })

let controller: AbortController | null = null
/** The current image's copy on the server (POST /api/v1/upload), which jobs name as source instead of sending it. */
let stored: { image: OpenedImage, name: string } | null = null
/** An image the server couldn't store: each job sends it, as before stored copies. */
let unstorable: OpenedImage | null = null
/** The open in progress. A newer open aborts it, so only the last image chosen is opened. */
let openController: AbortController | null = null

export function useImage() {
  const apiKey = useApiKey()
  const { limits } = useServer()
  const { hasUnsavedResults, add, clear } = useResults()
  const { settings } = useSettings()

  /**
   * Checks a file and makes it the current image, replacing the last one and its results. When results haven't been
   * downloaded, confirmDiscard is asked first; false keeps them and the current image.
   */
  async function open(file: Blob, name: string, confirmDiscard?: () => boolean): Promise<boolean> {
    const current = beginOpen({ name, fromUrl: false })
    try {
      return await load(file, name, current.signal, confirmDiscard)
    } finally {
      endOpen(current)
    }
  }

  /**
   * Opens the image at a web address. The server downloads it (POST /api/v1/fetch), since browsers keep pages from
   * reading most other sites' images, so its URL fetch settings decide which addresses work.
   */
  async function openUrl(address: string, confirmDiscard?: () => boolean): Promise<boolean> {
    if (limits.value && !limits.value.urlFetch) {
      openError.value = 'This server doesn\'t fetch images by URL. Save the image, then open the file.'
      return false
    }

    const name = fileNameFromUrl(address)
    const current = beginOpen({ name, fromUrl: true })
    try {
      let file: Blob
      try {
        file = await postForBlob(`/api/v1/fetch?url=${encodeURIComponent(address)}`,
          { headers: apiKey.headers(), signal: current.signal })
      } catch (failure) {
        if (current.signal.aborted)
          return false
        if (failure instanceof ApiError && failure.status === 401)
          apiKey.reject()
        openError.value = failure instanceof Error ? failure.message : 'The image could not be downloaded.'
        return false
      }
      return await load(file, name, current.signal, confirmDiscard, address)
    } finally {
      endOpen(current)
    }
  }

  /** Stops the open in progress, such as a download that's taking too long. */
  function cancelOpen() {
    openController?.abort()
    openController = null
    opening.value = null
  }

  /** Starts an open, stopping any still in progress: the last image chosen wins. */
  function beginOpen(next: Opening): AbortController {
    openController?.abort()
    const current = new AbortController()
    openController = current
    opening.value = next
    openError.value = null
    return current
  }

  function endOpen(current: AbortController) {
    if (openController === current) {
      openController = null
      opening.value = null
    }
  }

  async function load(file: Blob, name: string, signal: AbortSignal, confirmDiscard?: () => boolean,
    address?: string): Promise<boolean> {
    const checked = await check(file, name)
    const superseded = signal.aborted
    if (typeof checked === 'string') {
      if (!superseded)
        openError.value = checked
      return false
    }

    // Asked here rather than when the open started: a result can arrive while a download or a large image is read.
    if (superseded || (hasUnsavedResults.value && confirmDiscard && !confirmDiscard())) {
      discard(checked)
      return false
    }

    const previous = image.value && { width: image.value.width, height: image.value.height }
    cancel()
    release()
    image.value = { ...checked, name, blob: file, size: file.size, address }
    Object.assign(rect, nextRect(settings, rect, previous, checked))
    error.value = null
    return true
  }

  /** The image's format and size, or why it can't be used. */
  async function check(file: Blob, name: string): Promise<string | SourceImage & Measured> {
    const max = limits.value
    const sizeProblem = limitProblem(name, file.size, null, max)
    if (sizeProblem)
      return sizeProblem

    let head: Uint8Array
    try {
      head = new Uint8Array(await file.slice(0, 64).arrayBuffer())
    } catch {
      // Moved or deleted since it was chosen, or a cloud file that isn't on this device.
      return `Couldn't read “${name}”. It may have been moved, or not be downloaded to this device yet.`
    }
    const format = sniffFormat(head)
    if (!format) {
      return isHeif(head)
        ? `“${name}” is a HEIC image, which Image Mason doesn't read. Export it as JPEG first.`
        : `“${name}” isn't a PNG, WebP, AVIF, JPEG or JPEG XL image.`
    }

    // Not every browser decodes JPEG XL yet; the file is most likely fine.
    const undisplayable = format === 'jxl'
      ? `Your browser can't display JPEG XL, so “${name}” can't be opened here. Try a browser that shows JPEG XL.`
      : `Your browser couldn't display “${name}”. The file may be damaged.`

    // The size is checked before the pixels are decoded: a 108 MP scan would take about 430 MB to decode.
    const loaded = await loadImage(file)
    if (!loaded)
      return undisplayable
    const pixelProblem = limitProblem(name, file.size, loaded, max)
    if (pixelProblem) {
      URL.revokeObjectURL(loaded.url)
      return pixelProblem
    }

    try {
      const { width, height, url } = loaded
      return { format, width, height, url, preview: await loaded.decode() }
    } catch {
      URL.revokeObjectURL(loaded.url)
      return undisplayable
    }
  }

  /**
   * Sends the current image as planned, retrying while the server is busy, and adds the result. The first job stores
   * the image on the server; later ones name the stored copy instead of uploading it again, and upload it again if
   * that copy has expired.
   */
  async function run(plan: JobPlan): Promise<void> {
    const source = image.value
    if (!source || job.value)
      return

    const current = new AbortController()
    controller = current
    error.value = null
    const start = (attempt: number): Job => ({
      phase: stored?.image === source ? 'processing' : 'uploading',
      progress: 0,
      since: Date.now(),
      retryAt: null,
      attempt,
    })
    job.value = start(1)
    const options: PostImageOptions = {
      headers: apiKey.headers(),
      signal: current.signal,
      onUploadProgress: fraction => { if (job.value) job.value.progress = fraction },
      onUploaded: () => { if (job.value) Object.assign(job.value, { phase: 'processing', progress: 1, since: Date.now() }) },
    }
    let uploadedAgain = false

    // One try, uploading the image first unless the server has it.
    const send = async (attempt: number): Promise<ImageResponse> => {
      try {
        const name = await storedName(source, options)
        return name
          ? await postImage(withSource(plan.path, name), null, options)
          : await postImage(plan.path, source.blob, options)
      } catch (failure) {
        // The stored copy expired or was deleted: upload it again, once, without counting an attempt.
        if (failure instanceof ApiError && failure.status === 404 && stored?.image === source && !uploadedAgain) {
          stored = null
          uploadedAgain = true
          job.value = start(attempt)
          return await send(attempt)
        }
        throw failure
      }
    }

    try {
      const response = await retryWhileBusy(attempt => {
        if (attempt > 1)
          job.value = start(attempt)
        return send(attempt)
      }, current.signal, (attempt, retryAt) => {
        job.value = { ...start(attempt), phase: 'waiting', retryAt }
      })
      if (image.value !== source)
        return

      const format = formatFromContentType(response.blob.type || response.header('Content-Type')) ?? plan.format ?? source.format
      // Not decodable (JPEG XL in some browsers): the canvas says so, and it can still be downloaded.
      const shown = await measure(response.blob)
      if (image.value !== source) {
        if (shown)
          discard(shown)
        return
      }
      add({
        path: plan.path,
        label: plan.label,
        blob: response.blob,
        url: shown?.url ?? URL.createObjectURL(response.blob),
        preview: shown?.preview ?? null,
        format,
        fileName: resultFileName(source.name, plan.label, format),
        stats: readStats(response.header),
      })
    } catch (failure) {
      if (failure instanceof DOMException && failure.name === 'AbortError')
        return
      error.value = describeFailure(failure, limits.value)
      if (failure instanceof ApiError && failure.status === 401)
        apiKey.reject()
    } finally {
      if (controller === current) {
        controller = null
        job.value = null
      }
    }
  }

  /**
   * The current image's name on the server, storing it the first time. An image opened by web address is downloaded
   * by the server again rather than uploaded, falling back to uploading it. Null when the server can't store it, so
   * each job sends the image instead.
   */
  async function storedName(source: OpenedImage, options: PostImageOptions): Promise<string | null> {
    if (stored?.image === source)
      return stored.name
    if (unstorable === source)
      return null

    let name: string | null = null
    if (source.address) {
      try {
        name = await uploadImage(`/api/v1/upload?url=${encodeURIComponent(source.address)}`, null, options)
      } catch (failure) {
        if (!canSendInstead(failure))
          throw failure
      }
    }
    try {
      name ??= await uploadImage('/api/v1/upload', source.blob, options)
    } catch (failure) {
      if (!canSendInstead(failure))
        throw failure
      unstorable = source
      return null
    }

    if (image.value !== source) {
      void deleteStoredImage(name, apiKey.headers())
      throw new DOMException('Another image was opened.', 'AbortError')
    }
    stored = { image: source, name }
    return name
  }

  function cancel() {
    controller?.abort()
  }

  /** Clears the last job's error. */
  function dismissError() {
    error.value = null
  }

  /** Clears the message about an image that couldn't be opened. */
  function dismissOpenError() {
    openError.value = null
  }

  /** Revokes the object URLs of the current image and its results, and deletes its stored copy. */
  function release() {
    if (stored) {
      void deleteStoredImage(stored.name, apiKey.headers())
      stored = null
    }
    unstorable = null
    if (image.value)
      discard(image.value)
    clear()
    image.value = null
  }

  return {
    image,
    job: readonly(job),
    error: readonly(error),
    openError: readonly(openError),
    opening: readonly(opening),
    rect,
    open,
    openUrl,
    cancelOpen,
    run,
    cancel,
    dismissError,
    dismissOpenError,
  }
}

/**
 * Crop's area on a new image. While Crop is on it carries over: a shape's largest centered area, or with Free, the same
 * area in proportion (the frame's start, when there was no image before). Otherwise the whole image.
 */
export function nextRect(settings: Pick<Settings, 'crop' | 'shape'>, rect: Rect, previous: Size | null, image: Size): Rect {
  if (!settings.crop)
    return wholeImage(image)
  const aspect = shapeAspect(settings.shape)
  if (aspect)
    return centeredRect(aspect, image)
  return (previous && scaledRect(rect, previous, image)) ?? insetRect(image)
}

/** Frees an image's object URL and preview. */
function discard(shown: Pick<Measured, 'url' | 'preview'>) {
  URL.revokeObjectURL(shown.url)
  shown.preview?.close()
}

/** The job's path with source naming the stored image. */
function withSource(path: string, name: string): string {
  return `${path}${path.includes('?') ? '&' : '?'}source=${encodeURIComponent(name)}`
}

/**
 * Whether a failed upload can be worked around by sending the image with the job instead. Not when the server is
 * unreachable, busy or rejects the key, or when the image itself was refused, since the job would fail the same way.
 */
function canSendInstead(failure: unknown): boolean {
  return failure instanceof ApiError && ![0, 401, 413, 415, 422, 503].includes(failure.status)
}
