import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, type ImageResponse } from '../api/client'
import type { JobPlan } from '../jobs'

const client = vi.hoisted(() => ({
  // The server's limits never arrive, so the size checks are skipped.
  getJson: vi.fn(() => new Promise(() => {})),
  postForBlob: vi.fn<(path: string, init?: RequestInit) => Promise<Blob>>(),
  postImage: vi.fn<(path: string, body: Blob | null) => Promise<ImageResponse>>(),
  uploadImage: vi.fn<(path: string, body: Blob | null) => Promise<string>>(),
  deleteStoredImage: vi.fn<(name: string) => Promise<void>>(),
}))

vi.mock('../api/client', async importOriginal => ({
  ...await importOriginal<typeof import('../api/client')>(),
  ...client,
}))

const png = (name: string) =>
  new File([new Uint8Array([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])], name, { type: 'image/png' })

const plan = { path: '/api/v1/optimize?quality=high', output: null, format: null, errors: {}, action: 'Optimize', working: 'Optimizing', label: 'High' } satisfies JobPlan

/** A file of these bytes. */
const bytes = (name: string, ...values: number[]) => new File([new Uint8Array(values)], name)

/** A job the test finishes. */
function pendingJob() {
  let finish!: (response: ImageResponse) => void
  client.postImage.mockImplementationOnce(() => new Promise<ImageResponse>(resolve => { finish = resolve }))
  return () => finish({ blob: new Blob(['late'], { type: 'image/png' }), header: () => null })
}

/** A download the test finishes, which rejects like fetch when its signal aborts. */
function pendingDownload() {
  let finish!: (blob: Blob) => void
  client.postForBlob.mockImplementationOnce((_path, init) => new Promise<Blob>((resolve, reject) => {
    finish = resolve
    init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
  }))
  return (blob: Blob) => finish(blob)
}

// The image, its results and the open in progress are module state, so each test imports a fresh copy.
async function load() {
  vi.resetModules()
  const { useImage } = await import('./useImage')
  const { useResults } = await import('./useResults')
  return { ...useImage(), ...useResults() }
}

beforeEach(() => {
  // jsdom has no object URLs and doesn't load or decode images.
  let objectUrls = 0
  URL.createObjectURL = vi.fn(() => `blob:${++objectUrls}`)
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLImageElement.prototype, 'src', 'set').mockImplementation(function (this: HTMLImageElement) {
    setTimeout(() => this.onload?.(new Event('load')))
  })
  HTMLImageElement.prototype.decode = () => Promise.resolve()
  vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(40)
  vi.spyOn(HTMLImageElement.prototype, 'naturalHeight', 'get').mockReturnValue(30)
  client.postImage.mockImplementation(async () =>
    ({ blob: new Blob(['result'], { type: 'image/png' }), header: () => null }))
  let stored = 0
  client.uploadImage.mockImplementation(async () => `stored-${++stored}.png`)
  client.deleteStoredImage.mockImplementation(async () => {})
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  client.postForBlob.mockReset()
  client.postImage.mockReset()
  client.uploadImage.mockReset()
  client.deleteStoredImage.mockReset()
})

