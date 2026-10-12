import { useEffect } from 'react'
import { DailySummaryView } from './features/daily-summary/DailySummaryView'
import { SymbolSearchForm } from './features/daily-summary/SymbolSearchForm'
import { useDailySummary } from './features/daily-summary/useDailySummary'
import { useSymbolParam } from './features/daily-summary/useSymbolParam'

const APP_TITLE = 'Intraday Daily Summary'

export function App() {
  const [symbol, setSymbol] = useSymbolParam()

  // Tabs, history and bookmarks show which symbol a page is about.
  useEffect(() => {
    document.title = symbol ? `${symbol} · ${APP_TITLE}` : APP_TITLE
  }, [symbol])
  const { isFetching } = useDailySummary(symbol)

  return (
    <main className="mx-auto flex min-h-screen max-w-5xl flex-col gap-8 px-4 py-10">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Intraday Daily Summary</h1>
        <p className="text-ink-secondary mt-1">
          Last month of 15-minute market data, summarised per trading day.
        </p>
      </header>
      {/* Keyed by symbol so back/forward navigation resets the input to the URL's symbol. */}
      <SymbolSearchForm
        key={symbol ?? ''}
        initialSymbol={symbol ?? ''}
        isLoading={isFetching}
        onSearch={setSymbol}
      />
      <DailySummaryView symbol={symbol} />
    </main>
  )
}
