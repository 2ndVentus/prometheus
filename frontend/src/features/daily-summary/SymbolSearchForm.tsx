import { useId, useState, type FormEvent } from 'react'
import { EXAMPLE_SYMBOLS, isValidSymbol, normalizeSymbol, SYMBOL_MAX_LENGTH } from './symbol'

interface SymbolSearchFormProps {
  initialSymbol: string
  isLoading: boolean
  onSearch: (symbol: string) => void
}

export function SymbolSearchForm({ initialSymbol, isLoading, onSearch }: SymbolSearchFormProps) {
  const [value, setValue] = useState(initialSymbol)
  const [error, setError] = useState<string | null>(null)
  const inputId = useId()
  const hintId = useId()
  const errorId = useId()

  const search = (raw: string) => {
    const symbol = normalizeSymbol(raw)
    if (!isValidSymbol(symbol)) {
      setError(
        symbol === ''
          ? 'Enter a ticker symbol.'
          : `Use 1–${SYMBOL_MAX_LENGTH} letters, digits or . - ^ =, with at least one letter or digit (e.g. TSLA, BRK-B, ^GSPC).`,
      )
      return
    }
    setError(null)
    setValue(symbol)
    onSearch(symbol)
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    search(value)
  }

  return (
    // Stays usable while loading: a new search supersedes (and cancels) a slow one.
    <form
      aria-label="Search"
      aria-busy={isLoading}
      onSubmit={handleSubmit}
      noValidate
      className="flex flex-col gap-3"
    >
      <label htmlFor={inputId} className="text-ink-secondary text-sm font-medium">
        Ticker symbol
      </label>
      <div className="flex gap-2">
        <input
          id={inputId}
          name="symbol"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          maxLength={SYMBOL_MAX_LENGTH + 10}
          placeholder="e.g. TSLA"
          aria-invalid={error !== null}
          aria-describedby={error !== null ? errorId : hintId}
          className="border-border bg-surface placeholder:text-ink-muted focus-visible:ring-accent aria-[invalid=true]:border-critical min-w-0 flex-1 rounded-lg border px-3 py-2 text-base tracking-wide uppercase outline-none placeholder:tracking-normal placeholder:normal-case focus-visible:ring-2"
        />
        <button
          type="submit"
          className="bg-accent hover:bg-accent-strong focus-visible:ring-accent flex items-center gap-2 rounded-lg px-4 py-2 font-medium text-white transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
        >
          {isLoading && (
            <span
              aria-hidden
              className="size-3.5 animate-spin rounded-full border-2 border-white/40 border-t-white"
            />
          )}
          Show
        </button>
      </div>
      {error !== null ? (
        <p id={errorId} className="text-critical text-sm">
          {error}
        </p>
      ) : (
        <p id={hintId} className="text-ink-muted text-sm">
          Stocks, indices and FX: use Yahoo tickers like BRK-B, ^GSPC or VOD.L.
        </p>
      )}
      <fieldset className="flex flex-wrap items-center gap-2">
        <legend className="sr-only">Example symbols</legend>
        <span aria-hidden className="text-ink-muted text-sm">
          Try
        </span>
        {EXAMPLE_SYMBOLS.map((example) => (
          <button
            key={example}
            type="button"
            onClick={() => search(example)}
            className="border-border text-ink-secondary hover:border-accent hover:text-ink focus-visible:ring-accent rounded-full border px-3 py-1 text-sm transition-colors focus-visible:ring-2 focus-visible:outline-none"
          >
            {example}
          </button>
        ))}
      </fieldset>
    </form>
  )
}
