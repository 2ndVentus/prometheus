import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import type { ApiError } from '../../api/apiError'
import { createTestQueryClient } from '../../test/render'
import { DAILY_SUMMARY_URL, problem, server, TSLA_DAYS } from '../../test/server'
import { useDailySummary } from './useDailySummary'

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
