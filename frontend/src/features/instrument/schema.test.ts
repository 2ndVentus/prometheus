import { instrumentSchema } from './schema'

describe('instrumentSchema', () => {
  it('accepts full details', () => {
    const tesla = {
      symbol: 'TSLA',
      longName: 'Tesla, Inc.',
      currency: 'USD',
      exchangeName: 'NasdaqGS',
      instrumentType: 'EQUITY',
    }
    expect(instrumentSchema.parse(tesla)).toEqual(tesla)
  })

  it('accepts unknown details as null', () => {
    const unknown = {
      symbol: '^GSPC',
      longName: null,
      currency: null,
      exchangeName: null,
      instrumentType: null,
    }
    expect(instrumentSchema.parse(unknown)).toEqual(unknown)
  })

  it.each([
    ['missing field', { symbol: 'TSLA', longName: null, currency: null, exchangeName: null }],
    [
      'empty symbol',
      { symbol: '', longName: null, currency: null, exchangeName: null, instrumentType: null },
    ],
    [
      'number currency',
      { symbol: 'TSLA', longName: null, currency: 840, exchangeName: null, instrumentType: null },
    ],
  ])('rejects %s', (_, body) => {
    expect(instrumentSchema.safeParse(body).success).toBe(false)
  })
})
