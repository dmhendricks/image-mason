import { describe, expect, it } from 'vitest'
import type { Limits } from './api/types'
import { wholeImage } from './geometry'
import { type Job, type SourceImage, jobAnnouncement, jobStatus, planJob, resultAnnouncement, scoredPixelRange } from './jobs'
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
    const plan = planJob(settings(), photo, null, limits)

    expect(plan.path).toBe('/api/v1/transform?quality=medium')
    expect(plan.source).toEqual({ width: 640, height: 480 })
    expect(plan.output).toEqual({ width: 640, height: 480 })
    expect(plan.cropped).toBe(false)
    expect(plan.format).toBe('jpeg')
    expect(plan.errors).toEqual({})
    expect(plan.label).toBe('Medium')
  })

  it('uses the server\'s default quality until one is chosen', () => {
    const lowServer = { ...limits, defaultQuality: 'low' }
    expect(query(planJob(settings(), photo, null, lowServer).path)).toEqual({ quality: 'low' })
    expect(planJob(settings(), photo, null, lowServer).label).toBe('Low')
    expect(query(planJob(settings({ quality: 'high' }), photo, null, lowServer).path)).toEqual({ quality: 'high' })
    expect(query(planJob(settings(), photo, null, { ...limits, defaultQuality: 'ultra' }).path)).toEqual({ quality: 'high' })
  })

  it('sends a custom score, but not for lossless', () => {
    expect(query(planJob(settings({ customScore: true, minScore: 82.5 }), photo, null, limits).path))
      .toMatchObject({ minScore: '82.5' })
    expect(query(planJob(settings({ customScore: true, lossless: true }), photo, null, limits).path))
      .toEqual({ quality: 'lossless' })
  })

  it('labels the job by lossless, a custom score or the preset', () => {
    expect(planJob(settings({ lossless: true, customScore: true }), photo, null, limits).label).toBe('Lossless')
    expect(planJob(settings({ customScore: true, minScore: 75 }), photo, null, limits).label).toBe('Score 75')
    expect(planJob(settings({ quality: 'low' }), photo, null, limits).label).toBe('Low')
  })

  it('rejects a score outside 0 to 100', () => {
    const plan = planJob(settings({ customScore: true, minScore: 101 }), photo, null, limits)

    expect(plan.errors.minScore).toBe('Enter a score from 0 to 100.')
  })

  it('sends a chosen format, and treats the input\'s own as keeping it', () => {
    const png: SourceImage = { width: 640, height: 480, format: 'png' }

    expect(query(planJob(settings({ format: 'jpeg' }), photo, null, limits).path)).toEqual({ quality: 'medium' })
    expect(query(planJob(settings({ format: 'jpeg' }), png, null, limits).path)).toEqual({ quality: 'medium', format: 'jpeg' })
    expect(planJob(settings({ format: 'webp' }), photo, null, limits).label).toBe('Medium, WebP')
  })

  it('ignores the size while Resize is off', () => {
    expect(query(planJob(settings({ width: 320 }), photo, null, limits).path)).toEqual({ quality: 'medium' })
  })

  it('sends nothing for Resize until a size is typed', () => {
    const plan = planJob(settings({ resize: true }), photo, null, limits)

    expect(query(plan.path)).toEqual({ quality: 'medium' })
    expect(plan.output).toEqual({ width: 640, height: 480 })
  })

  it('sends the typed side while the shape is kept, and the server scales the other', () => {
    const byWidth = planJob(settings({ resize: true, width: 320 }), photo, null, limits)
    expect(query(byWidth.path)).toEqual({ quality: 'medium', width: '320' })
    expect(byWidth.output).toEqual({ width: 320, height: 240 })
    expect(byWidth.label).toBe('Medium, 320 × 240')

    const byHeight = planJob(settings({ resize: true, height: 120 }), photo, null, limits)
    expect(query(byHeight.path)).toEqual({ quality: 'medium', height: '120' })
    expect(byHeight.output).toEqual({ width: 160, height: 120 })
  })

  it('enlarges when the typed side is larger', () => {
    const plan = planJob(settings({ resize: true, width: 1280 }), photo, null, limits)

    expect(query(plan.path)).toEqual({ quality: 'medium', width: '1280', upscale: 'true' })
    expect(plan.output).toEqual({ width: 1280, height: 960 })
  })

  it('stretches to both sides when the shape isn\'t kept', () => {
    const plan = planJob(settings({ resize: true, lock: false, width: 300, height: 200 }), photo, null, limits)
    expect(query(plan.path)).toEqual({ quality: 'medium', width: '300', height: '200', fit: 'fill' })
    expect(plan.output).toEqual({ width: 300, height: 200 })

    expect(planJob(settings({ resize: true, lock: false, width: 300 }), photo, null, limits).output).toEqual({ width: 300, height: 480 })
  })

  it('rejects sides outside 1 to 65535 and text that isn\'t a number', () => {
    expect(planJob(settings({ resize: true, width: 70000 }), photo, null, limits).errors.width).toBe('Enter 1 to 65535 pixels.')
    const plan = planJob(settings({ resize: true, lock: false, width: 300, height: NaN }), photo, null, limits)
    expect(plan.errors.height).toBe('Enter 1 to 65535 pixels.')
    expect(plan.output).toBeNull()
  })

  it('crops to the area while Crop is on, and resizes the area', () => {
    const area = { left: 80, top: 0, width: 480, height: 480 }
    const cropped = planJob(settings({ crop: true }), photo, area, limits)
    expect(query(cropped.path)).toEqual({ quality: 'medium', crop: '80,0,480,480' })
    expect(cropped.source).toEqual({ width: 480, height: 480 })
    expect(cropped.cropped).toBe(true)

    const both = planJob(settings({ crop: true, resize: true, width: 240 }), photo, area, limits)
    expect(query(both.path)).toEqual({ quality: 'medium', crop: '80,0,480,480', width: '240' })
    expect(both.output).toEqual({ width: 240, height: 240 })

    expect(query(planJob(settings(), photo, area, limits).path)).toEqual({ quality: 'medium' })
  })

  it('rejects an area reaching past the image', () => {
    const plan = planJob(settings({ crop: true }), photo, { left: 200, top: 0, width: 480, height: 480 }, limits)

    expect(plan.errors.rect).toBe('The area must lie inside the image, which is 640 × 480.')
  })

  it('keeps the whole image when the area covers it', () => {
    const plan = planJob(settings({ crop: true }), photo, wholeImage(photo), limits)

    expect(query(plan.path)).toEqual({ quality: 'medium' })
    expect(plan.cropped).toBe(false)
    expect(plan.errors).toEqual({})
  })

  it('refuses results past the pixel limit or a format\'s largest side', () => {
    const big: SourceImage = { width: 20000, height: 2000, format: 'jpeg' }

    expect(planJob(settings({ format: 'webp' }), big, null, limits).errors.output)
      .toBe('WebP images can be at most 16383 pixels on each side; the result would be 20000 × 2000.')
    expect(planJob(settings({ resize: true, lock: false, width: 10000, height: 10000 }), photo, null, limits).errors.output)
      .toBe('The result would be 10000 × 10000 (100 megapixels); this server\'s limit is 50 megapixels.')
  })

  it('plans without an image or limits', () => {
    const plan = planJob(settings({ resize: true, width: 300 }), null, null, null)

    expect(plan.path).toBe('/api/v1/transform?quality=high')
    expect(plan.source).toBeNull()
    expect(plan.output).toBeNull()
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
    const plan = planJob(settings(), big, null, memoryLimits)

    expect(plan.errors.output).toBe('This needs about 3.77 GB of memory, but this server has 3.75 GB for jobs. A smaller size, or Lossless, needs less.')
  })

  it('allows the same image losslessly, or resized to fit', () => {
    expect(planJob(settings({ lossless: true }), big, null, memoryLimits).errors).toEqual({})
    expect(planJob(settings({ resize: true, width: 4000 }), big, null, memoryLimits).errors).toEqual({})
  })

  it('counts the larger format\'s cost when converting', () => {
    const photo20: SourceImage = { width: 5000, height: 4000, format: 'jpeg' }

    expect(planJob(settings(), photo20, null, memoryLimits).errors).toEqual({})
    expect(planJob(settings({ format: 'png' }), photo20, null, memoryLimits).errors.output).toMatch(/memory/)
  })

  it('holds lossless to JPEG to a score once the pixels change', () => {
    const png: SourceImage = { width: 6000, height: 4000, format: 'png' }

    expect(planJob(settings({ lossless: true, format: 'jpeg' }), png, null, memoryLimits).errors.output)
      .toMatch(/A smaller size needs less\.$/)
  })

  it('skips the check on servers that don\'t report memory', () => {
    expect(planJob(settings(), big, null, limits).errors).toEqual({})
  })

  it('reports what a lossy job can take at full size', () => {
    expect(scoredPixelRange(memoryLimits)).toEqual([19997085, 23851945])
    expect(scoredPixelRange(limits)).toBeNull()
    expect(scoredPixelRange({ ...memoryLimits, maxPixels: 10_000_000 })).toBeNull()
  })
})

