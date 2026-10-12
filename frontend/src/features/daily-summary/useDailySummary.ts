import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { isApiError } from '../../api/apiError'
import { getDailySummary } from './api'

/** Matches the backend cache: data can't change faster than this anyway. */
const STALE_TIME_MS = 5 * 60 * 1000

/**
 * The backend owns the retry budget against the upstream (and already retried before returning
 * 503), so the client retries once, and only for gateway errors/timeouts — never 4xx or 503.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  return (
    failureCount < 1 &&
    isApiError(error) &&
    error.kind === 'http' && // an answer from our API, not a proxy saying the API is down
    (error.status === 502 || error.status === 504)
  )
}

export function useDailySummary(symbol: string | null) {
  return useQuery({
    queryKey: ['daily-summary', symbol],
    queryFn: ({ signal }) => getDailySummary(symbol as string, signal),
    enabled: symbol !== null,
    staleTime: STALE_TIME_MS,
    retry: shouldRetry,
    // Keep the previous symbol's chart on screen (dimmed) while the next one loads — no flash.
    placeholderData: keepPreviousData,
  })
}
