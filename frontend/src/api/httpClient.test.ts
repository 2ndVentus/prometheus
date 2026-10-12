import { http, HttpResponse } from 'msw'
import { z } from 'zod'
import { problem, server } from '../test/server'
import { ApiError } from './apiError'
import { getJson } from './httpClient'

const schema = z.object({ ok: z.boolean() })
const URL = '/api/thing'

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  const error: unknown = await promise.catch((e: unknown) => e)
  expect(error).toBeInstanceOf(ApiError)
  return error as ApiError
}

describe('getJson', () => {
  it('returns the validated body', async () => {
    server.use(http.get(URL, () => HttpResponse.json({ ok: true })))

    await expect(getJson(URL, schema)).resolves.toEqual({ ok: true })
  })

  it('turns ProblemDetails into an http ApiError carrying status and detail', async () => {
    server.use(
      http.get(URL, () => problem(404, 'Symbol not found', { detail: 'No data for ZZZZ' })),
    )

    const error = await failure(getJson(URL, schema))

    expect(error.kind).toBe('http')
    expect(error.status).toBe(404)
    expect(error.problem?.title).toBe('Symbol not found')
    expect(error.message).toBe('No data for ZZZZ')
  })

  it('reads Retry-After seconds', async () => {
    server.use(http.get(URL, () => problem(503, 'Busy', { headers: { 'Retry-After': '12' } })))

    expect((await failure(getJson(URL, schema))).retryAfterSeconds).toBe(12)
  })

  it('reads Retry-After as an HTTP date', async () => {
    const inThirty = new Date(Date.now() + 30_000).toUTCString()
    server.use(http.get(URL, () => problem(503, 'Busy', { headers: { 'Retry-After': inThirty } })))

    const seconds = (await failure(getJson(URL, schema))).retryAfterSeconds
    expect(seconds).toBeGreaterThanOrEqual(28)
    expect(seconds).toBeLessThanOrEqual(30)
  })

  it.each([
    ['an HTML proxy page', 502, '<html>Bad Gateway</html>'],
    ['an empty proxy response', 502, ''],
    ['an empty 504', 504, ''],
    ['a 503 without problem details', 503, 'Service Unavailable'],
    ['JSON that is not problem details', 502, '{"message":"upstream connect error"}'],
  ])('treats a gateway error from %s as the server being unreachable', async (_, status, body) => {
    server.use(http.get(URL, () => new HttpResponse(body, { status })))

    const error = await failure(getJson(URL, schema))

    expect(error.kind).toBe('network')
    expect(error.status).toBe(status)
  })

  it('keeps a gateway status from our API (with ProblemDetails) as an http error', async () => {
    server.use(http.get(URL, () => problem(502, 'Market data provider error')))

    const error = await failure(getJson(URL, schema))

    expect(error.kind).toBe('http')
    expect(error.problem?.title).toBe('Market data provider error')
  })

  it('reports a non-gateway error without problem details by status', async () => {
    server.use(http.get(URL, () => new HttpResponse('<html>oops</html>', { status: 500 })))

    const error = await failure(getJson(URL, schema))

    expect(error.kind).toBe('http')
    expect(error.status).toBe(500)
    expect(error.message).toBe('Request failed (500).')
  })

  it('reports network failures as kind "network"', async () => {
    server.use(http.get(URL, () => HttpResponse.error()))

    expect((await failure(getJson(URL, schema))).kind).toBe('network')
  })

  it('reports a body that breaks the contract as kind "invalid-response"', async () => {
    server.use(http.get(URL, () => HttpResponse.json({ ok: 'yes' })))

    expect((await failure(getJson(URL, schema))).kind).toBe('invalid-response')
  })

  it('reports unparseable JSON as kind "invalid-response"', async () => {
    server.use(
      http.get(
        URL,
        () => new HttpResponse('not json', { headers: { 'Content-Type': 'application/json' } }),
      ),
    )

    expect((await failure(getJson(URL, schema))).kind).toBe('invalid-response')
  })

  it('lets aborts propagate untouched', async () => {
    server.use(http.get(URL, () => HttpResponse.json({ ok: true })))
    const controller = new AbortController()
    controller.abort()

    const error: unknown = await getJson(URL, schema, controller.signal).catch((e: unknown) => e)

    expect(error).not.toBeInstanceOf(ApiError)
    expect((error as Error).name).toBe('AbortError')
  })
})
