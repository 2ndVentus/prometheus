import { z } from 'zod'

/** Runtime contract for GET /api/v1/stocks/{symbol} — mirrors InstrumentResponse. */
export const instrumentSchema = z.object({
  symbol: z.string().min(1),
  longName: z.string().nullable(),
  currency: z.string().nullable(),
  exchangeName: z.string().nullable(),
  instrumentType: z.string().nullable(),
})

export type Instrument = z.infer<typeof instrumentSchema>
