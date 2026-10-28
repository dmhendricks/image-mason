import { afterEach, describe, expect, it, vi } from 'vitest'
import { defaultSettings, loadSettings, sanitize, saveSettings, shapeAspect } from './settings'

afterEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

describe('settings', () => {
  it('round-trips through localStorage', () => {
    const settings = { ...defaultSettings(), format: 'avif' as const, resize: true, width: 800, lock: false, crop: true, shape: '4:3' as const }
    saveSettings(settings)

    expect(loadSettings()).toEqual(settings)
  })

  it('puts back defaults for anything missing or invalid', () => {
    const settings = sanitize({ quality: 'medium', minScore: 140, resize: true, width: -5, height: 2.5, shape: '5:4', format: 'gif' })

    expect(settings).toEqual({ ...defaultSettings(), quality: 'medium', resize: true })
  })

  it('drops the settings of the version with tools, keeping quality and format', () => {
    const old = { tool: 'resize', quality: 'low', format: 'webp', preferJpeg: true, maxWidth: 1600, width: 800, height: 600, fit: 'cover', focus: 'attention', upscale: true }

    expect(sanitize(old)).toEqual({ ...defaultSettings(), quality: 'low', format: 'webp' })
  })

  it('gives each shape its aspect, and none for Free', () => {
    expect(shapeAspect('free')).toBeNull()
    expect(shapeAspect('1:1')).toBe(1)
    expect(shapeAspect('16:9')).toBeCloseTo(1.7778, 4)
  })

  it('keeps a chosen quality, and follows the server\'s default without one', () => {
    expect(defaultSettings().quality).toBeNull()
    expect(sanitize({ quality: 'high' }).quality).toBe('high')
    expect(sanitize({ quality: null }).quality).toBeNull()
  })

  it('turns a saved lossless quality into the lossless setting', () => {
    expect(sanitize({ quality: 'lossless' })).toEqual({ ...defaultSettings(), lossless: true })
    expect(sanitize({ lossless: true, quality: 'low' })).toEqual({ ...defaultSettings(), lossless: true, quality: 'low' })
  })

  it('starts from the defaults when storage is blocked or holds junk', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    expect(loadSettings()).toEqual(defaultSettings())

    vi.restoreAllMocks()
    localStorage.setItem('image-mason.settings', '{not json')
    expect(loadSettings()).toEqual(defaultSettings())
  })
})
