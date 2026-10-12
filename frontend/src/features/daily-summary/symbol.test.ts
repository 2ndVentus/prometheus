import { isValidSymbol, normalizeSymbol, SYMBOL_MAX_LENGTH } from './symbol'

describe('symbol rules (mirror of backend StockSymbol)', () => {
  it('normalises by trimming and upper-casing', () => {
    expect(normalizeSymbol('  brk-b ')).toBe('BRK-B')
  })

  it.each(['TSLA', '^GSPC', 'BRK-B', 'VOD.L', 'EURUSD=X', '0700.HK'])('accepts %s', (symbol) => {
    expect(isValidSymbol(symbol)).toBe(true)
  })

  it.each(['', 'TS LA', 'ABC/DEF', '<SCRIPT>', 'tsla', 'A'.repeat(SYMBOL_MAX_LENGTH + 1)])(
    'rejects "%s"',
    (symbol) => {
      expect(isValidSymbol(symbol)).toBe(false)
    },
  )

  it('allows exactly the maximum length', () => {
    expect(isValidSymbol('A'.repeat(SYMBOL_MAX_LENGTH))).toBe(true)
  })
})
