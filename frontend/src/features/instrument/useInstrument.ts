import { useQuery } from '@tanstack/react-query'
import { shouldRetry, STALE_TIME_MS } from '../../api/retryPolicy'
import { getInstrument } from './api'
import type { Instrument } from './schema'

/**
 * Instrument details for the header. The backend serves these from the same cached upstream call
 * as the daily summary, so requesting both in parallel costs one upstream request.
 *
 * Returns undefined until details for *this* symbol are available (or if they fail): metadata is an
 * enhancement and never blocks or replaces the summary.
 */
export function useInstrument(symbol: string | null): Instrument | undefined {
  const query = useQuery({
    queryKey: ['instrument', symbol],
    queryFn: ({ signal }) => getInstrument(symbol as string, signal),
    enabled: symbol !== null,
    staleTime: STALE_TIME_MS,
    retry: shouldRetry,
  })
  return query.data?.symbol === symbol ? query.data : undefined
}