describe('useImage', () => {
  it('drops a download that finishes after another image was opened', async () => {
    const images = await load()
    const finish = pendingDownload()
    const download = images.openUrl('https://example.com/slow.png')
    expect(images.opening.value).toEqual({ name: 'slow.png', fromUrl: true })

    expect(await images.open(png('dropped.png'), 'dropped.png')).toBe(true)
    finish(png('slow.png'))

    expect(await download).toBe(false)
    expect(images.image.value?.name).toBe('dropped.png')
    expect(images.opening.value).toBeNull()
    expect(images.openError.value).toBeNull()
  })

  it('aborts the download when a newer open starts', async () => {
    const images = await load()
    pendingDownload()
    void images.openUrl('https://example.com/slow.png')
    const signal = client.postForBlob.mock.calls[0]![1]!.signal!

    await images.open(png('dropped.png'), 'dropped.png')

    expect(signal.aborted).toBe(true)
  })

  it('cancels a download without an error', async () => {
    const images = await load()
    pendingDownload()
    const download = images.openUrl('https://example.com/slow.png')

    images.cancelOpen()

    expect(await download).toBe(false)
    expect(images.opening.value).toBeNull()
    expect(images.openError.value).toBeNull()
    expect(images.image.value).toBeNull()
  })

  it('asks before replacing results that arrived during a download, and keeps them when declined', async () => {
    const images = await load()
    await images.open(png('first.png'), 'first.png')
    const finish = pendingDownload()
    const download = images.openUrl('https://example.com/second.png', () => false)

    await images.run(plan)
    finish(png('second.png'))

    expect(await download).toBe(false)
    expect(images.image.value?.name).toBe('first.png')
    expect(images.results.value).toHaveLength(1)
  })

  it('doesn\'t ask when every result was downloaded', async () => {
    const images = await load()
    await images.open(png('first.png'), 'first.png')
    await images.run(plan)
    images.markDownloaded(images.results.value[0]!.id)
    const confirmDiscard = vi.fn(() => false)

    expect(await images.open(png('second.png'), 'second.png', confirmDiscard)).toBe(true)
    expect(confirmDiscard).not.toHaveBeenCalled()
    expect(images.results.value).toHaveLength(0)
  })

  it('replaces the result when the same settings run again', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    await images.run(plan)
    const first = images.results.value[0]!
    images.markDownloaded(first.id)

    await images.run(plan)

    expect(images.results.value).toHaveLength(1)
    expect(images.results.value[0]!.id).not.toBe(first.id)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(first.url)
    expect(images.hasUnsavedResults.value).toBe(true)
  })

  it('uploads the image once and names the stored copy in later jobs', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')

    await images.run(plan)
    await images.run({ ...plan, path: '/api/v1/optimize?quality=low', label: 'Low' })

    expect(client.uploadImage).toHaveBeenCalledOnce()
    expect(client.uploadImage.mock.calls[0]![0]).toBe('/api/v1/upload')
    expect(client.postImage.mock.calls.map(([path, body]) => [path, body])).toEqual([
      ['/api/v1/optimize?quality=high&source=stored-1.png', null],
      ['/api/v1/optimize?quality=low&source=stored-1.png', null],
    ])
    expect(images.results.value.map(r => r.path)).toEqual(['/api/v1/optimize?quality=high', '/api/v1/optimize?quality=low'])
  })

  it('uploads again once when the stored copy has expired', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    await images.run(plan)
    client.postImage.mockRejectedValueOnce(new ApiError('No stored image has that name.', 404))

    await images.run({ ...plan, path: '/api/v1/optimize?quality=low', label: 'Low' })

    expect(client.uploadImage).toHaveBeenCalledTimes(2)
    expect(client.postImage.mock.calls.at(-1)![0]).toBe('/api/v1/optimize?quality=low&source=stored-2.png')
    expect(images.error.value).toBeNull()
    expect(images.results.value).toHaveLength(2)
  })

  it('sends the image with each job when the server can\'t store it', async () => {
    const images = await load()
    const file = png('photo.png')
    await images.open(file, 'photo.png')
    client.uploadImage.mockRejectedValueOnce(new ApiError('The server answered 500.', 500))

    await images.run(plan)
    await images.run({ ...plan, path: '/api/v1/optimize?quality=low', label: 'Low' })

    expect(client.uploadImage).toHaveBeenCalledOnce()
    expect(client.postImage.mock.calls.map(([path, body]) => [path, body])).toEqual([
      ['/api/v1/optimize?quality=high', file],
      ['/api/v1/optimize?quality=low', file],
    ])
  })

  it('has the server download an image opened by web address instead of uploading it', async () => {
    const images = await load()
    client.postForBlob.mockResolvedValueOnce(png('remote.png'))
    await images.openUrl('https://example.com/a photo.png')

    await images.run(plan)

    expect(client.uploadImage).toHaveBeenCalledOnce()
    expect(client.uploadImage.mock.calls[0]!.slice(0, 2))
      .toEqual([`/api/v1/upload?url=${encodeURIComponent('https://example.com/a photo.png')}`, null])
  })

  it('deletes the stored copy when another image is opened', async () => {
    const images = await load()
    await images.open(png('first.png'), 'first.png')
    await images.run(plan)
    images.markDownloaded(images.results.value[0]!.id)

    await images.open(png('second.png'), 'second.png')

    expect(client.deleteStoredImage).toHaveBeenCalledWith('stored-1.png', expect.anything())
  })

  it('keeps results made with different settings', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    await images.run(plan)
    await images.run({ ...plan, path: '/api/v1/optimize?quality=low', label: 'Low' })

    expect(images.results.value.map(r => r.label)).toEqual(['High', 'Low'])
  })

  describe('when the server is busy', () => {
    it('waits as asked and tries again', async () => {
      const images = await load()
      await images.open(png('photo.png'), 'photo.png')
      vi.useFakeTimers()
      client.postImage.mockRejectedValueOnce(new ApiError('The server is busy.', 503, 3))

      const running = images.run(plan)
      await vi.advanceTimersByTimeAsync(0)
      expect(images.job.value).toMatchObject({ phase: 'waiting', attempt: 2, retryAt: Date.now() + 3000 })

      await vi.advanceTimersByTimeAsync(3000)
      // The result is then measured, which waits for a (fake) load event.
      await vi.runAllTimersAsync()
      await running

      expect(client.postImage).toHaveBeenCalledTimes(2)
      expect(client.uploadImage).toHaveBeenCalledOnce()
      expect(images.results.value).toHaveLength(1)
      expect(images.job.value).toBeNull()
      expect(images.error.value).toBeNull()
    })

    it('gives up after three tries', async () => {
      const images = await load()
      await images.open(png('photo.png'), 'photo.png')
      vi.useFakeTimers()
      client.postImage.mockRejectedValue(new ApiError('The server is busy.', 503))

      const running = images.run(plan)
      await vi.advanceTimersByTimeAsync(2000 + 4000)
      await running

      expect(client.postImage).toHaveBeenCalledTimes(3)
      expect(images.error.value).toEqual({
        message: 'The server is busy. The server was still busy after 3 tries.', status: 503, canRetry: true,
      })
      expect(images.results.value).toHaveLength(0)
    })

    it('stops waiting when cancelled, without an error', async () => {
      const images = await load()
      await images.open(png('photo.png'), 'photo.png')
      vi.useFakeTimers()
      client.postImage.mockRejectedValueOnce(new ApiError('The server is busy.', 503))

      const running = images.run(plan)
      await vi.advanceTimersByTimeAsync(0)
      images.cancel()
      await running
      await vi.advanceTimersByTimeAsync(10_000)

      expect(client.postImage).toHaveBeenCalledOnce()
      expect(images.job.value).toBeNull()
      expect(images.error.value).toBeNull()
      expect(images.results.value).toHaveLength(0)
    })
  })

  it('drops a result that arrives after another image was opened', async () => {
    const images = await load()
    await images.open(png('first.png'), 'first.png')
    const finish = pendingJob()
    const running = images.run(plan)
    await vi.waitFor(() => expect(client.postImage).toHaveBeenCalled())

    await images.open(png('second.png'), 'second.png')
    finish()
    await running

    expect(images.image.value?.name).toBe('second.png')
    expect(images.results.value).toHaveLength(0)
    expect(images.error.value).toBeNull()
  })

  it('forgets a key the server rejects, and asks for one', async () => {
    const images = await load()
    const { useApiKey } = await import('./useApiKey')
    const apiKey = useApiKey()
    apiKey.save('wrong', false)
    await images.open(png('photo.png'), 'photo.png')
    client.uploadImage.mockRejectedValueOnce(new ApiError('Unauthorized', 401))

    await images.run(plan)

    expect(client.postImage).not.toHaveBeenCalled()
    expect(apiKey.key.value).toBeNull()
    expect(apiKey.rejected.value).toBe(true)
    expect(images.error.value).toEqual({
      message: 'The server needs a valid API key. Enter it, then try again.', status: 401, canRetry: true,
    })
  })

  it.each([
    [new ApiError('The server could not be reached. Check that Image Mason is running.', 0),
      'The server could not be reached. Check that Image Mason is running.', true],
    [new ApiError('Payload Too Large', 413), 'The file is larger than this server accepts.', false],
    [new ApiError('The image needs more memory than this server has.', 422), 'The image needs more memory than this server has.', false],
    [new ApiError('Gateway Timeout', 504),
      'The server stopped after its seconds. A smaller size or a lower quality preset finishes sooner.', true],
    [new ApiError('Internal Server Error', 500), 'Internal Server Error', true],
  ])('explains a %s', async (failure, message, canRetry) => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    client.postImage.mockRejectedValueOnce(failure)

    await images.run(plan)

    expect(images.error.value).toEqual({ message, status: failure.status, canRetry })
    expect(images.job.value).toBeNull()
  })

  it.each([
    ['empty.png', bytes('empty.png'), '“empty.png” is empty.'],
    ['phone.heic', bytes('phone.heic', 0, 0, 0, 24, ...[...'ftypheic'].map(c => c.charCodeAt(0))),
      '“phone.heic” is a HEIC image, which Image Mason doesn\'t read. Export it as JPEG first.'],
    ['notes.txt', bytes('notes.txt', ...[...'hello, world'].map(c => c.charCodeAt(0))),
      '“notes.txt” isn\'t a PNG, WebP, AVIF, JPEG or JPEG XL image.'],
  ])('says why %s can\'t be opened', async (name, file, message) => {
    const images = await load()

    expect(await images.open(file, name)).toBe(false)
    expect(images.openError.value).toBe(message)
    expect(images.image.value).toBeNull()
  })

  it('says when the browser can\'t display an image', async () => {
    const images = await load()
    vi.spyOn(HTMLImageElement.prototype, 'src', 'set').mockImplementation(function (this: HTMLImageElement) {
      setTimeout(() => this.onerror?.(new Event('error')))
    })

    expect(await images.open(png('broken.png'), 'broken.png')).toBe(false)
    expect(images.openError.value).toBe('Your browser couldn\'t display “broken.png”. The file may be damaged.')
    expect(URL.revokeObjectURL).toHaveBeenCalled()
  })

  it('dismisses a job\'s error and an open\'s error separately', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    client.postImage.mockRejectedValueOnce(new ApiError('Internal Server Error', 500))
    await images.run(plan)
    await images.open(bytes('empty.png'), 'empty.png')

    images.dismissOpenError()
    expect(images.openError.value).toBeNull()
    expect(images.error.value?.status).toBe(500)

    images.dismissError()
    expect(images.error.value).toBeNull()
  })
})
