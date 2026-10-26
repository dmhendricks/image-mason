import { describe, expect, it } from 'vitest'
import type { Limits } from './api/types'
import { wholeImage } from './geometry'
import { type Job, type SourceImage, jobStatus, planJob, scoredPixelRange } from './jobs'
import { type Settings, defaultSettings } from './settings'

const limits: Limits = {
  formats: [
    { name: 'jpeg', mimeType: 'image/jpeg', extension: '.jpg', maxSide: 65535 },
    { name: 'png', mimeType: 'image/png', extension: '.png' },
    { name: 'webp', mimeType: 'image/webp', extension: '.webp', maxSide: 16383 },
    { name: 'avif', mimeType: 'image/avif', extension: '.avif' },
  ],
  maxInputSize: 26214400,
  maxPixels: 50_000_000,
  presets: { lossless: null, high: 85, medium: 80, low: 70 },
  defaultQuality: 'medium',
  defaultMaxWidth: 0,
  imageTtlMinutes: 20,
  timeoutSeconds: 120,
  urlFetch: false,
  apiKeyRequired: false,
}

const photo: SourceImage = { width: 640, height: 480, format: 'jpeg' }

const settings = (changes: Partial<Settings> = {}): Settings => ({ ...defaultSettings(), ...changes })
const query = (path: string) => Object.fromEntries(new URL(path, 'http://x').searchParams)

