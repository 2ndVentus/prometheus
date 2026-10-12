import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { DailySummary } from '../features/daily-summary/schema'

export const DAILY_SUMMARY_URL = '/api/v1/stocks/:symbol/daily-summary'

export const TSLA_DAYS: DailySummary[] = [
  { day: '2026-10-08', lowAverage: 371.4284, highAverage: 372.9249, volume: 22329809 },
  { day: '2026-10-09', lowAverage: 383.0768, highAverage: 384.9723, volume: 36336060 },
]

/** Intercepts fetch at the network layer, so tests exercise the real client → zod → query → UI path. */
export const server = setupServer(http.get(DAILY_SUMMARY_URL, () => HttpResponse.json(TSLA_DAYS)))

export function problem(
  status: number,
  title: string,
  init: { detail?: string; headers?: Record<string, string> } = {},
) {
  return HttpResponse.json(
    { type: 'about:blank', title, status, detail: init.detail },
    { status, headers: { 'Content-Type': 'application/problem+json', ...init.headers } },
  )
}
