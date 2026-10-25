import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import { applySavedTheme, useTheme } from './useTheme'

function systemPrefersDark(dark: boolean) {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: dark, addEventListener() {}, removeEventListener() {} })))
}

function withTheme() {
  const scope = effectScope()
  const result = scope.run(() => useTheme())!
  return { ...result, stop: () => scope.stop() }
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
  it('follows the system until the user chooses', () => {
    systemPrefersDark(true)

    const { theme, stop } = withTheme()

    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBeUndefined()
    stop()
  })

  it('toggles, applies and remembers the choice', () => {
    systemPrefersDark(false)
    const { theme, toggle, stop } = withTheme()

    toggle()

    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem('image-mason.theme')).toBe('dark')
    stop()
  })

  it('still toggles when storage is blocked', () => {
    systemPrefersDark(false)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Blocked', 'SecurityError') })
    const { theme, toggle, stop } = withTheme()

    toggle()

    expect(theme.value).toBe('dark')
    stop()
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
