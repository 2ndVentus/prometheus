import { exchangeLabel, instrumentTypeLabel } from './labels'
import type { Instrument } from './schema'

interface InstrumentHeaderProps {
  symbol: string
  instrument: Instrument | undefined
  loading: boolean
}

/** Name as the heading, with ticker · exchange · type above it. Falls back to the ticker alone. */
export function InstrumentHeader({ symbol, instrument, loading }: InstrumentHeaderProps) {
  const name = instrument?.longName ?? null
  const facts = [
    symbol,
    exchangeLabel(instrument?.exchangeName ?? null, instrument?.instrumentType ?? null),
    instrumentTypeLabel(instrument?.instrumentType ?? null),
  ].filter((fact): fact is string => fact !== null)

  return (
    <div className="min-w-0">
      {name !== null && (
        <p className="text-ink-muted text-sm font-medium tracking-wide">{facts.join(' · ')}</p>
      )}
      <h2 id="results-heading" className="text-xl font-semibold break-words">
        {loading ? `Loading ${symbol}…` : (name ?? symbol)}
      </h2>
      {name === null && facts.length > 1 && (
        <p className="text-ink-muted text-sm">{facts.slice(1).join(' · ')}</p>
      )}
    </div>
  )
}
