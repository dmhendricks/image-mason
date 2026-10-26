import { afterEach, describe, expect, it, vi } from 'vitest'
import { defaultSettings, loadSettings, sanitize, saveSettings } from './settings'

afterEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

describe('settings', () => {
  it('round-trips through localStorage', () => {
    const settings = { ...defaultSettings(), tool: 'crop' as const, format: 'avif' as const, width: 800, maxWidth: 0 }
    saveSettings(settings)

    expect(loadSettings()).toEqual(settings)
  })

  it('puts back defaults for anything missing or invalid', () => {
    const settings = sanitize({ tool: 'sharpen', quality: 'medium', minScore: 140, width: -5, height: 2.5, fit: 'cover', format: 'gif' })

    expect(settings).toEqual({ ...defaultSettings(), quality: 'medium', fit: 'cover' })
  })

  it('starts from the defaults when storage is blocked or holds junk', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    expect(loadSettings()).toEqual(defaultSettings())

    vi.restoreAllMocks()
    localStorage.setItem('image-mason.settings', '{not json')
    expect(loadSettings()).toEqual(defaultSettings())
  })
})
