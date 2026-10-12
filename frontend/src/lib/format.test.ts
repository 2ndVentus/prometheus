import {
  createAxisFormatter,
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

describe('createAxisFormatter', () => {
  it('gives every tick the same decimals', () => {
    const format = createAxisFormatter([9.99, 9.995, 10, 10.005])

    expect([9.99, 9.995, 10, 10.005].map(format)).toEqual(['9.990', '9.995', '10.000', '10.005'])
  })

  it('uses no decimals for whole-number ticks', () => {
    const format = createAxisFormatter([350, 360, 370])

    expect(format(360)).toBe('360')
    expect(createAxisFormatter([7800, 7850])(7850)).toBe('7,850')
  })

  it('handles FX-sized steps and caps at 4 decimals', () => {
    expect(createAxisFormatter([1.12, 1.1205, 1.121])(1.12)).toBe('1.1200')
    expect(createAxisFormatter([0.00001, 0.00002])(0.00001)).toBe('0.0000')
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
