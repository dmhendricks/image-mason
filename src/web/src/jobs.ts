// Turns the settings and image into a transform request, the size it should produce, and anything the server would
// reject, in plain words. The rules mirror ResizeParameters, CropParameter, TransformEndpoint and
// ImageOptimizer.CheckOutputSize and EstimateMemory, so a request the interface allows never comes back as a 400 or 422.
// Once the job runs: its progress and what went wrong, in plain words too.

import { ApiError } from './api/client'
import { maxAttempts } from './api/retry'
import type { Limits } from './api/types'
import { formatBytes, formatChange, formatMegapixels, formatName, formatSize } from './format'
import { type Rect, type Size, rectCovers, rectFits, rectParameter, resizeOutput } from './geometry'
import { type ImageFormat, type Settings, chosenFormat, effectiveQuality } from './settings'

/** Largest width or height the API accepts (ResizeParameters.MaxSide). */
export const maxSide = 65535

export interface SourceImage extends Size {
  format: ImageFormat
}

export type Field = 'minScore' | 'width' | 'height' | 'rect' | 'output'

export interface JobPlan {
  /** Path and query to POST the image to. */
  path: string
  /** What Resize starts from: Crop's area when it crops, else the whole image; null without an image. */
  source: Size | null
  /** The size the result should have, when the image is known and Resize's sides are valid sizes. */
  output: Size | null
  /** Crop is on and its area isn't the whole image. */
  cropped: boolean
  /** The result's format, when the image is known. */
  format: ImageFormat | null
  /** What's wrong, per field; empty when the job can run. */
  errors: Partial<Record<Field, string>>
  /** A short name for the result: "High", "Medium, WebP", "Low, 800 × 533". */
  label: string
}

const qualityNames = { high: 'High', medium: 'Medium', low: 'Low' }

const validSide = (value: number | null) => value === null || (Number.isInteger(value) && value >= 1 && value <= maxSide)

/**
 * Resize's output for a source: the typed sides, the other one following the shape while locked; the source's own
 * size for a side nothing was typed in. Null while a typed side isn't 1 to 65535.
 */
export function resizedSize(settings: Pick<Settings, 'width' | 'height' | 'lock'>, source: Size): Size | null {
  const { width, height, lock } = settings
  if (!validSide(width) || !validSide(height))
    return null
  if (!lock)
    return { width: width ?? source.width, height: height ?? source.height }
  if (width === null && height === null)
    return { width: source.width, height: source.height }
  // As the server sizes it, from the side typed last (width, when a saved pair has both).
  return resizeOutput({ width, height: width === null ? height : null, fit: 'contain', upscale: true }, source)
}

