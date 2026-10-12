import { isApiError } from '../../api/apiError'

export interface ErrorMessage {
  title: string
  detail: string
  /** Whether offering "Try again" makes sense. */
  retryable: boolean
}

/** Turns any query error into something a person can act on. Raw server text is never shown for 5xx. */
export function describeError(error: unknown, symbol: string): ErrorMessage {
  if (!isApiError(error)) {
    return {
      title: 'Something went wrong',
      detail: 'An unexpected error occurred.',
      retryable: true,
    }
  }

  const wait = error.retryAfterSeconds
  const waitText =
    wait !== undefined
      ? ` Try again in ${wait} second${wait === 1 ? '' : 's'}.`
      : ' Try again shortly.'

  switch (error.kind) {
    case 'network':
      return {
        title: "Can't reach the server",
        detail: 'Check your connection, or that the API is running.',
        retryable: true,
      }
    case 'invalid-response':
      return {
        title: 'Unexpected response',
        detail: 'The server returned data in a format this app does not understand.',
        retryable: true,
      }
    case 'http':
      break
  }

  switch (error.status) {
    case 400:
      return {
        title: 'Invalid symbol',
        detail: `"${symbol}" doesn't look like a valid ticker.`,
        retryable: false,
      }
    case 404:
      return {
        title: `No data for ${symbol}`,
        detail:
          'Check the ticker — Yahoo uses a dash for share classes (BRK-B, not BRK.B) and suffixes for non-US exchanges (VOD.L).',
        retryable: false,
      }
    case 429:
      return {
        title: 'Slow down',
        detail: `You're searching too quickly.${waitText}`,
        retryable: true,
      }
    case 503:
      return {
        title: 'Market data provider is busy',
        detail: `The data provider is rate limiting us.${waitText}`,
        retryable: true,
      }
    case 504:
      return {
        title: 'Market data provider timed out',
        detail: 'The data provider did not respond in time.',
        retryable: true,
      }
    default:
      return {
        title: 'Market data unavailable',
        detail: 'The data provider returned an error. Please try again.',
        retryable: true,
      }
  }
}