describe('planJob', () => {
  it('optimizes with only the quality by default', () => {
    const plan = planJob('optimize', settings(), photo, null, limits)

    expect(plan.path).toBe('/api/v1/optimize?quality=medium')
    expect(plan.output).toEqual({ width: 640, height: 480 })
    expect(plan.format).toBe('jpeg')
    expect(plan.errors).toEqual({})
    expect(plan.action).toBe('Optimize')
    expect(plan.label).toBe('Medium')
  })

  it('uses the server\'s default quality until one is chosen', () => {
    const lowServer = { ...limits, defaultQuality: 'low' }
    expect(query(planJob('optimize', settings(), photo, null, lowServer).path)).toEqual({ quality: 'low' })
    expect(planJob('optimize', settings(), photo, null, lowServer).label).toBe('Low')
    expect(query(planJob('optimize', settings({ quality: 'high' }), photo, null, lowServer).path)).toEqual({ quality: 'high' })
    expect(query(planJob('optimize', settings(), photo, null, { ...limits, defaultQuality: 'ultra' }).path)).toEqual({ quality: 'high' })
  })

  it('sends a custom score, but not for lossless', () => {
    expect(query(planJob('optimize', settings({ customScore: true, minScore: 82.5 }), photo, null, limits).path))
      .toMatchObject({ minScore: '82.5' })
    expect(query(planJob('optimize', settings({ customScore: true, lossless: true }), photo, null, limits).path))
      .toEqual({ quality: 'lossless' })
  })

  it('labels the job by lossless, a custom score or the preset', () => {
    expect(planJob('optimize', settings({ lossless: true, customScore: true }), photo, null, limits).label).toBe('Lossless')
    expect(planJob('optimize', settings({ customScore: true, minScore: 75 }), photo, null, limits).label).toBe('Score 75')
    expect(planJob('optimize', settings({ quality: 'low' }), photo, null, limits).label).toBe('Low')
  })

  it('rejects a score outside 0 to 100', () => {
    const plan = planJob('optimize', settings({ customScore: true, minScore: 101 }), photo, null, limits)

    expect(plan.errors.minScore).toBe('Enter a score from 0 to 100.')
  })

  it('sends preferJpeg only when no format is chosen', () => {
    expect(query(planJob('optimize', settings({ preferJpeg: true }), photo, null, limits).path)).toMatchObject({ preferJpeg: 'true' })
    expect(query(planJob('optimize', settings({ preferJpeg: true, format: 'webp' }), photo, null, limits).path))
      .toEqual({ quality: 'medium', format: 'webp' })
  })

  it('treats a chosen format that is the input\'s own as keeping it', () => {
    const png: SourceImage = { width: 640, height: 480, format: 'png' }

    expect(query(planJob('optimize', settings({ format: 'jpeg' }), photo, null, limits).path)).toEqual({ quality: 'medium' })
    expect(query(planJob('optimize', settings({ format: 'png', preferJpeg: true }), png, null, limits).path))
      .toEqual({ quality: 'medium', preferJpeg: 'true' })
    expect(query(planJob('optimize', settings({ format: 'jpeg' }), png, null, limits).path))
      .toEqual({ quality: 'medium', format: 'jpeg' })
  })

  it('shrinks to maxWidth, or to the server default when none is sent', () => {
    expect(planJob('optimize', settings({ maxWidth: 320 }), photo, null, limits).output).toEqual({ width: 320, height: 240 })
    expect(planJob('optimize', settings(), photo, null, { ...limits, defaultMaxWidth: 400 }).output).toEqual({ width: 400, height: 300 })
    // Lossless skips the server default, as ImageOptimizer does.
    expect(planJob('optimize', settings({ lossless: true }), photo, null, { ...limits, defaultMaxWidth: 400 }).output)
      .toEqual({ width: 640, height: 480 })
  })

  it('needs a width or height to resize', () => {
    const plan = planJob('resize', settings(), photo, null, limits)

    expect(plan.errors.width).toBe('Enter a width, a height or both.')
    expect(plan.output).toBeNull()
  })

  it('resizes to a box with fit and focus', () => {
    const plan = planJob('resize', settings({ width: 300, height: 300, fit: 'cover', focus: 'attention' }), photo, null, limits)

    expect(query(plan.path)).toEqual({ quality: 'medium', width: '300', height: '300', fit: 'cover', crop: 'attention' })
    expect(plan.output).toEqual({ width: 300, height: 300 })
    expect(plan.action).toBe('Resize to 300 × 300')
    expect(plan.label).toBe('Medium, 300 × 300')
  })

  it('needs both sides for cover and fill', () => {
    const plan = planJob('resize', settings({ width: 300, fit: 'fill' }), photo, null, limits)

    expect(plan.errors.height).toBe('Fill box and Stretch need both a width and a height.')
    expect(plan.errors.width).toBeUndefined()
  })

  it('rejects sides outside 1 to 65535 and text that isn\'t a number', () => {
    expect(planJob('resize', settings({ width: 70000 }), photo, null, limits).errors.width).toBe('Enter 1 to 65535 pixels.')
    expect(planJob('resize', settings({ height: NaN }), photo, null, limits).errors.height).toBe('Enter 1 to 65535 pixels.')
  })

  it('leaves out focus unless cover, and upscale for fill', () => {
    expect(query(planJob('resize', settings({ width: 300, focus: 'entropy' }), photo, null, limits).path)).not.toHaveProperty('crop')
    expect(query(planJob('resize', settings({ width: 300, height: 200, fit: 'fill', upscale: true }), photo, null, limits).path))
      .not.toHaveProperty('upscale')
  })

  it('crops to the rect, then applies maxWidth', () => {
    const plan = planJob('crop', settings({ maxWidth: 240 }), photo, { left: 80, top: 0, width: 480, height: 480 }, limits)

    expect(query(plan.path)).toEqual({ quality: 'medium', maxWidth: '240', rect: '80,0,480,480' })
    expect(plan.output).toEqual({ width: 240, height: 240 })
    expect(plan.action).toBe('Crop to 240 × 240')
  })

  it('rejects a rect reaching past the image', () => {
    const plan = planJob('crop', settings(), photo, { left: 200, top: 0, width: 480, height: 480 }, limits)

    expect(plan.errors.rect).toBe('The area must lie inside the image, which is 640 × 480.')
  })

  it('keeps the whole image when the rect covers it', () => {
    const plan = planJob('crop', settings(), photo, wholeImage(photo), limits)

    expect(plan.output).toEqual({ width: 640, height: 480 })
    expect(plan.errors).toEqual({})
  })

  it('always sends convert\'s format', () => {
    const plan = planJob('convert', settings({ convertTo: 'avif', format: 'png' }), photo, null, limits)

    expect(query(plan.path)).toEqual({ quality: 'medium', format: 'avif' })
    expect(plan.action).toBe('Convert to AVIF')
    expect(plan.label).toBe('Medium, AVIF')
  })

  it('refuses results past the pixel limit or a format\'s largest side', () => {
    const big: SourceImage = { width: 20000, height: 10000, format: 'jpeg' }

    expect(planJob('convert', settings({ convertTo: 'webp' }), { ...big, height: 2000 }, null, limits).errors.output)
      .toBe('WebP images can be at most 16383 pixels on each side; the result would be 20000 × 2000.')
    expect(planJob('resize', settings({ width: 10000, height: 10000, fit: 'fill' }), photo, null, limits).errors.output)
      .toBe('The result would be 10000 × 10000 (100 megapixels); this server\'s limit is 50 megapixels.')
  })

  it('plans without an image or limits', () => {
    const plan = planJob('resize', settings({ width: 300 }), null, null, null)

    expect(plan.path).toBe('/api/v1/resize?quality=high&width=300')
    expect(plan.output).toBeNull()
    expect(plan.action).toBe('Resize')
  })
})

