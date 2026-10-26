// Turns the current tool, settings and image into a request, the size it should produce, and anything the server
// would reject, in plain words. The rules mirror ResizeParameters, RectParameter, OptimizeEndpoint and
// ImageOptimizer.CheckOutputSize, so a request the interface allows never comes back as a 400 or 422.

import type { Limits } from './api/types'
import { formatMegapixels, formatName, formatSize } from './format'
import { type Rect, type Size, rectCovers, rectFits, rectParameter, resizeOutput } from './geometry'
import { type ImageFormat, type Settings, type Tool, chosenFormat } from './settings'

/** Largest width or height the API accepts (ResizeParameters.MaxSide). */
export const maxSide = 65535

export interface SourceImage extends Size {
  format: ImageFormat
}

export type Field = 'minScore' | 'maxWidth' | 'width' | 'height' | 'rect' | 'output'

export interface JobPlan {
  /** Path and query to POST the image to. */
  path: string
  /** The size the result should have, when the image is known. */
  output: Size | null
  /** The result's format, when known (a PNG photo with preferJpeg may still become JPEG). */
  format: ImageFormat | null
  /** What's wrong, per field; empty when the job can run. */
  errors: Partial<Record<Field, string>>
  /** The run button: "Optimize", "Resize to 800 × 600", "Convert to WebP". */
  action: string
  /** While it runs: "Optimizing", "Resizing". */
  working: string
  /** A short name for the result: "High", "Medium, WebP", "Low, 800 × 533". */
  label: string
}

const qualityNames = { lossless: 'Lossless', high: 'High', medium: 'Medium', low: 'Low' }
const workingNames = { optimize: 'Optimizing', resize: 'Resizing', crop: 'Cropping', convert: 'Converting' }

export function planJob(tool: Tool, settings: Settings, image: SourceImage | null, rect: Rect | null, limits: Limits | null): JobPlan {
  const errors: JobPlan['errors'] = {}
  const query = new URLSearchParams()
  const lossless = settings.quality === 'lossless'

  query.set('quality', settings.quality)
  if (settings.customScore && !lossless) {
    if (Number.isFinite(settings.minScore) && settings.minScore >= 0 && settings.minScore <= 100)
      query.set('minScore', String(settings.minScore))
    else
      errors.minScore = 'Enter a score from 0 to 100.'
  }

  // Output format: convert always sends one; the others keep the input's unless one is chosen.
  const format = tool === 'convert' ? settings.convertTo : chosenFormat(settings.format, image?.format)
  if (format !== null)
    query.set('format', format)
  if (tool === 'optimize' && format === null && settings.preferJpeg)
    query.set('preferJpeg', 'true')

  // Resize takes a box; the other tools take maxWidth, which falls back to the server's default.
  let sizing: Parameters<typeof resizeOutput>[0] | null = null
  if (tool === 'resize') {
    const { width, height, fit } = settings
    for (const side of ['width', 'height'] as const) {
      const value = settings[side]
      if (value !== null && !(Number.isInteger(value) && value >= 1 && value <= maxSide))
        errors[side] = `Enter 1 to ${maxSide} pixels.`
    }
    if (width === null && height === null)
      errors.width = 'Enter a width, a height or both.'
    else if (fit !== 'contain') {
      const message = 'Fill box and Stretch need both a width and a height.'
      if (width === null)
        errors.width = message
      if (height === null)
        errors.height = message
    }

    if (width !== null)
      query.set('width', String(width))
    if (height !== null)
      query.set('height', String(height))
    if (fit !== 'contain')
      query.set('fit', fit)
    if (fit === 'cover' && settings.focus !== 'center')
      query.set('crop', settings.focus)
    // Fill always reaches the exact size, so enlarging is implied.
    const upscale = settings.upscale && fit !== 'fill'
    if (upscale)
      query.set('upscale', 'true')
    sizing = { width, height, fit, upscale }
  } else {
    const value = settings.maxWidth
    if (value !== null && !(Number.isInteger(value) && value >= 0 && value <= maxSide))
      errors.maxWidth = `Enter a width up to ${maxSide}, or 0 for none.`
    else if (value !== null)
      query.set('maxWidth', String(value))

    const maxWidth = value ?? (lossless ? 0 : limits?.defaultMaxWidth ?? 0)
    if (maxWidth > 0)
      sizing = { width: maxWidth, height: null, fit: 'contain', upscale: false }
  }

  // Crop's area, checked against the image as displayed.
  let source: Size | null = image
  if (tool === 'crop' && image && rect) {
    const whole = [rect.left, rect.top, rect.width, rect.height].every(Number.isInteger)
    if (!whole || !rectFits(rect, image))
      errors.rect = `The area must lie inside the image, which is ${formatSize(image.width, image.height)}.`
    else {
      query.set('rect', rectParameter(rect))
      if (!rectCovers(rect, image))
        source = { width: rect.width, height: rect.height }
    }
  }

  const resultFormat = format ?? (settings.preferJpeg && tool === 'optimize' && image?.format === 'png' ? null : image?.format ?? null)
  let output: Size | null = null
  if (source && Object.keys(errors).length === 0) {
    output = sizing ? resizeOutput(sizing, source) : { width: source.width, height: source.height }
    const problem = outputProblem(output, resultFormat, limits)
    if (problem)
      errors.output = problem
  }

  return {
    path: `/api/v1/${tool}?${query}`,
    output,
    format: resultFormat,
    errors,
    action: actionName(tool, output, settings),
    working: workingNames[tool],
    label: [
      settings.customScore && !lossless ? `Score ${settings.minScore}` : qualityNames[settings.quality],
      format !== null && format !== image?.format ? formatName(format) : null,
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

function actionName(tool: Tool, output: Size | null, settings: Settings): string {
  switch (tool) {
    case 'optimize':
      return 'Optimize'
    case 'resize':
      return output ? `Resize to ${formatSize(output.width, output.height)}` : 'Resize'
    case 'crop':
      return output ? `Crop to ${formatSize(output.width, output.height)}` : 'Crop'
    case 'convert':
      return `Convert to ${formatName(settings.convertTo)}`
  }
}
