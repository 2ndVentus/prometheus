import { dailySummariesSchema } from './schema'

const valid = { day: '2026-10-09', lowAverage: 383.0768, highAverage: 384.9723, volume: 36336060 }

describe('dailySummariesSchema', () => {
  it('accepts the API contract', () => {
    expect(dailySummariesSchema.parse([valid])).toEqual([valid])
  })

  it('accepts an empty array', () => {
    expect(dailySummariesSchema.parse([])).toEqual([])
  })

  it.each([
    ['missing field', { day: valid.day, lowAverage: 1, highAverage: 2 }],
    ['string price', { ...valid, lowAverage: '383.07' }],
    ['malformed day', { ...valid, day: '09/10/2026' }],
    ['fractional volume', { ...valid, volume: 1.5 }],
    ['negative volume', { ...valid, volume: -1 }],
  ])('rejects %s', (_, row) => {
    expect(dailySummariesSchema.safeParse([row]).success).toBe(false)
  })

  it('rejects a non-array body', () => {
    expect(dailySummariesSchema.safeParse({ items: [valid] }).success).toBe(false)
  })
})