describe('memory', () => {
  // A 4 GB container: 3.75 GiB for jobs, as GET /api/v1/limits reports it.
  const memoryLimits: Limits = {
    ...limits,
    formats: limits.formats.map(f => ({ ...f, bytesPerPixel: { jpeg: 16, png: 48, webp: 32, avif: 40 }[f.name] })),
    jobMemory: { budget: 4026531840, overhead: 67108864, scoringBytesPerPixel: 150 },
  }
  const big: SourceImage = { width: 6000, height: 4000, format: 'jpeg' }

  it('blocks a lossy job the server would reject for memory, before anything is sent', () => {
    const plan = planJob('optimize', settings(), big, null, memoryLimits)

    expect(plan.errors.output).toBe('This needs about 3.77 GB of memory, but this server has 3.75 GB for jobs. A smaller size, or Lossless, needs less.')
  })

  it('allows the same image losslessly, or resized to fit', () => {
    expect(planJob('optimize', settings({ lossless: true }), big, null, memoryLimits).errors).toEqual({})
    expect(planJob('optimize', settings({ maxWidth: 4000 }), big, null, memoryLimits).errors).toEqual({})
  })

  it('counts the larger format\'s cost when converting', () => {
    const photo20: SourceImage = { width: 5000, height: 4000, format: 'jpeg' }

    expect(planJob('convert', settings({ convertTo: 'jpeg' }), photo20, null, memoryLimits).errors).toEqual({})
    expect(planJob('convert', settings({ convertTo: 'png' }), photo20, null, memoryLimits).errors.output).toMatch(/memory/)
  })

  it('holds lossless to JPEG to a score once the pixels change', () => {
    const png: SourceImage = { width: 6000, height: 4000, format: 'png' }

    expect(planJob('convert', settings({ lossless: true, convertTo: 'jpeg' }), png, null, memoryLimits).errors.output)
      .toMatch(/A smaller size needs less\.$/)
  })

  it('skips the check on servers that don\'t report memory', () => {
    expect(planJob('optimize', settings(), big, null, limits).errors).toEqual({})
  })

  it('reports what a lossy job can take at full size', () => {
    expect(scoredPixelRange(memoryLimits)).toEqual([19997085, 23851945])
    expect(scoredPixelRange(limits)).toBeNull()
    expect(scoredPixelRange({ ...memoryLimits, maxPixels: 10_000_000 })).toBeNull()
  })
})

describe('jobStatus', () => {
  const job: Job = { working: 'Optimizing', phase: 'processing', progress: 1, since: 10_000, retryAt: null, attempt: 1 }

  it('shows the upload\'s progress', () => {
    expect(jobStatus({ ...job, phase: 'uploading', progress: 0.426 }, 10_000)).toBe('Uploading… 43%')
  })

  it('counts whole seconds while the server works, from the first one', () => {
    expect(jobStatus(job, 10_999)).toBe('Optimizing…')
    expect(jobStatus(job, 13_500)).toBe('Optimizing… 3 s')
  })

  it('counts down to the next try, rounding up and stopping at 0', () => {
    const waiting: Job = { ...job, phase: 'waiting', retryAt: 12_000, attempt: 2 }
    expect(jobStatus(waiting, 10_500)).toBe('The server is busy. Trying again in 2 s (try 2 of 3).')
    expect(jobStatus(waiting, 12_400)).toBe('The server is busy. Trying again in 0 s (try 2 of 3).')
  })
})