describe('jobStatus', () => {
  const job: Job = { phase: 'processing', progress: 1, since: 10_000, retryAt: null, attempt: 1 }

  it('shows the upload\'s progress', () => {
    expect(jobStatus({ ...job, phase: 'uploading', progress: 0.426 }, 10_000)).toBe('Uploading… 43%')
  })

  it('counts whole seconds while the server works, from the first one', () => {
    expect(jobStatus(job, 10_999)).toBe('Transforming…')
    expect(jobStatus(job, 13_500)).toBe('Transforming… 3 s')
  })

  it('counts down to the next try, rounding up and stopping at 0', () => {
    const waiting: Job = { ...job, phase: 'waiting', retryAt: 12_000, attempt: 2 }
    expect(jobStatus(waiting, 10_500)).toBe('The server is busy. Trying again in 2 s (try 2 of 3).')
    expect(jobStatus(waiting, 12_400)).toBe('The server is busy. Trying again in 0 s (try 2 of 3).')
  })
})

describe('announcements', () => {
  const job = (changes: Partial<Job>): Job =>
    ({ phase: 'processing', progress: 0, since: 1000, retryAt: null, attempt: 1, ...changes })

  it('names each phase once, without the ticking time', () => {
    expect(jobAnnouncement(job({ phase: 'uploading', progress: 0.4 }))).toBe('Uploading the image.')
    expect(jobAnnouncement(job({}))).toBe('Transforming.')
    expect(jobAnnouncement(job({ phase: 'waiting', retryAt: 4000, attempt: 2 })))
      .toBe('The server is busy. Trying again in 3 seconds (try 2 of 3).')
  })

  it('says how the result compares in words', () => {
    expect(resultAnnouncement('Medium', 641_393, 390_144)).toBe('Done: Medium, 626 KB to 381 KB, 39% smaller.')
    expect(resultAnnouncement('Lossless', 307_039, 705_995)).toBe('Done: Lossless, 300 KB to 689 KB, 130% larger.')
    expect(resultAnnouncement('High', 1000, 1000)).toBe('Done: High, 1000 bytes to 1000 bytes, the same size.')
  })
})

