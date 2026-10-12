import {
  formatCompact,
  formatDayLong,
  formatDayShort,
  formatPrice,
  formatVolume,
} from '../../lib/format'
import type { DailySummary } from './schema'

interface DailySummaryTableProps {
  symbol: string
  data: DailySummary[]
  /** Quote currency for the price headers, e.g. "USD" or "GBp"; omitted when unknown. */
  currencyUnit?: string | null
}

export function DailySummaryTable({ symbol, data, currencyUnit = null }: DailySummaryTableProps) {
  const unit = currencyUnit ? ` (${currencyUnit})` : ''
  const newestFirst = [...data].reverse()

  return (
    <div className="border-border bg-surface overflow-x-auto rounded-xl border">
      <table className="w-full text-sm">
        <caption className="sr-only">Daily summary for {symbol}, newest first</caption>
        <thead>
          <tr className="border-hairline text-ink-secondary border-b text-left">
            <th scope="col" className="px-3 py-2 font-medium sm:px-4">
              Day
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium sm:px-4">
              Low average{unit}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium sm:px-4">
              High average{unit}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium sm:px-4">
              Volume
            </th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {newestFirst.map((row) => (
            <tr key={row.day} className="border-hairline border-b last:border-0">
              <th scope="row" className="px-3 py-2 text-left font-normal whitespace-nowrap sm:px-4">
                <time dateTime={row.day}>
                  {/* Short date on phones so all four columns fit; screen readers always get the full date. */}
                  <span aria-hidden className="sm:hidden">
                    {formatDayShort(row.day)}
                  </span>
                  <span className="sr-only sm:not-sr-only">{formatDayLong(row.day)}</span>
                </time>
              </th>
              <td className="px-3 py-2 text-right sm:px-4">{formatPrice(row.lowAverage)}</td>
              <td className="px-3 py-2 text-right sm:px-4">{formatPrice(row.highAverage)}</td>
              <td className="px-3 py-2 text-right sm:px-4">
                <span aria-hidden className="sm:hidden">
                  {formatCompact(row.volume)}
                </span>
                <span className="sr-only sm:not-sr-only">{formatVolume(row.volume)}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
