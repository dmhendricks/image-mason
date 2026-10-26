// Trying a request again while the server is busy: it answers 503, with Retry-After, when its job queue is full.

import { ApiError } from './client'

/** Tries for a busy server (503) before giving up. */
export const maxAttempts = 3

/**
 * Calls send (with the try's number, from 1) until it succeeds or fails for another reason. After a 503 it waits as
 * the server asked (Retry-After, else 2 then 4 seconds; at most a minute), telling onWait the next try's number and
 * when it starts. Up to maxAttempts tries; aborting the signal ends a wait with an AbortError.
 */
export async function retryWhileBusy<T>(send: (attempt: number) => Promise<T>, signal: AbortSignal,
  onWait: (attempt: number, retryAt: number) => void): Promise<T> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await send(attempt)
    } catch (failure) {
      if (!(failure instanceof ApiError && failure.status === 503 && attempt < maxAttempts))
        throw failure
      const seconds = Math.min(failure.retryAfter ?? 2 ** attempt, 60)
      onWait(attempt + 1, Date.now() + seconds * 1000)
      await delay(seconds * 1000, signal)
    }
  }
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(new DOMException('The request was cancelled.', 'AbortError'))
    }
    signal.addEventListener('abort', onAbort, { once: true })
  })
}
