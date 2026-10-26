import { describe, expect, it } from 'vitest'
import type { Limits } from './api/types'
import { wholeImage } from './geometry'
import { type SourceImage, planJob } from './jobs'
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
    expect(query(planJob('optimize', settings(), photo, null, { ...limits, defaultQuality: 'ultra' }).path)).toEqual({ quality: 'medium' })
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

    expect(plan.path).toBe('/api/v1/resize?quality=medium&width=300')
    expect(plan.output).toBeNull()
    expect(plan.action).toBe('Resize')
  })
})
