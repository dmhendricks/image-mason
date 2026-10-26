import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ImageResponse } from '../api/client'
import type { JobPlan } from '../jobs'

const client = vi.hoisted(() => ({
  // The server's limits never arrive, so the size checks are skipped.
  getJson: vi.fn(() => new Promise(() => {})),
  postForBlob: vi.fn<(path: string, init?: RequestInit) => Promise<Blob>>(),
  postImage: vi.fn<(path: string, body: Blob) => Promise<ImageResponse>>(),
}))

vi.mock('../api/client', async importOriginal => ({
  ...await importOriginal<typeof import('../api/client')>(),
  ...client,
}))

const png = (name: string) =>
  new File([new Uint8Array([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])], name, { type: 'image/png' })

const plan = { path: '/api/v1/optimize?quality=high', output: null, format: null, errors: {}, action: 'Optimize', working: 'Optimizing', label: 'High' } satisfies JobPlan

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
  return useImage()
}

beforeEach(() => {
  // jsdom has no object URLs and doesn't decode images.
  let objectUrls = 0
  URL.createObjectURL = vi.fn(() => `blob:${++objectUrls}`)
  URL.revokeObjectURL = vi.fn()
  HTMLImageElement.prototype.decode = () => Promise.resolve()
  vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(40)
  vi.spyOn(HTMLImageElement.prototype, 'naturalHeight', 'get').mockReturnValue(30)
  client.postImage.mockImplementation(async () =>
    ({ blob: new Blob(['result'], { type: 'image/png' }), header: () => null }))
})

afterEach(() => {
  vi.restoreAllMocks()
  client.postForBlob.mockReset()
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

  it('keeps results made with different settings', async () => {
    const images = await load()
    await images.open(png('photo.png'), 'photo.png')
    await images.run(plan)
    await images.run({ ...plan, path: '/api/v1/optimize?quality=low', label: 'Low' })

    expect(images.results.value.map(r => r.label)).toEqual(['High', 'Low'])
  })
})
