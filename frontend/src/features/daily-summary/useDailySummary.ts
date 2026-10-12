import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { shouldRetry, STALE_TIME_MS } from '../../api/retryPolicy'
import { getDailySummary } from './api'

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
