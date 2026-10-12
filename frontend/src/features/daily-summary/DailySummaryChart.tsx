import {
  Area,
  Bar,
  BarChart,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import {
  createAxisFormatter,
  formatCompact,
  formatDayLong,
  formatDayShort,
  formatPrice,
  formatVolume,
} from '../../lib/format'
import { useChartTheme, type ChartTheme } from './chartTheme'
import { niceTicks } from './niceTicks'
import type { DailySummary } from './schema'

/**
 * Two charts on one shared time axis instead of one dual-axis chart: price (low/high average with
 * the daily range as a wash) on top, volume below. The crosshair and tooltip are synced, and the
 * tooltip lists every value for the hovered day. The table below is the accessible equivalent.
 */
export function DailySummaryChart({ symbol, data }: { symbol: string; data: DailySummary[] }) {
  const theme = useChartTheme()
  const rows = data.map((d) => ({ ...d, range: [d.lowAverage, d.highAverage] as [number, number] }))
  const ticks = priceTicks(data)
  const formatPriceTick = createAxisFormatter(ticks)
  const syncId = `daily-summary-${symbol}`
  const axisTick = { fill: theme.inkMuted, fontSize: 12 }

  return (
    <figure className="border-border bg-surface rounded-xl border p-4">
      <figcaption className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <span className="font-medium">Daily price range</span>
        <span className="text-ink-secondary flex gap-4 text-sm">
          <LegendKey color={theme.high} label="High average" />
          <LegendKey color={theme.low} label="Low average" />
        </span>
      </figcaption>

      <div className="h-64">
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart
            data={rows}
            syncId={syncId}
            title={`Daily low and high average prices for ${symbol}`}
            desc={`${data.length} trading days. Values are listed in the table below.`}
            // Bottom margin keeps the lowest tick label clear: the hidden x-axis reserves no space.
            margin={{ top: 8, right: 8, bottom: 8, left: 0 }}
          >
            <CartesianGrid vertical={false} stroke={theme.grid} />
            <XAxis dataKey="day" hide />
            <YAxis
              domain={[ticks[0] ?? 'auto', ticks.at(-1) ?? 'auto']}
              ticks={ticks}
              interval={0}
              tickFormatter={formatPriceTick}
              tick={axisTick}
              tickLine={false}
              axisLine={false}
              width={72}
            />
            <Tooltip
              content={<DayTooltip theme={theme} />}
              cursor={{ stroke: theme.axis, strokeWidth: 1 }}
            />
            <Area
              dataKey="range"
              stroke="none"
              fill={theme.high}
              fillOpacity={0.1}
              isAnimationActive={false}
              activeDot={false}
            />
            <Line
              dataKey="highAverage"
              name="High average"
              stroke={theme.high}
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 4, stroke: theme.surface, strokeWidth: 2 }}
              isAnimationActive={false}
            />
            <Line
              dataKey="lowAverage"
              name="Low average"
              stroke={theme.low}
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 4, stroke: theme.surface, strokeWidth: 2 }}
              isAnimationActive={false}
            />
          </ComposedChart>
        </ResponsiveContainer>
      </div>

      <p className="mt-4 mb-2 font-medium">Volume</p>
      <div className="h-36">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart
            data={rows}
            syncId={syncId}
            title={`Daily volume for ${symbol}`}
            desc="Values are listed in the table below."
            margin={{ top: 4, right: 8, bottom: 0, left: 0 }}
          >
            <CartesianGrid vertical={false} stroke={theme.grid} />
            <XAxis
              dataKey="day"
              tickFormatter={formatDayShort}
              tick={axisTick}
              tickLine={false}
              axisLine={{ stroke: theme.axis }}
              minTickGap={24}
            />
            <YAxis
              tickCount={3}
              tickFormatter={formatCompact}
              tick={axisTick}
              tickLine={false}
              axisLine={false}
              width={72}
            />
            {/* Shares the price chart's hover; the readout lives in the one tooltip above. */}
            <Tooltip content={() => null} cursor={{ fill: theme.grid, fillOpacity: 0.5 }} />
            <Bar
              dataKey="volume"
              name="Volume"
              fill={theme.volume}
              maxBarSize={24}
              radius={[4, 4, 0, 0]}
              isAnimationActive={false}
            />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </figure>
  )
}

/** Price axis hugs the data on round ticks — starting at 0 would flatten a $380 stock into a line. */
function priceTicks(data: DailySummary[]): number[] {
  return niceTicks(
    Math.min(...data.map((d) => d.lowAverage)),
    Math.max(...data.map((d) => d.highAverage)),
  )
}

function LegendKey({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span
        aria-hidden
        className="inline-block h-0.5 w-4 rounded-full"
        style={{ backgroundColor: color }}
      />
      {label}
    </span>
  )
}

interface DayTooltipProps {
  active?: boolean
  payload?: ReadonlyArray<{ payload?: unknown }>
  theme: ChartTheme
}

/** Values lead, labels follow; line keys rather than boxes. */
function DayTooltip({ active, payload, theme }: DayTooltipProps) {
  const row = payload?.[0]?.payload as DailySummary | undefined
  if (!active || !row) return null

  const entries = [
    { label: 'High average', value: formatPrice(row.highAverage), color: theme.high },
    { label: 'Low average', value: formatPrice(row.lowAverage), color: theme.low },
    { label: 'Volume', value: formatVolume(row.volume), color: theme.volume },
  ]

  return (
    <div className="border-border bg-surface rounded-lg border px-3 py-2 text-sm shadow-sm">
      <p className="text-ink-secondary mb-1">{formatDayLong(row.day)}</p>
      {entries.map((entry) => (
        <p key={entry.label} className="flex items-center gap-2">
          <span
            aria-hidden
            className="inline-block h-0.5 w-3 rounded-full"
            style={{ backgroundColor: entry.color }}
          />
          <span className="font-semibold tabular-nums">{entry.value}</span>
          <span className="text-ink-secondary">{entry.label}</span>
        </p>
      ))}
    </div>
  )
}
