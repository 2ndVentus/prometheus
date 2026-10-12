import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { App } from './App'
import { renderWithClient } from './test/render'
import { DAILY_SUMMARY_URL, problem, server } from './test/server'

function renderAt(search = '') {
  window.history.replaceState(null, '', `/${search}`)
  return { user: userEvent.setup(), ...renderWithClient(<App />) }
}

describe('App', () => {
  it('prompts for a ticker when none is selected', () => {
    renderAt()

    expect(
      screen.getByRole('heading', { level: 1, name: /intraday daily summary/i }),
    ).toBeInTheDocument()
    expect(screen.getByText(/enter a ticker to see/i)).toBeInTheDocument()
  })

  it('searches, shows results and puts the symbol in the URL', async () => {
    const { user } = renderAt()

    await user.type(screen.getByLabelText('Ticker symbol'), 'tsla{Enter}')

    expect(await screen.findByRole('heading', { name: 'TSLA — last month' })).toBeInTheDocument()
    expect(window.location.search).toBe('?symbol=TSLA')
  })

  it('loads the symbol from the URL and lists days newest first without shifting dates', async () => {
    // Tests run in America/Los_Angeles; "2026-10-09" must still read as Oct 9.
    renderAt('?symbol=TSLA')

    const table = await screen.findByRole('table', { name: /daily summary for tsla/i })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(within(rows[0]!).getByRole('rowheader')).toHaveTextContent('Fri, Oct 9, 2026')
    expect(within(rows[1]!).getByRole('rowheader')).toHaveTextContent('Thu, Oct 8, 2026')
    expect(within(rows[0]!).getByText('383.0768')).toBeInTheDocument()
    expect(within(rows[0]!).getByText('36,336,060')).toBeInTheDocument()
  })

  it('shows summary statistics', async () => {
    renderAt('?symbol=TSLA')

    const tile = (await screen.findByText('Highest high average')).parentElement!
    expect(within(tile).getByText('384.9723')).toBeInTheDocument()
    expect(within(tile).getByText('Oct 9')).toBeInTheDocument()
  })

  it('ignores an invalid symbol in the URL', () => {
    renderAt('?symbol=%3Cscript%3E')

    expect(screen.getByText(/enter a ticker to see/i)).toBeInTheDocument()
  })

  it('shows a loading state', async () => {
    renderAt('?symbol=TSLA')

    expect(screen.getByRole('status')).toHaveTextContent('Loading TSLA…')
    await screen.findByRole('table')
  })

  it('explains an unknown symbol without offering a retry', async () => {
    server.use(http.get(DAILY_SUMMARY_URL, () => problem(404, 'Symbol not found')))
    renderAt('?symbol=BRK.B')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No data for BRK.B')
    expect(alert).toHaveTextContent('BRK-B')
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows when to retry if the provider is busy, and recovers on retry', async () => {
    let calls = 0
    server.use(
      http.get(DAILY_SUMMARY_URL, () =>
        ++calls === 1
          ? problem(503, 'Busy', { headers: { 'Retry-After': '7' } })
          : HttpResponse.json([{ day: '2026-10-09', lowAverage: 1, highAverage: 2, volume: 3 }]),
      ),
    )
    const { user } = renderAt('?symbol=TSLA')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Try again in 7 seconds.')
    await user.click(within(alert).getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it('reports an unreachable server', async () => {
    server.use(http.get(DAILY_SUMMARY_URL, () => HttpResponse.error()))
    renderAt('?symbol=TSLA')

    expect(await screen.findByRole('alert')).toHaveTextContent("Can't reach the server")
  })

  it('says the server is unreachable when the dev proxy answers 502 for a stopped backend', async () => {
    let calls = 0
    server.use(
      http.get(DAILY_SUMMARY_URL, () => {
        calls += 1
        return new HttpResponse(null, { status: 502 })
      }),
    )
    renderAt('?symbol=TSLA')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent("Can't reach the server")
    expect(alert).not.toHaveTextContent('Market data')
    expect(calls).toBe(1)
  })

  it('notes a short history, e.g. a newly listed symbol', async () => {
    renderAt('?symbol=TSLA') // default handler returns 2 days

    expect(await screen.findByText(/only 2 trading days of data/i)).toBeInTheDocument()
  })

  it('does not note a full month of data', async () => {
    const days = Array.from({ length: 20 }, (_, i) => ({
      day: `2026-09-${String(i + 1).padStart(2, '0')}`,
      lowAverage: 100,
      highAverage: 101,
      volume: 1000,
    }))
    server.use(http.get(DAILY_SUMMARY_URL, () => HttpResponse.json(days)))
    renderAt('?symbol=TSLA')

    await screen.findByRole('table')
    expect(screen.queryByText(/trading days? of data/i)).not.toBeInTheDocument()
  })

  it('shows an empty state when there were no trading days', async () => {
    server.use(http.get(DAILY_SUMMARY_URL, () => HttpResponse.json([])))
    renderAt('?symbol=NEWIPO')

    expect(
      await screen.findByText('No trading data for NEWIPO in the last month.'),
    ).toBeInTheDocument()
  })

  it('follows browser back navigation', async () => {
    const { user } = renderAt('?symbol=TSLA')
    await screen.findByRole('heading', { name: 'TSLA — last month' })

    await user.click(screen.getByRole('button', { name: 'AAPL' }))
    await screen.findByRole('heading', { name: 'AAPL — last month' })
    window.history.back()

    await waitFor(() => expect(window.location.search).toBe('?symbol=TSLA'))
    expect(await screen.findByRole('heading', { name: 'TSLA — last month' })).toBeInTheDocument()
    expect(screen.getByLabelText('Ticker symbol')).toHaveValue('TSLA')
  })
})
