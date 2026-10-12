import { isApiError } from './apiError'

/**
 * The backend owns the retry budget against the upstream (and already retried before returning
 * 503), so the client retries once, and only for gateway errors/timeouts answered by our API —
 * never 4xx, 503, or a proxy reporting our API as down.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  return (
    failureCount < 1 &&
    isApiError(error) &&
    error.kind === 'http' &&
    (error.status === 502 || error.status === 504)
  )
}

/** Matches the backend cache: data can't change faster than this anyway. */
export const STALE_TIME_MS = 5 * 60 * 1000
