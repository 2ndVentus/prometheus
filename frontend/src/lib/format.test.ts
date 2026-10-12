import {
  formatAxisNumber,
  formatCompact,
  formatDayLong,
  formatDayShort,
  formatHeadlinePrice,
  formatPrice,
  formatVolume,
} from './format'

describe('formatPrice', () => {
  it.each([
    [371.4284, '371.4284'],
    [382.7, '382.7000'],
    [2910, '2,910.0000'],
    [1.12, '1.1200'],
    [0.0001, '0.0001'],
  ])('formats %s as %s', (value, expected) => {
    expect(formatPrice(value)).toBe(expected)
  })
})

describe('formatVolume', () => {
  it('uses thousands separators', () => {
    expect(formatVolume(22329809)).toBe('22,329,809')
  })
})

describe('formatCompact', () => {
  it.each([
    [49073348, '49.1M'],
    [980000, '980K'],
    [2056734397, '2.1B'],
  ])('formats %s as %s', (value, expected) => {
    expect(formatCompact(value)).toBe(expected)
  })
})

describe('day formatting', () => {
  // Tests run in America/Los_Angeles: new Date('2026-10-09') would render as Oct 8 here.
  it('shows the calendar day as given, regardless of the viewer timezone', () => {
    expect(formatDayShort('2026-10-09')).toBe('Oct 9')
    expect(formatDayLong('2026-10-09')).toBe('Fri, Oct 9, 2026')
  })

  it('handles month and year boundaries', () => {
    expect(formatDayShort('2026-11-01')).toBe('Nov 1')
    expect(formatDayLong('2027-01-01')).toBe('Fri, Jan 1, 2027')
  })
})

describe('formatAxisNumber', () => {
  it.each([
    [350, '350'],
    [1.1205, '1.1205'],
    [7850, '7,850'],
  ])('formats %s as %s', (value, expected) => {
    expect(formatAxisNumber(value)).toBe(expected)
  })
})

describe('formatHeadlinePrice', () => {
  it.each([
    [384.9723, '384.9723'],
    [1.12, '1.1200'],
    [86099.248, '86,099.25'],
    [1000, '1,000.00'],
  ])('formats %s as %s', (value, expected) => {
    expect(formatHeadlinePrice(value)).toBe(expected)
  })
})
