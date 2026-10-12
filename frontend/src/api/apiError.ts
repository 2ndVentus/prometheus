/** RFC 7807 problem details, as returned by the backend for every error. */
export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export type ApiErrorKind = 'http' | 'network' | 'invalid-response'

/** A failed API call, normalised so the UI never deals with raw Response/TypeError objects. */
export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | undefined
  readonly problem: ProblemDetails | undefined
  /** Seconds the server asked us to wait (Retry-After), when it said. */
  readonly retryAfterSeconds: number | undefined

  constructor(
    kind: ApiErrorKind,
    message: string,
    options: {
      status?: number
      problem?: ProblemDetails
      retryAfterSeconds?: number
      cause?: unknown
    } = {},
  ) {
    super(message, { cause: options.cause })
    this.name = 'ApiError'
    this.kind = kind
    this.status = options.status
    this.problem = options.problem
    this.retryAfterSeconds = options.retryAfterSeconds
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}
