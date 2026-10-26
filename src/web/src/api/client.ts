/** A failed request, carrying the server's own explanation when it sent one (RFC 9457 Problem Details). */
export class ApiError extends Error {
  /** The HTTP status, or 0 when the server couldn't be reached. */
  readonly status: number
  /** Seconds the server asked to wait before trying again (Retry-After), when it said. */
  readonly retryAfter: number | null

  constructor(message: string, status: number, retryAfter: number | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.retryAfter = retryAfter
  }
}

const unreachable = 'The server could not be reached. Check that Image Mason is running.'

/**
 * GETs JSON from the API.
 * @throws {ApiError} The server couldn't be reached or answered with an error.
 * @throws {DOMException} The request was aborted through init.signal ("AbortError").
 */
export async function getJson<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, { ...init, headers: { Accept: 'application/json', ...init?.headers } })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError')
      throw error
    throw new ApiError(unreachable, 0)
  }

  if (!response.ok)
    throw new ApiError(problemMessage(await response.text(), response.status, response.statusText), response.status)

  return await response.json() as T
}

/**
 * POSTs to the API and returns the file it answers with (POST /api/v1/fetch).
 * @throws {ApiError} The server couldn't be reached or answered with an error.
 * @throws {DOMException} The request was aborted through init.signal ("AbortError").
 */
export async function postForBlob(path: string, init?: RequestInit): Promise<Blob> {
  let response: Response
  try {
    response = await fetch(path, { ...init, method: 'POST' })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError')
      throw error
    throw new ApiError(unreachable, 0)
  }

  if (!response.ok)
    throw new ApiError(problemMessage(await response.text(), response.status, response.statusText), response.status)

  return await response.blob()
}

export interface PostImageOptions {
  headers?: Record<string, string>
  signal?: AbortSignal
  /** Called as the body uploads, with the fraction sent (0 to 1). */
  onUploadProgress?: (fraction: number) => void
  /** Called once the whole body is sent and the server is working on it. */
  onUploaded?: () => void
}

export interface ImageResponse {
  blob: Blob
  header: (name: string) => string | null
}

/**
 * POSTs an image as the raw request body, as the API expects, and returns the image it sends back. Uses
 * XMLHttpRequest rather than fetch, for upload progress. A null body sends nothing, for a path that names the image
 * (source or url); onUploaded is then called right away.
 * @throws {ApiError} The server couldn't be reached or answered with an error.
 * @throws {DOMException} The request was aborted through options.signal ("AbortError").
 */
export function postImage(path: string, body: Blob | null, options: PostImageOptions = {}): Promise<ImageResponse> {
  return new Promise((resolve, reject) => {
    const aborted = () => new DOMException('The request was cancelled.', 'AbortError')
    if (options.signal?.aborted) {
      reject(aborted())
      return
    }

    const xhr = new XMLHttpRequest()
    xhr.open('POST', path)
    xhr.responseType = 'blob'
    for (const [name, value] of Object.entries(options.headers ?? {}))
      xhr.setRequestHeader(name, value)
    if (body?.type)
      xhr.setRequestHeader('Content-Type', body.type)

    const onAbort = () => xhr.abort()
    options.signal?.addEventListener('abort', onAbort, { once: true })
    const settle = () => options.signal?.removeEventListener('abort', onAbort)

    xhr.upload.onprogress = event => {
      if (event.lengthComputable && event.total > 0)
        options.onUploadProgress?.(event.loaded / event.total)
    }
    xhr.upload.onload = () => options.onUploaded?.()
    xhr.onabort = () => {
      settle()
      reject(aborted())
    }
    xhr.onerror = () => {
      settle()
      reject(new ApiError(unreachable, 0))
    }
    xhr.onload = async () => {
      settle()
      const blob = xhr.response as Blob
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve({ blob, header: name => xhr.getResponseHeader(name) })
        return
      }
      const text = await blob.text().catch(() => '')
      const retryAfter = parseRetryAfter(xhr.getResponseHeader('Retry-After'), Date.now())
      reject(new ApiError(problemMessage(text, xhr.status, xhr.statusText), xhr.status, retryAfter))
    }

    xhr.send(body)
    if (!body)
      options.onUploaded?.()
  })
}

/**
 * Stores an image on the server (POST /api/v1/upload) and returns its name, which the job endpoints take as source.
 * A null body with ?url= in the path has the server download it instead.
 * @throws {ApiError} The server couldn't be reached or answered with an error.
 * @throws {DOMException} The request was aborted through options.signal ("AbortError").
 */
export async function uploadImage(path: string, body: Blob | null, options: PostImageOptions = {}): Promise<string> {
  const response = await postImage(path, body, options)
  let output: unknown
  try {
    output = (JSON.parse(await response.blob.text()) as { output?: unknown }).output
  } catch {
    output = null
  }
  if (typeof output !== 'string' || !output.includes('/images/'))
    throw new ApiError('The server stored the image but didn\'t say where.', 500)
  return output.slice(output.lastIndexOf('/') + 1)
}

/** Deletes a stored image (DELETE /images/{name}). Best effort: it expires on its own if this fails. */
export async function deleteStoredImage(name: string, headers: Record<string, string>): Promise<void> {
  try {
    await fetch(`/images/${encodeURIComponent(name)}`, { method: 'DELETE', headers })
  } catch {
    // Unreachable: the image expires on its own.
  }
}

/** Retry-After in seconds, from either form it takes: a number of seconds or an HTTP date. */
export function parseRetryAfter(value: string | null, now: number): number | null {
  if (!value)
    return null
  if (/^\d+$/.test(value.trim()))
    return Number(value.trim())
  const date = Date.parse(value)
  return Number.isNaN(date) ? null : Math.max(0, Math.ceil((date - now) / 1000))
}

/** The Problem Details detail or title, or else the status line. */
function problemMessage(body: string, status: number, statusText: string): string {
  try {
    const problem = JSON.parse(body) as { detail?: unknown, title?: unknown }
    if (typeof problem.detail === 'string' && problem.detail)
      return problem.detail
    if (typeof problem.title === 'string' && problem.title)
      return problem.title
  } catch {
    // Not JSON: fall back to the status line.
  }
  return `The server answered ${status} ${statusText}`.trimEnd() + '.'
}
