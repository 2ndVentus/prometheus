import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { DailySummary } from '../features/daily-summary/schema'
import type { Instrument } from '../features/instrument/schema'

export const DAILY_SUMMARY_URL = '/api/v1/stocks/:symbol/daily-summary'
export const INSTRUMENT_URL = '/api/v1/stocks/:symbol'

export const TSLA_DAYS: DailySummary[] = [
  { day: '2026-10-08', lowAverage: 371.4284, highAverage: 372.9249, volume: 22329809 },
  { day: '2026-10-09', lowAverage: 383.0768, highAverage: 384.9723, volume: 36336060 },
]

export const TSLA_INSTRUMENT: Instrument = {
  symbol: 'TSLA',
  longName: 'Tesla, Inc.',
  currency: 'USD',
  exchangeName: 'NasdaqGS',
  instrumentType: 'EQUITY',
}

/** Details for TSLA; any other symbol comes back with nothing known (all nulls). */
export function instrumentFor(symbol: string): Instrument {
  return symbol === 'TSLA'
    ? TSLA_INSTRUMENT
    : { symbol, longName: null, currency: null, exchangeName: null, instrumentType: null }
}

/** Intercepts fetch at the network layer, so tests exercise the real client → zod → query → UI path. */
export const server = setupServer(
  http.get(DAILY_SUMMARY_URL, () => HttpResponse.json(TSLA_DAYS)),
  http.get(INSTRUMENT_URL, ({ params }) => HttpResponse.json(instrumentFor(String(params.symbol)))),
)

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
