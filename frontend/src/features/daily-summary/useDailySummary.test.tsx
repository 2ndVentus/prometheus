import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import { ApiError } from '../../api/apiError'
import { createTestQueryClient } from '../../test/render'
import { DAILY_SUMMARY_URL, problem, server, TSLA_DAYS } from '../../test/server'
import { shouldRetry, useDailySummary } from './useDailySummary'

function renderUseDailySummary(symbol: string | null) {
  const client = createTestQueryClient()
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  return renderHook(() => useDailySummary(symbol), { wrapper })
}

function countingHandler(responses: Array<() => Response>) {
  let calls = 0
  server.use(
    http.get(DAILY_SUMMARY_URL, () => {
      const respond = responses[Math.min(calls, responses.length - 1)]!
      calls += 1
      return respond()
    }),
  )
  return () => calls
}

describe('shouldRetry', () => {
  const error = (status: number) => new ApiError('http', 'x', { status })

  it.each([502, 504])('retries %s once', (status) => {
    expect(shouldRetry(0, error(status))).toBe(true)
    expect(shouldRetry(1, error(status))).toBe(false)
  })

  it.each([400, 404, 429, 503, 500])('does not retry %s', (status) => {
    expect(shouldRetry(0, error(status))).toBe(false)
  })

  it('does not retry a proxy 502 (our API is down, retrying will not help)', () => {
    expect(shouldRetry(0, new ApiError('network', 'x', { status: 502 }))).toBe(false)
  })

  it('does not retry network or contract failures', () => {
    expect(shouldRetry(0, new ApiError('network', 'x'))).toBe(false)
    expect(shouldRetry(0, new Error('x'))).toBe(false)
  })
})

describe('useDailySummary', () => {
  it('does not fetch without a symbol', () => {
    const { result } = renderUseDailySummary(null)

    expect(result.current.fetchStatus).toBe('idle')
  })

  it('loads and validates data', async () => {
    const { result } = renderUseDailySummary('TSLA')

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data).toEqual(TSLA_DAYS)
  })

  it('requests the URL-encoded symbol', async () => {
    let requested = ''
    server.use(
      http.get(DAILY_SUMMARY_URL, ({ request }) => {
        requested = new URL(request.url).pathname
        return HttpResponse.json([])
      }),
    )
    const { result } = renderUseDailySummary('^GSPC')

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(requested).toBe('/api/v1/stocks/%5EGSPC/daily-summary')
  })

  it('retries a 502 once and recovers', async () => {
    const calls = countingHandler([
      () => problem(502, 'Bad gateway'),
      () => HttpResponse.json(TSLA_DAYS),
    ])
    const { result } = renderUseDailySummary('TSLA')

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(calls()).toBe(2)
  })

  it.each([404, 503])('does not retry %s', async (status) => {
    const calls = countingHandler([() => problem(status, 'Nope')])
    const { result } = renderUseDailySummary('TSLA')

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(calls()).toBe(1)
    expect((result.current.error as ApiError).status).toBe(status)
  })
})
