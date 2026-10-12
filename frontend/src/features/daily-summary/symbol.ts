/**
 * Mirrors StockSymbol on the backend so the form can give instant feedback. The backend remains
 * the authority — this is UX only.
 */
export const SYMBOL_MAX_LENGTH = 14

// At least one letter or digit: punctuation-only input (".", "..", "^") is never a ticker.
const SYMBOL_PATTERN = new RegExp(`^(?=.*[A-Z0-9])[A-Z0-9.\\-^=]{1,${SYMBOL_MAX_LENGTH}}$`)

export const normalizeSymbol = (input: string): string => input.trim().toUpperCase()

export const isValidSymbol = (symbol: string): boolean => SYMBOL_PATTERN.test(symbol)

export const EXAMPLE_SYMBOLS = ['TSLA', 'AAPL', '^GSPC', 'VOD.L', '7203.T'] as const
