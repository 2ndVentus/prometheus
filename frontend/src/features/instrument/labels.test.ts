import { currencyLabel, currencyUnit, exchangeLabel, instrumentTypeLabel } from './labels'

describe('instrumentTypeLabel', () => {
  it.each([
    ['EQUITY', 'Stock'],
    ['ETF', 'ETF'],
    ['INDEX', 'Index'],
    ['CURRENCY', 'Currency pair'],
    ['CRYPTOCURRENCY', 'Cryptocurrency'],
    ['MUTUALFUND', 'Mutual fund'],
    ['WARRANT', 'Warrant'],
  ])('labels %s as %s', (type, label) => {
    expect(instrumentTypeLabel(type)).toBe(label)
  })

  it('returns null when unknown', () => {
    expect(instrumentTypeLabel(null)).toBeNull()
  })
})

describe('exchangeLabel', () => {
  it('shows the exchange for exchange-traded instruments', () => {
    expect(exchangeLabel('NasdaqGS', 'EQUITY')).toBe('NasdaqGS')
    expect(exchangeLabel('NYSEArca', 'ETF')).toBe('NYSEArca')
    expect(exchangeLabel('LSE', null)).toBe('LSE')
  })

  it.each([
    ['SNP', 'INDEX'],
    ['CCY', 'CURRENCY'],
    ['CCC', 'CRYPTOCURRENCY'],
  ])('hides the terse code %s for %s', (exchange, type) => {
    expect(exchangeLabel(exchange, type)).toBeNull()
  })

  it('returns null when unknown', () => {
    expect(exchangeLabel(null, 'EQUITY')).toBeNull()
  })
})

describe('currency labels', () => {
  it('passes ordinary codes through', () => {
    expect(currencyLabel('USD')).toBe('USD')
    expect(currencyUnit('JPY')).toBe('JPY')
  })

  it.each([
    ['GBp', 'pence sterling'],
    ['GBX', 'pence sterling'],
    ['ZAc', 'South African cents'],
    ['ILA', 'Israeli agorot'],
  ])('spells out the minor unit %s', (code, words) => {
    expect(currencyLabel(code)).toContain(words)
  })

  it('normalises GBX to GBp for column headers', () => {
    expect(currencyUnit('GBX')).toBe('GBp')
    expect(currencyUnit('GBp')).toBe('GBp')
  })

  it('returns null when unknown', () => {
    expect(currencyLabel(null)).toBeNull()
    expect(currencyUnit(null)).toBeNull()
  })
})
