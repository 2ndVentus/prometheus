/** Presentation of the provider's raw instrument details. */

const TYPE_LABELS: Record<string, string> = {
  EQUITY: 'Stock',
  ETF: 'ETF',
  INDEX: 'Index',
  MUTUALFUND: 'Mutual fund',
  CURRENCY: 'Currency pair',
  CRYPTOCURRENCY: 'Cryptocurrency',
  FUTURE: 'Future',
  OPTION: 'Option',
}

/** "EQUITY" → "Stock"; unknown types are shown sentence-cased rather than hidden. */
export function instrumentTypeLabel(type: string | null): string | null {
  if (!type) return null
  return TYPE_LABELS[type] ?? type.charAt(0) + type.slice(1).toLowerCase()
}

/**
 * Indices, FX and crypto don't trade on an exchange in a meaningful sense, and the provider reports
 * terse codes for them (^GSPC → "SNP", EURUSD=X → "CCY", BTC-USD → "CCC"), so only show the exchange
 * for exchange-traded instruments.
 */
const NON_EXCHANGE_TYPES = new Set(['INDEX', 'CURRENCY', 'CRYPTOCURRENCY'])

export function exchangeLabel(exchangeName: string | null, type: string | null): string | null {
  if (!exchangeName || (type !== null && NON_EXCHANGE_TYPES.has(type))) return null
  return exchangeName
}

/**
 * Some markets quote in a minor unit, signalled by a mixed-case code: London in pence ("GBp"),
 * Johannesburg in cents ("ZAc"), Tel Aviv in agorot ("ILA"). Showing these as the major currency
 * would overstate prices 100×.
 */
const MINOR_UNITS: Record<string, string> = {
  GBp: 'GBp — pence sterling',
  GBX: 'GBp — pence sterling',
  ZAc: 'ZAc — South African cents',
  ILA: 'ILA — Israeli agorot',
}

/** For column headers and notes: "USD", or "GBp — pence sterling". */
export function currencyLabel(currency: string | null): string | null {
  if (!currency) return null
  return MINOR_UNITS[currency] ?? currency
}

/** Short unit for table headers: "USD", "GBp". */
export function currencyUnit(currency: string | null): string | null {
  if (!currency) return null
  return currency === 'GBX' ? 'GBp' : currency
}
