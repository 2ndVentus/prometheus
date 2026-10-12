import type { z } from 'zod'
import { ApiError, type ProblemDetails } from './apiError'

/**
 * Base URL for API calls. Empty means same-origin (`/api/...`), which is what the Vite dev/preview
 * proxy and the nginx container use. Set `VITE_API_BASE_URL` only when the API lives elsewhere.
 */
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

/**
 * GETs JSON and validates it against `schema`. Every failure becomes an {@link ApiError}:
 * non-2xx (with ProblemDetails when the body has them), network failure, or a response that
 * doesn't match the contract. Aborts propagate untouched so callers can ignore them.
 */
export async function getJson<T>(
  path: string,
  schema: z.ZodType<T>,
  signal?: AbortSignal,
): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, { headers: { Accept: 'application/json' }, signal })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new ApiError('network', 'Could not reach the server.', { cause: error })
  }

  if (!response.ok) {
    const problem = await readProblem(response)

    // Our API answers every error with ProblemDetails. A gateway status *without* them came from a
    // proxy in front of it (Vite dev proxy, nginx) — i.e. our backend is down or unreachable, which
    // is a connectivity problem, not "the market data provider failed".
    if (problem === undefined && GATEWAY_STATUSES.has(response.status)) {
      throw new ApiError('network', 'Could not reach the server.', { status: response.status })
    }

    throw new ApiError(
      'http',
      problem?.detail ?? problem?.title ?? `Request failed (${response.status}).`,
      {
        status: response.status,
        problem,
        retryAfterSeconds: parseRetryAfter(response.headers.get('Retry-After')),
      },
    )
  }

  let body: unknown
  try {
    body = await response.json()
  } catch (error) {
    throw new ApiError('invalid-response', 'The server returned an unreadable response.', {
      cause: error,
    })
  }

  const parsed = schema.safeParse(body)
  if (!parsed.success) {
    throw new ApiError('invalid-response', 'The server returned data in an unexpected format.', {
      cause: parsed.error,
    })
  }
  return parsed.data
}

const GATEWAY_STATUSES = new Set([502, 503, 504])

/** RFC 7807 bodies always carry a numeric `status` or a `title`; anything else isn't ours. */
async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    const body: unknown = await response.json()
    if (typeof body !== 'object' || body === null) return undefined
    const candidate = body as ProblemDetails
    return typeof candidate.status === 'number' || typeof candidate.title === 'string'
      ? candidate
      : undefined
  } catch {
    return undefined // e.g. a proxy's HTML or empty error page
  }
}

function parseRetryAfter(value: string | null): number | undefined {
  if (value === null) return undefined
  const seconds = Number(value)
  if (Number.isFinite(seconds) && seconds >= 0) return Math.ceil(seconds)
  const date = Date.parse(value)
  return Number.isNaN(date) ? undefined : Math.max(0, Math.ceil((date - Date.now()) / 1000))
}
