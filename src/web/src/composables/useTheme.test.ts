import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applySavedTheme } from './useTheme'

function systemPrefersDark(dark: boolean) {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: dark, addEventListener() {}, removeEventListener() {} })))
}

// The theme is shared module state, read on first use, so each test imports a fresh copy.
async function load() {
  vi.resetModules()
  const { useTheme } = await import('./useTheme')
  return useTheme()
}

beforeEach(() => {
  localStorage.clear()
  delete document.documentElement.dataset.theme
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useTheme', () => {
  it('follows the system until the user chooses', async () => {
    systemPrefersDark(true)

    const { theme } = await load()

    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBeUndefined()
  })

  it('toggles, applies and remembers the choice', async () => {
    systemPrefersDark(false)
    const { theme, toggle } = await load()

    toggle()

    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem('image-mason.theme')).toBe('dark')
  })

  it('still toggles when storage is blocked', async () => {
    systemPrefersDark(false)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Blocked', 'SecurityError') })
    const { theme, toggle } = await load()

    toggle()

    expect(theme.value).toBe('dark')
  })
})

describe('applySavedTheme', () => {
  it('applies a remembered choice', () => {
    localStorage.setItem('image-mason.theme', 'light')

    applySavedTheme()

    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('ignores anything else', () => {
    localStorage.setItem('image-mason.theme', 'purple')

    applySavedTheme()

    expect(document.documentElement.dataset.theme).toBeUndefined()
  })
})
