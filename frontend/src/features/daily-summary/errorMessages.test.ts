import { ApiError } from '../../api/apiError'
import { describeError } from './errorMessages'

const http = (status: number, retryAfterSeconds?: number) =>
  new ApiError('http', 'raw server text', { status, retryAfterSeconds })

describe('describeError', () => {
  it('explains a 404 with ticker hints and no retry', () => {
    const message = describeError(http(404), 'BRK.B')

    expect(message.title).toBe('No data for BRK.B')
    expect(message.detail).toContain('BRK-B')
    expect(message.retryable).toBe(false)
  })

  it('does not offer retry for an invalid symbol', () => {
    expect(describeError(http(400), 'X').retryable).toBe(false)
  })

  it('includes the server-provided wait for 503', () => {
    expect(describeError(http(503, 12), 'TSLA').detail).toContain('Try again in 12 seconds.')
  })

  it('uses singular seconds', () => {
    expect(describeError(http(429, 1), 'TSLA').detail).toContain('in 1 second.')
  })

  it('falls back to a generic wait when none is given', () => {
    expect(describeError(http(503), 'TSLA').detail).toContain('Try again shortly.')
  })

  it.each([502, 500])('never shows raw server text for %s', (status) => {
    const message = describeError(http(status), 'TSLA')

    expect(message.detail).not.toContain('raw server text')
    expect(message.retryable).toBe(true)
  })

  it('describes network and contract failures', () => {
    expect(describeError(new ApiError('network', 'x'), 'TSLA').title).toBe("Can't reach the server")
    expect(describeError(new ApiError('invalid-response', 'x'), 'TSLA').title).toBe(
      'Unexpected response',
    )
    expect(describeError(http(504), 'TSLA').title).toBe('Market data provider timed out')
  })

  it('handles non-ApiError values', () => {
    expect(describeError(new Error('boom'), 'TSLA').title).toBe('Something went wrong')
  })
})