export function planJob(settings: Settings, image: SourceImage | null, rect: Rect | null, limits: Limits | null): JobPlan {
  const errors: JobPlan['errors'] = {}
  const query = new URLSearchParams()
  const lossless = settings.lossless
  const quality = effectiveQuality(settings.quality, limits?.defaultQuality)

  query.set('quality', lossless ? 'lossless' : quality)
  if (settings.customScore && !lossless) {
    if (Number.isFinite(settings.minScore) && settings.minScore >= 0 && settings.minScore <= 100)
      query.set('minScore', String(settings.minScore))
    else
      errors.minScore = 'Enter a score from 0 to 100.'
  }

  const format = chosenFormat(settings.format, image?.format)
  if (format !== null)
    query.set('format', format)

  // Crop's area, checked against the image as displayed. Resize then starts from it.
  let source: Size | null = image && { width: image.width, height: image.height }
  let cropped = false
  if (settings.crop && image && rect) {
    const whole = [rect.left, rect.top, rect.width, rect.height].every(Number.isInteger)
    if (!whole || !rectFits(rect, image))
      errors.rect = `The area must lie inside the image, which is ${formatSize(image.width, image.height)}.`
    else if (!rectCovers(rect, image)) {
      query.set('crop', rectParameter(rect))
      source = { width: rect.width, height: rect.height }
      cropped = true
    }
  }

  // Locked: the typed side, which the server scales the other to (enlarging only when it's larger). Unlocked: both,
  // stretched. Nothing when the size is the source's own.
  let output = source
  if (settings.resize) {
    for (const side of ['width', 'height'] as const) {
      if (!validSide(settings[side]))
        errors[side] = `Enter 1 to ${maxSide} pixels.`
    }
    output = source && resizedSize(settings, source)
    if (source && output && (output.width !== source.width || output.height !== source.height)) {
      if (settings.lock) {
        const side = settings.width !== null ? 'width' : 'height'
        query.set(side, String(output[side]))
        if (output.width > source.width)
          query.set('upscale', 'true')
      } else {
        query.set('width', String(output.width))
        query.set('height', String(output.height))
        query.set('fit', 'fill')
      }
    }
  }

  const resultFormat = format ?? image?.format ?? null
  if (output && image && Object.keys(errors).length === 0) {
    let problem = outputProblem(output, resultFormat, limits)
    if (!problem) {
      // As in ImageOptimizer: lossy presets are scored, and so is lossless to JPEG once the pixels change.
      const target = resultFormat ?? image.format
      const changed = cropped || output.width !== image.width || output.height !== image.height || target !== image.format
      const scored = !lossless || (target === 'jpeg' && changed)
      problem = memoryProblem(image, output, target, scored, lossless, limits)
    }
    if (problem)
      errors.output = problem
  }

  return {
    path: `/api/v1/transform?${query}`,
    source,
    output,
    cropped,
    format: resultFormat,
    errors,
    label: [
      lossless ? 'Lossless' : settings.customScore ? `Score ${settings.minScore}` : qualityNames[quality],
      format !== null ? formatName(format) : null,
      output && image && (output.width !== image.width || output.height !== image.height) ? formatSize(output.width, output.height) : null,
    ].filter(Boolean).join(', '),
  }
}

/** The server's ImageOptimizer.CheckOutputSize, in the interface's words. */
function outputProblem(output: Size, format: ImageFormat | null, limits: Limits | null): string | null {
  if (!limits)
    return null

  const pixels = output.width * output.height
  if (pixels > limits.maxPixels) {
    return `The result would be ${formatSize(output.width, output.height)} (${formatMegapixels(pixels)}); `
      + `this server's limit is ${formatMegapixels(limits.maxPixels)}.`
  }

  const formatMaxSide = limits.formats.find(f => f.name === format)?.maxSide
  if (format && formatMaxSide && (output.width > formatMaxSide || output.height > formatMaxSide)) {
    return `${formatName(format)} images can be at most ${formatMaxSide} pixels on each side; `
      + `the result would be ${formatSize(output.width, output.height)}.`
  }
  return null
}

/** The server's ImageOptimizer.EstimateMemory against its job budget, in the interface's words. */
function memoryProblem(image: SourceImage, output: Size, target: ImageFormat, scored: boolean, lossless: boolean,
  limits: Limits | null): string | null {
  const memory = limits?.jobMemory
  if (!memory)
    return null

  const perPixel = (format: ImageFormat) => limits.formats.find(f => f.name === format)?.bytesPerPixel ?? 0
  const outputPixels = output.width * output.height
  const needed = memory.overhead
    + Math.max(image.width * image.height, outputPixels) * Math.max(perPixel(image.format), perPixel(target))
    + (scored ? outputPixels * memory.scoringBytesPerPixel : 0)
  if (needed <= memory.budget)
    return null

  // Two decimals when one would make the two look the same ("3.8 GB of 3.8 GB").
  let [need, have] = [formatBytes(needed), formatBytes(memory.budget)]
  if (need === have)
    [need, have] = [needed, memory.budget].map(bytes => `${(bytes / 1024 ** 3).toFixed(2)} GB`) as [string, string]
  return `This needs about ${need} of memory, but this server has ${have} for jobs. `
    + (scored && !lossless ? 'A smaller size, or Lossless, needs less.' : 'A smaller size needs less.')
}

