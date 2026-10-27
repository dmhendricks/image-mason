import { beforeEach, describe, expect, it, vi } from 'vitest'

// The key is read from storage when the module loads, so each test imports a fresh copy.
async function load() {
  vi.resetModules()
  const { useApiKey } = await import('./useApiKey')
  return useApiKey()
}

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  vi.restoreAllMocks()
})

describe('useApiKey', () => {
  it('remembers on this device in localStorage', async () => {
    (await load()).save('secret', true)

    expect(localStorage.getItem('image-mason.api-key')).toBe('secret')
    expect(sessionStorage.getItem('image-mason.api-key')).toBeNull()

    const reloaded = await load()
    expect(reloaded.key.value).toBe('secret')
    expect(reloaded.remembered.value).toBe(true)
    expect(reloaded.headers()).toEqual({ 'X-Api-Key': 'secret' })
  })

  it('keeps the key for the tab only in sessionStorage', async () => {
    const apiKey = await load()
    apiKey.save('first', true)
    apiKey.save('second', false)

    expect(localStorage.getItem('image-mason.api-key')).toBeNull()
    expect(sessionStorage.getItem('image-mason.api-key')).toBe('second')
  })

  it('forgets a rejected key and says so', async () => {
    const apiKey = await load()
    apiKey.save('old', true)
    apiKey.reject()

    expect(apiKey.key.value).toBeNull()
    expect(apiKey.rejected.value).toBe(true)
    expect(apiKey.headers()).toEqual({})
    expect(localStorage.getItem('image-mason.api-key')).toBeNull()
  })

  it('forgetting the key clears the rejection too', async () => {
    const apiKey = await load()
    apiKey.save('old', true)
    apiKey.reject()
    apiKey.forget()

    expect(apiKey.rejected.value).toBe(false)
  })

  it('drops a stored key the browser couldn\'t send', async () => {
    localStorage.setItem('image-mason.api-key', 'clé')
    const apiKey = await load()

    expect(apiKey.key.value).toBeNull()
    expect(apiKey.remembered.value).toBe(false)
    expect(apiKey.headers()).toEqual({})
  })

  it('accepts printable ASCII keys only', async () => {
    const { isSendableKey } = await import('./useApiKey')
    expect(isSendableKey('abc-123_XYZ ~!')).toBe(true)
    expect(isSendableKey('clé')).toBe(false)
    expect(isSendableKey('key\n')).toBe(false)
    expect(isSendableKey('')).toBe(false)
  })

  it('keeps the key in memory when storage is blocked', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    const apiKey = await load()
    apiKey.save('secret', true)

    expect(apiKey.key.value).toBe('secret')
  })
})
