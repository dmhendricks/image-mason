/** A failed request, carrying the server's own explanation when it sent one (RFC 9457 Problem Details). */
export class ApiError extends Error {
  /** The HTTP status, or 0 when the server couldn't be reached. */
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

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
    throw new ApiError('The server could not be reached. Check that Image Mason is running.', 0)
  }

  if (!response.ok)
    throw new ApiError(await problemMessage(response), response.status)

  return await response.json() as T
}

async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = await response.json() as { detail?: unknown, title?: unknown }
    if (typeof problem.detail === 'string' && problem.detail)
      return problem.detail
    if (typeof problem.title === 'string' && problem.title)
      return problem.title
  } catch {
    // Not JSON: fall back to the status line.
  }
  return `The server answered ${response.status} ${response.statusText}`.trimEnd() + '.'
}
