import { z } from 'zod'

/** Runtime contract for GET /api/v1/stocks/{symbol}/daily-summary — mirrors DailySummaryResponse. */
export const dailySummarySchema = z.object({
  day: z.iso.date(),
  lowAverage: z.number().nonnegative(),
  highAverage: z.number().nonnegative(),
  volume: z.number().int().nonnegative(),
})

export const dailySummariesSchema = z.array(dailySummarySchema)

export type DailySummary = z.infer<typeof dailySummarySchema>
