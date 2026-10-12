// All formatting is locale-aware via Intl but pinned to en-US so tests and screenshots are stable.
const LOCALE = 'en-US'

const priceFormat = new Intl.NumberFormat(LOCALE, {
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
})
const axisNumberFormat = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 4 })
const volumeFormat = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 })
const compactFormat = new Intl.NumberFormat(LOCALE, {
  notation: 'compact',
  maximumFractionDigits: 1,
})
const shortDateFormat = new Intl.DateTimeFormat(LOCALE, {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})
const longDateFormat = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'short',
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
})

/** Prices at the API's precision, always 4 dp so columns compare at a glance. No currency: the API doesn't say which. */
export const formatPrice = (value: number): string => priceFormat.format(value)

const headlineLargePriceFormat = new Intl.NumberFormat(LOCALE, {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/**
 * For standalone headline figures (stat tiles): 4 dp is noise on a 5-digit price and won't fit a
 * tile, so prices ≥ 1,000 show 2 dp. Tables and tooltips keep the full 4 dp.
 */
export const formatHeadlinePrice = (value: number): string =>
  Math.abs(value) >= 1000 ? headlineLargePriceFormat.format(value) : priceFormat.format(value)

/** Axis ticks are already round numbers (see niceTicks), so show only the decimals they need. */
export const formatAxisNumber = (value: number): string => axisNumberFormat.format(value)

export const formatVolume = (value: number): string => volumeFormat.format(value)

/** For axis ticks: 49.1M, 980K. */
export const formatCompact = (value: number): string => compactFormat.format(value)

/**
 * `day` is an exchange-local calendar date ("2026-10-09"), not an instant. `new Date("2026-10-09")`
 * would parse it as UTC midnight and show Oct 8 to anyone west of UTC, so build the date in UTC and
 * format it in UTC.
 */
function toUtcDate(day: string): Date {
  const [year, month, date] = day.split('-').map(Number)
  return new Date(Date.UTC(year ?? 0, (month ?? 1) - 1, date ?? 1))
}

export const formatDayShort = (day: string): string => shortDateFormat.format(toUtcDate(day))

export const formatDayLong = (day: string): string => longDateFormat.format(toUtcDate(day))
