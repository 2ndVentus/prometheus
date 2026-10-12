import { getJson } from '../../api/httpClient'
import { dailySummariesSchema, type DailySummary } from './schema'

export function getDailySummary(symbol: string, signal?: AbortSignal): Promise<DailySummary[]> {
  // encodeURIComponent matters: ^GSPC and EURUSD=X must survive as a single path segment.
  return getJson(
    `/api/v1/stocks/${encodeURIComponent(symbol)}/daily-summary`,
    dailySummariesSchema,
    signal,
  )
}
