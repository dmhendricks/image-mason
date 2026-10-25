import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getJson } from './client'

function respond(body: string, status = 200, statusText = 'OK') {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(body, { status, statusText })))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('getJson', () => {
  it('returns the parsed body', async () => {
    respond('{"apiKeyRequired":false}')

    await expect(getJson('/api/v1/limits')).resolves.toEqual({ apiKeyRequired: false })
  })

  it('throws the Problem Details detail on an error status', async () => {
    respond('{"title":"Bad Request","status":400,"detail":"rect must be left,top,width,height."}', 400, 'Bad Request')

    const error = await getJson('/api/v1/crop').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).message).toBe('rect must be left,top,width,height.')
  })

  it('falls back to the status line when the body is not JSON', async () => {
    respond('<html>Bad gateway</html>', 502, 'Bad Gateway')

    await expect(getJson('/api/v1/limits')).rejects.toThrow('The server answered 502 Bad Gateway.')
  })

  it('reports an unreachable server as status 0', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch') }))

    const error = await getJson('/api/v1/limits').catch((e: unknown) => e)

    expect((error as ApiError).status).toBe(0)
  })

  it('passes an abort through unchanged', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new DOMException('Aborted', 'AbortError') }))

    await expect(getJson('/api/v1/limits')).rejects.toMatchObject({ name: 'AbortError' })
  })
})
