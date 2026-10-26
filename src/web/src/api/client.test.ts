import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getJson, parseRetryAfter, postImage } from './client'
import { readStats } from './types'

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

/** Enough of XMLHttpRequest for postImage: answers with the given status, headers and body once sent. */
class FakeXhr {
  static next: { status: number, statusText?: string, headers?: Record<string, string>, body?: string, fail?: boolean, hang?: boolean }
  static last: FakeXhr
  readonly upload: { onprogress?: (e: ProgressEvent) => void, onload?: () => void } = {}
  requestHeaders: Record<string, string> = {}
  method = ''
  url = ''
  responseType = ''
  status = 0
  statusText = ''
  response: Blob | null = null
  onload?: () => void
  onerror?: () => void
  onabort?: () => void

  constructor() { FakeXhr.last = this }
  open(method: string, url: string) { this.method = method; this.url = url }
  setRequestHeader(name: string, value: string) { this.requestHeaders[name] = value }
  getResponseHeader(name: string) {
    const headers = FakeXhr.next.headers ?? {}
    return Object.entries(headers).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1] ?? null
  }

  abort() { this.onabort?.() }

  send(body: Blob) {
    const next = FakeXhr.next
    if (next.hang)
      return
    queueMicrotask(() => {
      if (next.fail) {
        this.onerror?.()
        return
      }
      this.upload.onprogress?.({ lengthComputable: true, loaded: body.size, total: body.size } as ProgressEvent)
      this.upload.onload?.()
      this.status = next.status
      this.statusText = next.statusText ?? ''
      this.response = new Blob([next.body ?? ''], { type: this.getResponseHeader('Content-Type') ?? '' })
      this.onload?.()
    })
  }
}

describe('postImage', () => {
  const image = new Blob([new Uint8Array([0xFF, 0xD8, 0xFF])], { type: 'image/jpeg' })

  it('sends the image as the body and returns the response with its headers', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.next = { status: 200, headers: { 'Content-Type': 'image/webp', 'X-Image-Mason-Method': 'cwebp-q79' }, body: 'RIFF' }
    const progress: number[] = []
    const uploaded = vi.fn()

    const response = await postImage('/api/v1/optimize?quality=high', image, {
      headers: { 'X-Api-Key': 'secret' },
      onUploadProgress: fraction => progress.push(fraction),
      onUploaded: uploaded,
    })

    expect(FakeXhr.last.method).toBe('POST')
    expect(FakeXhr.last.url).toBe('/api/v1/optimize?quality=high')
    expect(FakeXhr.last.requestHeaders).toEqual({ 'X-Api-Key': 'secret', 'Content-Type': 'image/jpeg' })
    expect(response.blob.type).toBe('image/webp')
    expect(response.header('x-image-mason-method')).toBe('cwebp-q79')
    expect(progress).toEqual([1])
    expect(uploaded).toHaveBeenCalledOnce()
  })

  it('throws the Problem Details detail with Retry-After', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.next = { status: 503, statusText: 'Service Unavailable', headers: { 'Retry-After': '30' }, body: '{"detail":"Out of memory."}' }

    const error = await postImage('/api/v1/optimize', image).catch((e: unknown) => e)

    expect(error).toMatchObject({ name: 'ApiError', status: 503, retryAfter: 30, message: 'Out of memory.' })
  })

  it('reports an unreachable server as status 0', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.next = { status: 0, fail: true }

    await expect(postImage('/api/v1/optimize', image)).rejects.toMatchObject({ status: 0 })
  })

  it('aborts through the signal', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.next = { status: 200, hang: true }
    const controller = new AbortController()

    const request = postImage('/api/v1/optimize', image, { signal: controller.signal })
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('doesn\'t start when the signal is already aborted', async () => {
    const controller = new AbortController()
    controller.abort()

    await expect(postImage('/api/v1/optimize', image, { signal: controller.signal })).rejects.toMatchObject({ name: 'AbortError' })
  })
})

describe('parseRetryAfter', () => {
  it('reads seconds and HTTP dates', () => {
    const now = Date.parse('2026-10-25T12:00:00Z')

    expect(parseRetryAfter('30', now)).toBe(30)
    expect(parseRetryAfter('Sun, 25 Oct 2026 12:00:45 GMT', now)).toBe(45)
    expect(parseRetryAfter('soon', now)).toBeNull()
    expect(parseRetryAfter(null, now)).toBeNull()
  })
})

describe('readStats', () => {
  it('reads the X-Image-Mason-* headers', () => {
    const headers: Record<string, string> = {
      'X-Image-Mason-Quality': 'high',
      'X-Image-Mason-Original-Size': '161713',
      'X-Image-Mason-Optimized-Size': '129005',
      'X-Image-Mason-Original-Dimensions': '640x480',
      'X-Image-Mason-Dimensions': '320x240',
      'X-Image-Mason-Method': 'resize+jpegli-d1.063',
      'X-Image-Mason-Duration-Ms': '136',
      'X-Image-Mason-Score': '85.13',
      'X-Image-Mason-Min-Score': '85',
    }

    expect(readStats(name => headers[name] ?? null)).toEqual({
      quality: 'high',
      originalSize: 161713,
      size: 129005,
      originalWidth: 640,
      originalHeight: 480,
      width: 320,
      height: 240,
      method: 'resize+jpegli-d1.063',
      durationMs: 136,
      score: 85.13,
      minScore: 85,
    })
  })

  it('has no score for lossless results', () => {
    expect(readStats(() => null)).toMatchObject({ score: null, minScore: null, size: 0, width: 0 })
  })
})
