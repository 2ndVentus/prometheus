import { lazy, Suspense, type ReactNode } from 'react'
import { ErrorBoundary } from '../../components/ErrorBoundary'
import { DailySummaryTable } from './DailySummaryTable'
import { describeError } from './errorMessages'
import { SummaryStats } from './SummaryStats'
import { useDailySummary } from './useDailySummary'

// Recharts is most of the bundle; load it on demand so the form and table appear immediately.
const DailySummaryChart = lazy(() =>
  import('./DailySummaryChart').then((module) => ({ default: module.DailySummaryChart })),
)

/**
 * A month is ~20–23 trading days (fewer around holidays — Tokyo had 19 in Sep–Oct 2026). Well
 * below that, the "last month" heading would mislead without a note. The API has no listing date,
 * so the note can't say which reason applies.
 */
const SHORT_HISTORY_DAYS = 15

export function DailySummaryView({ symbol }: { symbol: string | null }) {
  const query = useDailySummary(symbol)

  if (symbol === null) {
    return (
      <Panel>
        <p className="text-ink-secondary">
          Enter a ticker to see its daily low/high averages and volume for the last month.
        </p>
      </Panel>
    )
  }

  if (query.isError) {
    const message = describeError(query.error, symbol)
    return (
      <div role="alert" className="border-critical/40 bg-critical-wash rounded-xl border p-5">
        <p className="font-semibold">{message.title}</p>
        <p className="text-ink-secondary mt-1">{message.detail}</p>
        {message.retryable && (
          <button
            type="button"
            onClick={() => void query.refetch()}
            disabled={query.isFetching}
            className="border-border bg-surface hover:border-accent focus-visible:ring-accent mt-3 rounded-lg border px-3 py-1.5 text-sm font-medium focus-visible:ring-2 focus-visible:outline-none disabled:opacity-60"
          >
            {query.isFetching ? 'Retrying…' : 'Try again'}
          </button>
        )}
      </div>
    )
  }

  if (query.isPending) {
    return (
      <Panel>
        <output className="text-ink-secondary flex items-center gap-3">
          <span
            aria-hidden
            className="border-hairline border-t-accent size-4 animate-spin rounded-full border-2"
          />
          Loading {symbol}…
        </output>
      </Panel>
    )
  }

  const data = query.data
  // While a new symbol loads, the previous result stays on screen (dimmed) instead of flashing a spinner.
  const showingPrevious = query.isPlaceholderData

  if (data.length === 0) {
    return (
      <Panel>
        <p className="text-ink-secondary">No trading data for {symbol} in the last month.</p>
      </Panel>
    )
  }

  return (
    <section
      aria-labelledby="results-heading"
      aria-busy={showingPrevious}
      className={`flex flex-col gap-4 transition-opacity ${showingPrevious ? 'opacity-50' : ''}`}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="results-heading" className="text-xl font-semibold">
          {showingPrevious ? `Loading ${symbol}…` : `${symbol} — last month`}
        </h2>
        <p className="text-ink-muted text-sm">Days are the exchange's local trading days</p>
      </div>
      {!showingPrevious && data.length < SHORT_HISTORY_DAYS && (
        <p className="border-border bg-surface text-ink-secondary rounded-lg border px-4 py-2 text-sm">
          Only {data.length} trading day{data.length === 1 ? '' : 's'} of data — fewer than a
          typical month. The symbol may be newly listed, or trading may have been suspended.
        </p>
      )}
      <SummaryStats data={data} />
      {/* Keyed by symbol so a chart failure for one ticker doesn't stick for the next. */}
      <ErrorBoundary key={symbol} fallback={<ChartUnavailable />}>
        <Suspense fallback={<ChartPlaceholder />}>
          <DailySummaryChart symbol={symbol} data={data} />
        </Suspense>
      </ErrorBoundary>
      <DailySummaryTable symbol={symbol} data={data} />
      <p className="text-ink-muted text-xs">
        Averages of 15-minute bar lows and highs, in the instrument's trading currency. The most
        recent day may be incomplete during market hours. Source: Yahoo Finance.
      </p>
    </section>
  )
}

function Panel({ children }: { children: ReactNode }) {
  return <div className="border-border bg-surface rounded-xl border p-6">{children}</div>
}

/** Same footprint as the chart card so nothing jumps when it arrives. */
function ChartPlaceholder() {
  return <div aria-hidden className="border-border bg-surface h-[30rem] rounded-xl border" />
}

function ChartUnavailable() {
  return (
    <Panel>
      <p className="text-ink-secondary">
        The chart couldn't be displayed. All values are in the table below.
      </p>
    </Panel>
  )
}
