import { niceTicks } from './niceTicks'

describe('niceTicks', () => {
  it('covers the range with round steps', () => {
    expect(niceTicks(350.1237, 384.9723)).toEqual([350, 360, 370, 380, 390])
  })

  it('works for small FX-style ranges without float noise', () => {
    expect(niceTicks(1.12, 1.1217)).toEqual([1.12, 1.1205, 1.121, 1.1215, 1.122])
  })

  it('works for large index values', () => {
    expect(niceTicks(7650.2, 7806.8)).toEqual([7650, 7700, 7750, 7800, 7850])
  })

  it('pads a flat range', () => {
    const ticks = niceTicks(100, 100)
    expect(ticks[0]).toBeLessThan(100)
    expect(ticks.at(-1)).toBeGreaterThan(100)
  })

  it('returns nothing for non-finite input', () => {
    expect(niceTicks(Number.NaN, 1)).toEqual([])
  })
})
