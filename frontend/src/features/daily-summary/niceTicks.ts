/**
 * Round axis ticks (e.g. 350 / 360 / 370 / 380 / 390) covering [min, max]. Recharts spaces ticks
 * evenly between the domain ends, so non-round ends give ticks like 376.6388.
 */
export function niceTicks(min: number, max: number, targetCount = 5): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max)) return []
  if (min === max) {
    const pad = Math.abs(min) * 0.01 || 1
    return niceTicks(min - pad, max + pad, targetCount)
  }

  const step = niceStep((max - min) / (targetCount - 1))
  const start = Math.floor(min / step) * step
  const end = Math.ceil(max / step) * step
  const ticks: number[] = []
  for (let value = start; value <= end + step / 2; value += step) {
    ticks.push(Number(value.toPrecision(12))) // strip float noise (0.30000000000000004)
  }
  return ticks
}

/** The smallest of 1, 2, 2.5, 5 × 10^k that is at least `rough`. */
function niceStep(rough: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const fraction = rough / magnitude
  const nice =
    fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10
  return nice * magnitude
}
