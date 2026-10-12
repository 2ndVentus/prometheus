import { useCallback, useSyncExternalStore } from 'react'
import { isValidSymbol, normalizeSymbol } from './symbol'

const PARAM = 'symbol'

function subscribe(onChange: () => void): () => void {
  window.addEventListener('popstate', onChange)
  return () => window.removeEventListener('popstate', onChange)
}

function readSymbol(): string | null {
  const raw = new URLSearchParams(window.location.search).get(PARAM)
  if (raw === null) return null
  const symbol = normalizeSymbol(raw)
  return isValidSymbol(symbol) ? symbol : null
}

/**
 * The selected symbol lives in the URL (`?symbol=TSLA`) so results are shareable, bookmarkable,
 * and survive refresh and back/forward. A router would be overkill for one view.
 */
export function useSymbolParam(): [string | null, (symbol: string) => void] {
  const symbol = useSyncExternalStore(subscribe, readSymbol, () => null)

  const setSymbol = useCallback((next: string) => {
    const url = new URL(window.location.href)
    url.searchParams.set(PARAM, next)
    window.history.pushState(null, '', url)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, [])

  return [symbol, setSymbol]
}