/**
 * The largest image, in pixels, a lossy job can take at its original size: the smallest and largest across formats
 * (each kept in its own format). Null when the server doesn't say or the pixel limit is lower anyway.
 */
export function scoredPixelRange(limits: Limits | null): [number, number] | null {
  const memory = limits?.jobMemory
  if (!memory)
    return null

  const fits = limits.formats
    .filter(f => f.bytesPerPixel !== undefined)
    .map(f => Math.min(limits.maxPixels, Math.floor((memory.budget - memory.overhead) / (f.bytesPerPixel! + memory.scoringBytesPerPixel))))
  if (fits.length === 0 || Math.min(...fits) >= limits.maxPixels)
    return null
  return [Math.max(0, Math.min(...fits)), Math.max(0, Math.max(...fits))]
}

/** A job on its way: uploading the image, waiting for the server to make the result, or waiting to try again. */
export interface Job {
  phase: 'uploading' | 'processing' | 'waiting'
  /** Fraction of the image uploaded, 0 to 1. */
  progress: number
  /** When the current phase started (Date.now()). */
  since: number
  /** While waiting: when the next attempt starts. */
  retryAt: number | null
  attempt: number
}

/** The line under the progress bar at the time now: "Uploading… 40%", "Transforming… 3 s", or the retry countdown. */
export function jobStatus(job: Job, now: number): string {
  switch (job.phase) {
    case 'uploading':
      return `Uploading… ${Math.round(job.progress * 100)}%`
    case 'processing': {
      const seconds = Math.floor((now - job.since) / 1000)
      return seconds < 1 ? 'Transforming…' : `Transforming… ${seconds} s`
    }
    case 'waiting': {
      const seconds = Math.max(0, Math.ceil(((job.retryAt ?? now) - now) / 1000))
      return `The server is busy. Trying again in ${seconds} s (try ${job.attempt} of ${maxAttempts}).`
    }
  }
  return ''
}

/**
 * What a screen reader hears when a job changes phase: "Uploading the image.", "Transforming.", or the wait. Unlike
 * jobStatus it doesn't tick, so each phase is announced once.
 */
export function jobAnnouncement(job: Job): string {
  switch (job.phase) {
    case 'uploading':
      return 'Uploading the image.'
    case 'processing':
      return 'Transforming.'
    case 'waiting': {
      const seconds = Math.max(0, Math.ceil(((job.retryAt ?? job.since) - job.since) / 1000))
      return `The server is busy. Trying again in ${seconds} seconds (try ${job.attempt} of ${maxAttempts}).`
    }
  }
  return ''
}

/** The finished job, for a screen reader: "Done: Medium, 626 KB to 381 KB, 39% smaller." */
export function resultAnnouncement(label: string, originalSize: number, size: number): string {
  const change = formatChange(originalSize, size)
  const words = change.startsWith('−') ? `${change.slice(1)} smaller`
    : change.startsWith('+') ? `${change.slice(1)} larger`
      : 'the same size'
  return `Done: ${label}, ${formatBytes(originalSize)} to ${formatBytes(size)}, ${words}.`
}

export interface JobError {
  message: string
  /** The HTTP status; 0 when the server couldn't be reached. */
  status: number
  /** Whether trying the same request again might work. */
  canRetry: boolean
}

/** Why a job failed and whether to offer trying again, from what it threw. */
export function describeFailure(failure: unknown, limits: Limits | null): JobError {
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
        message: `The file is larger than this server accepts${limits ? ` (${formatBytes(limits.maxInputSize)})` : ''}.`,
        status,
        canRetry: false,
      }
    case 503:
      return { message: `${message} The server was still busy after ${maxAttempts} tries.`, status, canRetry: true }
    case 504:
      return {
        message: `The server stopped after ${limits?.timeoutSeconds ?? 'its'} seconds. A smaller size or a lower `
          + 'quality preset finishes sooner.',
        status,
        canRetry: true,
      }
    default:
      return { message, status, canRetry: status >= 500 }
  }
}
