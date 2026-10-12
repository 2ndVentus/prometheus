import { formatCompact, formatDayShort, formatHeadlinePrice, formatPrice } from '../../lib/format'
import type { DailySummary } from './schema'

interface Stat {
  label: string
  value: string
  /** Full-precision value, shown on hover when the tile rounds it. */
  exact?: string
  note: string
}

function computeStats(data: DailySummary[]): Stat[] {
  const first = data[0]
  const last = data[data.length - 1]
  if (!first || !last) return []

  const lowest = data.reduce((min, d) => (d.lowAverage < min.lowAverage ? d : min), first)
  const highest = data.reduce((max, d) => (d.highAverage > max.highAverage ? d : max), first)
  const averageVolume = data.reduce((sum, d) => sum + d.volume, 0) / data.length

  return [
    {
      label: 'Trading days',
      value: String(data.length),
      note: `${formatDayShort(first.day)} – ${formatDayShort(last.day)}`,
    },
    {
      label: 'Lowest low average',
      value: formatHeadlinePrice(lowest.lowAverage),
      exact: formatPrice(lowest.lowAverage),
      note: formatDayShort(lowest.day),
    },
    {
      label: 'Highest high average',
      value: formatHeadlinePrice(highest.highAverage),
      exact: formatPrice(highest.highAverage),
      note: formatDayShort(highest.day),
    },
    {
      label: 'Average daily volume',
      value: formatCompact(averageVolume),
      note: 'shares / units per day',
    },
  ]
}

export function SummaryStats({ data }: { data: DailySummary[] }) {
  return (
    // Two per row until there's room for four, so tiles never get squeezed to ~140px.
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {computeStats(data).map((stat) => (
        <div
          key={stat.label}
          className="border-border bg-surface @container min-w-0 rounded-xl border p-4"
        >
          <dt className="text-ink-secondary text-sm">{stat.label}</dt>
          {/* Sized by the tile's own width (container query), so long prices shrink instead of overflowing. */}
          <dd
            className="mt-1 text-lg font-semibold @[10rem]:text-xl @[12rem]:text-2xl"
            title={stat.exact !== undefined && stat.exact !== stat.value ? stat.exact : undefined}
          >
            {stat.value}
          </dd>
          <dd className="text-ink-muted mt-0.5 text-xs">{stat.note}</dd>
        </div>
      ))}
    </dl>
  )
}
