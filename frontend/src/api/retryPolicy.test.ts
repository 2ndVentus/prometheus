import { ApiError } from './apiError'
import { shouldRetry } from './retryPolicy'

describe('shouldRetry', () => {
  const error = (status: number) => new ApiError('http', 'x', { status })

  it.each([502, 504])('retries %s once', (status) => {
    expect(shouldRetry(0, error(status))).toBe(true)
    expect(shouldRetry(1, error(status))).toBe(false)
  })

  it.each([400, 404, 429, 503, 500])('does not retry %s', (status) => {
    expect(shouldRetry(0, error(status))).toBe(false)
  })

  it('does not retry a proxy 502 (our API is down, retrying will not help)', () => {
    expect(shouldRetry(0, new ApiError('network', 'x', { status: 502 }))).toBe(false)
  })

  it('does not retry network or contract failures', () => {
    expect(shouldRetry(0, new ApiError('network', 'x'))).toBe(false)
    expect(shouldRetry(0, new Error('x'))).toBe(false)
  })
})
