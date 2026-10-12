# Design Decisions

The decisions behind this project, why they were made, and what was consciously left out. Each
decision was agreed between the author and Claude during a step-by-step design review (API →
backend → frontend → testing → hidden issues → setup/CI) and then refined while building.
Where the build changed a planned decision, the entry says **Changed during build** and why.

This file is the source for the README's "Design & trade-offs" section. `PROMPT_LOG.md` records
the conversation itself.

## At a glance

| Area | Decision |
|---|---|
| Upstream | Yahoo chart API, `interval=15m&range=1mo`, browser User-Agent |
| Day boundaries | Exchange-local calendar day (IANA zone, DST-aware) |
| Contract | The brief's bare array; prices averaged in `decimal`, rounded once to 4 dp (away from zero) |
| Data cleaning | Drop bars with any null price; drop Yahoo's trailing last-price snapshot only |
| Backend | .NET 10, Api → Application ← Infrastructure, Minimal APIs, ProblemDetails |
| Errors | Values for validation, provider-neutral exceptions for upstream failures, one handler |
| Resilience | Standard resilience handler (≤ 2 retries), HybridCache (5 min), per-client rate limit |
| Frontend | React 19 + Vite + TypeScript, TanStack Query, zod, Recharts, Tailwind v4 |
| Charts | Price and volume as two charts on one time axis (no dual axis), plus a full table |
| Tests | xUnit v3 (MTP) + Shouldly + NSubstitute; Vitest + Testing Library + MSW; real Yahoo fixtures |
| Run | Native (SDK + Node) or `docker compose up --build` (Docker only) |
| GitHub | Public repo, PR per milestone, ruleset as code, CodeQL, Dependabot, SHA-pinned actions |

---

## 1. Upstream API (Yahoo Finance chart API)

Findings from probing `https://query1.finance.yahoo.com/v8/finance/chart/{symbol}` across US,
Toronto, London, Frankfurt, Tokyo, Hong Kong, India and Sydney equities, indices, crypto, FX and
futures.

### 1.1 Request: `interval=15m&range=1mo`
- The brief's example curl (no `range`) returns **only the latest trading day**, which doesn't
  satisfy "last month". `range=1mo` returns ~22 trading days.
- `range=1mo` rather than computing `period1`/`period2`: it uses Yahoo's own definition of a month
  and keeps clock logic out of our code.
- 15m data is kept for **60 days** only (`range=3mo` → HTTP 422), so interval and range are
  validated against allow-lists in configuration.
- `includePrePost` stays `false`: extended-hours bars are mostly zero-volume and would distort the
  averages.
- A browser-like `User-Agent` is **required**; without it Yahoo answers 429.

### 1.2 Days are exchange-local
- Each bar's UTC timestamp is converted to `meta.exchangeTimezoneName` (IANA, DST-aware via
  `TimeZoneInfo`) and grouped by that calendar date. If the zone can't be resolved on the host, the
  fixed `meta.gmtoffset` is used and a **warning is logged** (a fixed offset is wrong across a DST
  change).
- Grouping by UTC or server time happens to work for US stocks but is wrong for Asia/Pacific
  exchanges and 24/7 instruments.

### 1.3 Aggregation semantics
- `lowAverage` = mean of the day's 15m bar **lows**; `highAverage` = mean of the **highs**;
  `volume` = **sum** of volumes.
- Computed in `decimal`, rounded **once, after averaging**, to **4 dp** with
  `MidpointRounding.AwayFromZero` (financial convention; .NET defaults to banker's rounding).
- Days in ascending order. A day with no usable bars is omitted, never emitted as zeros.
- The brief's sample date (`2009-01-30`) only illustrates the format; 15m history doesn't go back
  that far.

### 1.4 The response is the brief's bare array
- `[{ day, lowAverage, highAverage, volume }]`, no envelope. Instrument metadata (name, currency,
  exchange, type) is served by a **separate endpoint** (§2.5) so this contract stays exactly as
  specified.

### 1.5 Incomplete bars are dropped
- Yahoo sends bars with `null` prices: **every Hong Kong and Tokyo session has lunch-break null
  bars** (found while building fixtures; the first probe had missed them), plus FX gaps, halts and
  illiquid symbols. A bar with **any** null price is removed so it can't drag averages towards zero.
  Null volume counts as 0.

### 1.6 Yahoo's trailing "snapshot" bar is dropped — narrowly
- Yahoo appends a **last-price snapshot** to the end of the series: zero volume and
  open = high = low = close. Seen on US, Toronto, London, Frankfurt, Tokyo, Sydney, futures and
  crypto; **not** on Hong Kong or India, whose final bar is a real auction bar with volume. Its
  timestamp isn't always on the 15m grid (BHP.AX 16:12, GC=F 16:59).
- **Rule: drop only the last bar, and only if it is zero-volume and flat.**
- Rejected: dropping every zero-volume bar (deletes real bars — Tokyo's 11:30 pre-lunch bar, FX,
  indices); matching `meta.regularMarketTime` (off by 497–2301 s for VOD.L, SAP.DE, BHP.AX).
- **Order: snapshot rule first, then incomplete bars.** The snapshot rule must see the series as
  Yahoo sent it; removing nulls first could make a genuine bar "last".
- During market hours the last bar is the in-progress bar, which has volume, so it's kept.

### 1.7 Symbols
- Trimmed and upper-cased (Yahoo normalises `tsla` → `TSLA` anyway) and URL-escaped
  (`^GSPC`, `EURUSD=X`).
- Yahoo uses `-` for share classes (`BRK-B`, not `BRK.B`) and suffixes for other exchanges
  (`VOD.L`); the UI hint and the 404 message say so.

---

## 2. Backend (.NET 10 LTS)

### 2.1 Three projects: Api → Application ← Infrastructure
- The compiler enforces the dependency direction: core logic knows nothing about Yahoo or HTTP. A
  second provider or endpoint can be added without touching it.
- **No separate Domain project (yet).** The domain is a few records and a pure function, so it
  lives in `Application/Domain/`. Splitting it out later is mechanical.

### 2.2 Domain model (Application)
- **`StockSymbol` value object**: parsed once at the edge (trim, upper-case, **max 14 chars**,
  `[A-Z0-9.\-^=]`) and passed as that type everywhere after; equality by normalised value.
- **`IntradayBar` has non-nullable prices and volume.** Infrastructure drops incomplete bars before
  mapping, so "a bar is complete" is enforced by the type system.
- **`DailySummaryAggregator` is a static pure function**: nothing to mock or swap, so no interface.
  **`DailySummaryService`** (behind `IDailySummaryService`) orchestrates provider → aggregator and
  is the seam the API depends on.

### 2.3 Yahoo integration (Infrastructure)
- **Data-cleaning filters live in Infrastructure**, not the core: `TrailingSnapshotBarFilter` and
  `IncompleteBarFilter` (each an `IBarFilter`, registered in order) exist because of *Yahoo's*
  quirks, not a business rule. The provider returns clean bars; Application never sees the mess.
- **`YahooChartMapper`** validates the shape (missing meta/quote, mismatched array lengths →
  "unavailable"), resolves the timezone (§1.2) and runs the filters.
- **Wire DTOs** are separate from domain records and deserialized with a source-generated
  `JsonSerializerContext`; everything nullable, so the mapper decides what's required.
- **`YahooMarketDataProvider`**: typed `HttpClient`, symbol escaping, and every failure translated
  into a provider-neutral exception (§2.4).
- **Options** (`MarketData:Yahoo`), validated at startup: base URL, User-Agent, interval and range
  (allow-lists), retries, timeouts.
- **Resilience** via `Microsoft.Extensions.Http.Resilience` standard handler: **≤ 2 retries** with
  jittered backoff (honours `Retry-After`), **4 s per attempt, 10 s total**, circuit breaker. The
  backend owns the retry budget; the frontend doesn't stack its own on top (hidden issue #7).
- `SocketsHttpHandler` with automatic decompression and a 5-minute pooled-connection lifetime
  (picks up DNS changes).

### 2.4 Error handling: values for validation, exceptions for upstream failures
- **Invalid input** is an expected mistake → returned as a value (`TypedResults.ValidationProblem`),
  no exception.
- **Upstream failures** become provider-neutral exceptions defined in Application
  (`SymbolNotFoundException`, `MarketDataRateLimitedException` with `RetryAfter`,
  `MarketDataUnavailableException`, `MarketDataTimeoutException`, all `MarketDataException`), mapped
  to RFC 7807 ProblemDetails by **one `IExceptionHandler`**. **Changed during build:** named
  `MarketData*` rather than the planned `Upstream*`, so the API needn't know which provider failed.
- **Why not `Result<T>`:** C# has no `?` propagation, so every layer gains unwrap/re-wrap
  boilerplate, and `HttpClient`/JSON/cancellation throw anyway, so a global handler is needed
  regardless. A unit test enumerates every `MarketDataException` type and asserts it has an HTTP
  mapping, closing the "forgot to map it → silent 500" gap. Switching the service boundary to
  `Result<T>` later is a contained change.

| Condition | Response |
|---|---|
| Invalid symbol format | 400 ValidationProblem (`errors.symbol`, allowed characters, examples) |
| Yahoo 404 / "Not Found" | 404, with the symbol and a `BRK-B` hint |
| Yahoo 429 | **503** + `Retry-After` when Yahoo gave one (our dependency is throttled, not the caller) |
| Yahoo 5xx / 422 / bad JSON / shape change / network / open circuit | 502 |
| Timeout | 504 |
| Our own rate limit exceeded | 429 + `Retry-After` |
| Anything unexpected | 500 ProblemDetails, no details |
| Client disconnected mid-request | 499, logged at debug (not an error) |
| Valid symbol, no bars | 200 `[]` |

- 5xx-class problems carry **generic details**; the exception (which may contain provider
  internals) is logged, never returned. Every problem has `instance` and `traceId`.

### 2.5 HTTP API
- **`GET /api/v1/stocks/{symbol}/daily-summary`** with Minimal APIs (`MapGroup` + `TypedResults`,
  which flow into OpenAPI). Versioned prefix for forward compatibility.
- The route takes a raw `string` and parses `StockSymbol` in the handler, so an invalid symbol gets
  a **descriptive** 400 (binding the value object directly would give a generic one).
- **`GET /api/v1/stocks/{symbol}`** returns instrument metadata:
  `{ symbol, longName, currency, exchangeName, instrumentType }`, unknown fields as explicit `null`.
  - **Same upstream call, same cache.** Yahoo's chart response already carries the metadata in
    `meta`, so the provider maps it into an `InstrumentInfo` carried on the `IntradaySeries`, and
    both endpoints read the same cached entry. The UI requests both at once; HybridCache collapses
    that into **one** Yahoo request (verified end to end). Rejected: a second provider method making
    its own call — doubles upstream traffic against a per-IP rate limit.
  - **Provider-neutral names**: Yahoo's `fullExchangeName` is exposed as `exchangeName`, and
    `longName` falls back to `shortName` (and `fullExchangeName` to `exchangeName`) when missing.
  - Values are passed through as reported: `GBp` means pence; indices, crypto and FX report terse
    exchange codes (`SNP`, `CCC`, `CCY`). Presentation is the frontend's job.
  - Same validation, error mapping and rate limit as the daily summary (shared helpers in
    `StockEndpoints`).
- **`DailySummaryResponse`** is separate from the domain record; serialized with a source-generated
  `ApiJsonContext`. **`CanonicalDecimalJsonConverter`** writes the shortest form (`382.7`, not
  `382.7000`) because `decimal` carries its scale into JSON (hidden issue #1).

### 2.6 Caching
- **`CachedMarketDataProvider`** decorates the Yahoo provider using **HybridCache**: 5-minute TTL
  (`MarketData:Cache:Duration`) and **stampede protection** — concurrent requests for a symbol make
  one upstream call, which matters because Yahoo rate-limits per IP.
- Caches the **raw series**, not summaries, so future endpoints can reuse it.
- "Not found" is cached with the same TTL. **Changed during build** from a planned 1-minute
  negative TTL: HybridCache sets expiry per call, not per result, so a separate TTL needs a second
  key and lookup. Rate-limit, timeout and availability failures are never cached.
- The cache entry stores the timezone as its **system id** (or a fixed UTC offset for the fallback
  zone). `TimeZoneInfo.FromSerializedString` was tried first and **fails for ICU-backed zones on
  Linux/macOS** (e.g. `Europe/London`) — caught by a round-trip test. The entry is `sealed` +
  `[ImmutableObject(true)]` so HybridCache reuses the instance in memory, yet stays serializable.
- In-memory today; a distributed cache (Redis) is one service registration away.

### 2.7 Hosting
- **Rate limiting**: per-client fixed window on `/api/v1/stocks` only (`RateLimiting:PermitLimit`,
  default 30/min; `/health` exempt), protecting the shared upstream quota.
- **Forwarded headers** trusted only from `ForwardedHeaders:KnownNetworks` (empty by default; the
  compose subnet in Docker), so rate limiting sees real clients behind nginx without letting
  clients spoof `X-Forwarded-For` (verified, §6.2).
- **CORS**: allow-list from `Cors:AllowedOrigins` (empty by default — local dev and Docker are
  same-origin via proxies), GET only, exposes `Retry-After`. Verified by tests that CORS headers
  survive on error responses.
- **No HTTPS redirection or HSTS in the app.** **Changed during build:** TLS terminates at the
  reverse proxy, where HSTS belongs; redirecting behind an HTTP-only proxy only logs warnings and
  risks redirect loops.
- Middleware order: forwarded headers → exception handler → status code pages → CORS → rate
  limiter → endpoints.
- `/health`; OpenAPI + Scalar UI **in Development only**; HTTP launch profile on **:5080**.
- `CancellationToken` flows end-to-end, so an abandoned request cancels the upstream call.

| Configuration | Default |
|---|---|
| `MarketData:Yahoo:{BaseUrl, UserAgent, Interval, Range}` | Yahoo query1, browser UA, `15m`, `1mo` |
| `MarketData:Yahoo:{MaxRetryAttempts, AttemptTimeout, TotalRequestTimeout}` | 2, 4 s, 10 s |
| `MarketData:Cache:Duration` | 5 min |
| `RateLimiting:{PermitLimit, Window}` | 30, 1 min |
| `Cors:AllowedOrigins` | none |
| `ForwardedHeaders:KnownNetworks` | none (compose: `172.28.0.0/24`) |

### 2.8 Code conventions
- Nullable enabled, warnings as errors, latest recommended analyzers, code style enforced in build
  (`.editorconfig`), central package management, NuGet lock files with `--locked-mode` restores.
- `sealed` by default, records for data, primary constructors, source-generated logging.

---

## 3. Frontend (React 19 + TypeScript + Vite)

### 3.1 Tooling
- **Vite 8 + React 19 + TypeScript 6** (`strict`, `noUncheckedIndexedAccess`). CRA is deprecated;
  Next.js would add a server we don't need.
- **Node 24 LTS**, pinned via `.nvmrc` and `engines`; npm. npm 11's install-script gate is set
  explicitly: `fsevents` and `msw` post-install scripts are denied (neither is needed).
- **oxlint + Prettier.** **Changed during build** from ESLint: the current Vite template ships
  oxlint; it's one dependency instead of ~6, much faster, and covers React hooks, TypeScript,
  jsx-a11y, Vitest and import-cycle rules. Run with `--deny-warnings`. Prettier sorts Tailwind classes.
- **Tailwind CSS v4** (author's preference over CSS Modules): tokens are `@theme` variables, with
  separately chosen dark values under `prefers-color-scheme`. No component library.

### 3.2 Structure
- `src/api/` (HTTP client, `ApiError`) → `src/features/daily-summary/` (schema, symbol rules,
  hooks, components) → `src/components/` (generic UI) → `src/lib/` (formatting). Components never
  call `fetch` directly; new features become new folders.

### 3.3 Data
- **TanStack Query**: loading/error/cache states without hand-rolled `useEffect` races; 5-minute
  `staleTime` to match the backend cache; no refetch on window focus; the previous result stays on
  screen, dimmed and `aria-busy`, while a new symbol loads.
- **Retry once, only on 502/504 answered by our API** — never 4xx, never 503 (the backend already
  retried), never a proxy's gateway error (below).
- **zod** validates every response, so contract drift fails loudly instead of rendering `NaN`;
  `z.infer` is the single source of the `DailySummary` type. OpenAPI client generation is deferred.
- **State in the URL** (`?symbol=TSLA`): shareable, bookmarkable, back/forward and refresh work.
  A small `useSyncExternalStore` hook over `popstate`; no router. Invalid `?symbol=` values are
  ignored. The form is keyed by symbol so back/forward resets the input.
- **API base URL**: same-origin `/api` by default — the Vite dev/preview proxy locally, nginx in
  Docker. `VITE_API_BASE_URL` (plus the backend CORS allow-list) only if the API is hosted elsewhere.

### 3.4 Errors
Every failure becomes a typed `ApiError` (`http` / `network` / `invalid-response`) with
ProblemDetails and `Retry-After` (seconds or date).

| Situation | Message |
|---|---|
| 400 | "Invalid symbol" — no retry button |
| 404 | "No data for XYZ" with `BRK-B` / `VOD.L` hints — no retry button |
| 429 / 503 | "Slow down" / "provider is busy" + **"Try again in N seconds"** when known |
| 504 / other 5xx | Generic timeout / unavailable text (server detail never shown) + Try again |
| Network failure, or a 502/503/504 **without ProblemDetails** | "Can't reach the server" + Try again |
| Unreadable or off-contract body | "Unexpected response" + Try again |

- **A gateway status without ProblemDetails means our own API is down.** Our API answers every
  error with ProblemDetails, so a bare 502/503/504 came from a proxy in front of it. **Changed
  during build:** found by stopping the backend while testing — the UI said "Market data
  unavailable" and retried, which was wrong and pointless.

### 3.5 Visualization
- **Chart and table.** **Changed during build:** the planned single chart with volume on a second
  y-axis became **two charts on one shared time axis** — dual axes invite false correlations. Price
  chart: high and low average lines (2 px) with the daily range as a 10% wash; volume chart below
  (columns ≤ 24 px, rounded tops). Crosshairs are synced and **one tooltip lists every value** for
  the hovered day.
- **Recharts 3** (declarative, typed, testable). Rejected: TradingView `lightweight-charts` (built
  for OHLC candles we don't have; imperative canvas), Chart.js (imperative, weaker React fit).
- **Palette validated, not eyeballed**: blue/orange/aqua checked with a palette validator in both
  modes, every pair (colour-vision-deficiency ΔE ≥ 9.2 light / 9.4 dark). Light-mode aqua is below
  3:1 contrast, which is acceptable because the table carries every value. Chart colours are
  concrete values in `chartTheme.ts` (SVG attributes don't reliably resolve CSS variables).
- **Axes**: the price axis hugs the data (never from 0) on **round ticks** (`niceTicks`: steps of
  1/2/2.5/5 × 10ⁿ), every tick with the same decimals (`9.990 / 9.995 / 10.000`). Found by looking
  at real output: Recharts produced `376.6388`, and PAXGU (~$10) produced `9.99 / 10`.
- **Lazy-loaded chart**: Recharts is most of the bundle; initial JS went from 212 kB to 105 kB
  gzipped. A same-size placeholder avoids layout shift, and an **error boundary** (keyed by symbol)
  turns a chart crash or a failed chunk download into "chart unavailable — see the table".
- **Table**: semantic `<table>`, newest first, tabular numerals; short dates and compact volume on
  phones so all columns fit, while screen readers always get full values.
- **Stat tiles**: trading days, lowest low average, highest high average, average daily volume.
  Prices ≥ 1,000 show 2 dp in tiles (exact value on hover and in the table). Tiles size their value
  with a **container query** and sit two per row until wide screens. **Changed during build:** the
  author spotted BTC-USD prices overflowing the tiles.
- **Short history note**: fewer than 15 trading days shows "Only N trading days of data…", so the
  "last month" heading doesn't mislead for new listings (PAXGU had 4) or suspended symbols.

### 3.6 Formatting
- Prices at a **fixed 4 dp** in table and tooltip (the API's precision; aligned columns).
- **No currency symbol**: the contract has no currency and some instruments aren't USD (VOD.L is in
  pence).
- `day` is a calendar date, not an instant: it is never passed to `new Date("yyyy-MM-dd")` (UTC
  midnight → the previous day west of UTC); formatting builds it in UTC.

### 3.7 Input and accessibility
- Input trimmed and upper-cased; the backend's symbol rule is mirrored for instant feedback (the
  backend stays the authority). Search on submit; example chips (TSLA, AAPL, ^GSPC, VOD.L, 7203.T);
  submit disabled while loading.
- Labelled input with `aria-invalid`/`aria-describedby`; chips in a `<fieldset>`; loading in an
  `<output>`; errors in `role="alert"`; charts labelled with SVG `title`/`desc` inside a
  `<figure>` (not `role="img"`, which would hide Recharts' keyboard layer); the table is the full
  text equivalent. oxlint's jsx-a11y rules caught several `role=` attributes with native equivalents.

---

## 4. Testing

### 4.1 Principles
- Weighted to pure logic (aggregation, filters, parsing, formatting); behaviour over
  implementation; `Method_Scenario_Expected` naming.
- **No real network in CI.** Yahoo is replaced by **trimmed real payloads** captured during the
  API investigation (TSLA, 0700.HK, EURUSD=X, BHP.AX, 7203.T, not-found, rate-limited), so tests see
  genuine upstream quirks. Small builders (`Bars`, `RawBars`) cover synthetic edge cases.
- **Expected values for fixtures were computed independently** (Python, exact decimals) rather than
  by running the C# code and pasting its output.
- **Timezones**: backend tests run under `TZ=Asia/Tokyo` in CI; frontend tests under
  `America/Los_Angeles` (set in a Vitest `globalSetup`, with a sanity test). A `new Date("yyyy-MM-dd")`
  regression or server-timezone dependence fails tests.

### 4.2 Backend
- **xUnit v3 on Microsoft.Testing.Platform**, **Shouldly**, **NSubstitute**, hand-written HTTP stubs.
  - Shouldly, not FluentAssertions: FluentAssertions v8+ needs a paid commercial licence.
  - NSubstitute, not Moq: avoids the SponsorLink controversy.
  - **Changed during build:** VSTest was replaced by MTP — the .NET 10 SDK no longer runs xUnit v3
    in VSTest mode. `global.json` opts in; coverage comes from
    `Microsoft.Testing.Extensions.CodeCoverage`. MTP fails a run where zero tests executed, which is
    kept as a guard against filters that silently exclude everything.
- **Integration tests** (`WebApplicationFactory`) assert the **raw JSON string**, ProblemDetails per
  status, `Retry-After`, CORS on error responses, rate limiting, OpenAPI in Development only, no
  leakage from 500s in Production, and end-to-end runs on a captured Yahoo response.
- **Live contract tests** call real Yahoo through the production pipeline. They are xUnit
  **explicit** tests tagged `Category=Live`: never part of a normal or PR run; run with
  `-- --explicit only` by the scheduled workflow (§6.5) and on demand.
- **Coverage is reported, not gated.** Each test project writes its own `TestResults/` (a shared
  folder made a second project overwrite the first's coverage file — found when adding it), and
  `coverage.settings.xml` excludes generated code.

### 4.3 Frontend
- **Vitest + Testing Library + user-event + MSW.** MSW intercepts at the `fetch` level, so tests
  exercise the real client → zod → TanStack Query → component chain.
- Covered: formatters, schema accept/reject, every `ApiError` kind including proxy gateway errors
  and abort propagation, retry policy, form validation, every UI state (idle, loading, data, empty,
  each error, retry recovery), URL load and back navigation, stat tiles, chart rendering (titles,
  legend, one column per day — no SVG snapshots), error boundary.

---

## 5. Hidden issues and mitigations

Identified during design; ✅ = verified by a test or by hand.

| # | Issue | Mitigation |
|---|---|---|
| 1 | `decimal` keeps its scale in JSON (`382.7000`, `2910.0`) | `CanonicalDecimalJsonConverter`; raw-JSON tests ✅ |
| 2 | HybridCache serializes values unless sealed + immutable; `TimeZoneInfo` isn't serializable | Cache entry stores the zone id / offset; marked immutable; round-trip tests ✅ |
| 3 | CORS headers missing on error responses → opaque network error in the browser | CORS headers survive the exception handler; `Retry-After` exposed; tests ✅ |
| 4 | macOS AirPlay occupies port 5000 | API on **5080**, Vite on 5173, Docker on 8080 |
| 5 | HTTPS dev-certificate friction for reviewers | HTTP locally; TLS/HSTS belong at the reverse proxy (§2.7) |
| 6 | No Vite proxy in `vite preview` | `preview.proxy` configured ✅ |
| 7 | Retry amplification (frontend × backend; retrying 429 worsens throttling) | Backend owns the budget (≤ 2, honours `Retry-After`, breaker); frontend retries once on API-answered 502/504 ✅ |
| 8 | Per-IP rate limit behind a proxy puts everyone in one bucket | Forwarded headers from the compose subnet only; per-client buckets and spoof resistance verified behind nginx ✅ |
| 9 | Missing tzdata/ICU in slim containers → silent fixed-offset fallback | Full `aspnet` image (Ubuntu 24.04); fallback logs a warning; none seen in Docker ✅ |
| 10 | Today's row is partial during market hours | UI footnote; round once after averaging ✅ |
| 11 | Fewer days or bars than expected (holidays, half-days, new listings) | Nothing assumes fixed counts; short-history note; tests for short and empty series ✅ |
| 12 | Special characters in symbols (`^`, `=`, `-`, `.`) | Escaped on both sides; tested both sides ✅ |
| 13 | Client aborts logged as 500s | Handled as 499, debug log ✅ |
| 14 | A price axis from 0 flattens the lines | Round-tick domain hugging the data; volume in its own chart ✅ |
| 15 | Recharts in jsdom / React 19 | Recharts 3; `ResizeObserver` stub; fixed-size container in chart tests ✅ |
| 16 | `VITE_*` values are baked in at build time | Accepted for the MVP (deferred) |
| 17 | Yahoo's API is unofficial (no SLA, terms restrict use, may add auth) | `IMarketDataProvider` abstraction; scheduled live contract test; configurable host |
| 18 | GitHub Actions IPs get throttled by Yahoo | Live job is non-blocking with one retry |
| 19 | Float32 artefacts in Yahoo numbers (`381.239990234375`) | Parsed straight into `decimal`, averaged at full precision, rounded once ✅ |
| 20 | Volume overflow | Not an issue: `long` in .NET; JS is exact to 9e15 vs ~1e11 observed |
| 21 | React StrictMode double-fetch in development | Expected; TanStack Query de-duplicates |

---

## 6. Running, containers, GitHub and CI

### 6.1 Two ways to run
- **Native** (.NET 10 SDK + Node 24): `dotnet run` the API on :5080, `npm run dev` on :5173 with
  the Vite proxy. No certificates, no environment variables.
- **Docker Compose** (`docker compose up --build`, then http://localhost:8080): only Docker
  needed, which removes the "SDK not installed" failure mode.
- No Makefile: not available on Windows by default; explicit commands in the README are clearer.

### 6.2 Containers
- **`api`**: multi-stage `sdk:10.0` → `aspnet:10.0`. The full runtime image (on .NET 10 that's
  **Ubuntu 24.04**, not the Debian assumed in planning) ships tzdata and ICU; Asia/Tokyo resolves
  with no fallback warning. Restore runs from project and lock files first for layer caching.
  Non-root (uid 1654), port 8080, **not published to the host**; healthcheck via bash `/dev/tcp`.
- **`web`**: `node:24-alpine` build → **unprivileged nginx** (non-root, :8080) serving the SPA and
  proxying `/api` to the API — one origin, no CORS. Security headers (CSP, nosniff, frame deny,
  referrer policy) come from one included snippet, because nginx doesn't inherit `add_header` into
  locations that set their own. Hashed assets cached for a year, `index.html` never cached, gzip.
  The CSP allows inline *styles* only (React/Recharts style attributes); no violations in the browser.
- **Hardening**: read-only root filesystems with tmpfs `/tmp`, all Linux capabilities dropped,
  `no-new-privileges` (verified: `CapEff 0`, `NoNewPrivs 1`). `web` waits for `api` to be healthy.
- **Forwarded headers**: a fixed compose subnet (`172.28.0.0/24`) is the only trusted proxy network.
  Verified with the limit lowered to 3/min: the host client got 429 on its 4th request; a spoofed
  `X-Forwarded-For` didn't help (nginx appends the real IP and the API reads that last hop); a second
  client kept its own allowance.
- Base images pinned by **tag and digest**; Dependabot bumps both.

### 6.3 GitHub
- **Public repo**, default branch `main`, Conventional Commits, **a PR per milestone**.
- **Process**: the author reviews every change locally before it's committed; Claude commits, pushes
  and opens PRs only when asked. The assignment PDF is git-ignored (a third-party document).
- **Merge settings**: merge commits off; **squash** (PR title + body) for Dependabot and small PRs;
  **rebase** for milestone PRs so their individual commits stay visible on `main`. Head branches
  are deleted on merge; "update branch" is suggested; auto-merge is off.
- **Dependabot**: weekly, grouped per ecosystem — nuget, npm, github-actions, dotnet-sdk, docker.
  Major updates to `@types/node` (must match the Node 24 runtime) and `typescript` (TS 7 is a new
  native compiler needing deliberate verification) are ignored.
- **CodeQL advanced setup** (`codeql.yml`; C#, TypeScript, Actions; `security-and-quality`
  queries; C# `build-mode: none`) on every PR, every push to `main` and weekly. Its findings are
  fixed rather than dismissed (e.g. undisposed test objects, float multiplication, `Path.Combine`).

**Branch protection: a repository ruleset kept as code** (`.github/rulesets/main.json`, applied with
`gh api … /rulesets`). Rulesets over classic protection: auditable, layerable, visible to readers of
a public repo, reproducible from a file.

| Rule | Setting | Why |
|---|---|---|
| Restrict deletion, block force pushes | on | `main` can't be deleted or rewritten |
| Require a pull request | **0 approvals**, conversations must be resolved | Solo repo — GitHub doesn't allow approving your own PR |
| Required checks `backend`, `frontend`, `docker` | **pinned to the GitHub Actions app**, branch must be up to date | Another integration can't satisfy them; PRs are tested against current `main` |
| Linear history | on | Matches the merge settings |
| Code scanning | CodeQL high+ security alerts or errors block merge | Requires CodeQL results on every PR (see lesson) |
| Bypass list | empty | Rules apply to the admin too; edits to the ruleset are audited |

- GitHub adds an undocumented `require_extra_approval_for_unattributed_changes` parameter (default
  `true`); left at the default — our commits are attributed, and merges aren't blocked by it.
- **Lesson — the code-scanning gate and CodeQL default setup.** With *default* setup, CodeQL skipped
  a docs-only PR, so the code-scanning rule waited forever and blocked it. Fix: rule temporarily
  removed, CodeQL moved to an advanced-setup workflow that runs on every PR, default setup turned
  off (they can't coexist), rule restored. A merge gate has to be tried against every kind of PR
  before it counts as done.

### 6.4 CI (`ci.yml`: every PR and push to `main`)
- Read-only token; concurrency cancels superseded PR runs; **actions pinned to commit SHAs**.
- **backend**: SDK from `global.json`, NuGet cache, `restore --locked-mode`, `dotnet format
  --verify-no-changes`, Release build (warnings as errors), tests without `Category=Live` under
  `TZ=Asia/Tokyo` with coverage; coverage summary (ReportGenerator, pinned in `dotnet-tools.json`)
  in the job summary; TRX and coverage artifacts.
- **frontend**: Node from `.nvmrc`, npm cache, `npm ci`, lint, format check, typecheck, tests with
  coverage, build.
- **docker**: build, start with `--wait`, smoke test through nginx (`/healthz`, `index.html`, and an
  invalid symbol the API rejects with 400 — proves nginx → API without calling Yahoo), logs on
  failure, tear down.
- **No path filters**: a path-filtered *required* check leaves PRs stuck "waiting for status", and
  all three jobs are fast.

### 6.5 Live contract workflow (`live-contract.yml`)
- Weekdays 15:00 UTC (during the US session, so in-session responses are exercised) and on demand;
  runs the explicit live tests with one retry.
- **Never a required check**: Yahoo is outside our control and throttles shared CI IPs, so a red run
  means "look", not "block merges".

---

## 7. Delivery history

| PR | Milestone |
|---|---|
| #1 | Scaffold: solution, Vite app, CI, repo config |
| #5 | Branch protection ruleset, merge settings, CodeQL |
| #6 | Backend core: `StockSymbol`, domain, aggregation (test-first), CodeQL advanced setup |
| #7 | Yahoo infrastructure: provider, cleaning filters, caching, fixtures |
| #8 | API host: endpoint, error mapping, CORS, rate limiting, OpenAPI |
| #9 | Frontend: data layer, charts, table, states |
| #10 | Docker Compose stack, docker CI job, live contract workflow |
| — | Documentation: README, `PROMPT_LOG.md` |

---

## 8. Deliberate deferrals

Consciously **not** built for the MVP, why, and what would trigger revisiting.

| Deferred | Why not now | Revisit when |
|---|---|---|
| Separate `Domain` project | A few records and a pure function | The domain gains real behaviour (indicators, portfolios, corporate actions) |
| `Result<T>` across layers | Boilerplate without `?` propagation; a global handler is needed anyway | Richer validation or several failure types per call |
| Listing date and timezone in the metadata | Not needed by the UI yet | A feature needs them (e.g. explaining short histories) |
| Configurable range / interval | The brief fixes "last month"; 15m data is limited to 60 days | Product needs other windows (validated allow-list) |
| Extended-hours data | Mostly zero-volume bars that distort averages | An explicit need; a separate opt-in series |
| Distributed cache (Redis) | Single instance; in-memory HybridCache suffices | Scaling out to several instances |
| Serilog / OpenTelemetry exporters | Built-in structured `ILogger` is enough | Deployed somewhere with a log/trace backend |
| OpenAPI-generated TypeScript client | Codegen in build/CI for one endpoint | ~3+ endpoints or a second API consumer |
| TanStack Table (sorting, paging) | ~22 rows | Larger or interactive tables |
| Client-side router | One view; URL search params cover shareable state | A second page |
| Component library (MUI etc.) | Bundle weight for one view | Design-system requirements |
| Light/dark toggle | Follows the OS setting | Users ask for it |
| MSW in the browser (mock mode) | Tests use MSW in Node only | Frontend work must proceed without the backend |
| Authentication / API keys | Public read-only data; rate limiting protects the upstream quota | User-specific data or a paid upstream |
| Playwright end-to-end tests | Integration tests on both sides of the HTTP boundary cover the contract | Several user flows or pages |
| Mutation testing (Stryker) | Heavyweight for an MVP | Test-suite quality becomes a concern |
| Coverage thresholds | Encourage coverage-chasing tests; coverage is reported | The team agrees on a meaningful gate |
| Runtime frontend config | Build-time `VITE_*` is fine for one deployment | Build once, deploy to many environments |
| .NET Aspire | Extra SDK workload; Compose already gives a one-command run | More services, or a need for its dashboard |
| CD and image publishing (GHCR) | No target environment | A hosting target exists |
| Release versioning / changelog automation | A single MVP release | Regular releases |
| PR approvals, stale-review dismissal, CODEOWNERS | Solo repo | A second contributor joins |
| Required signed commits | GitHub's rebase-merge produces unsigned commits | Policy requires provenance (switch to squash-only) |
| Dependabot auto-merge | Updates reviewed by hand while the project is young | The test suite has proven stable |

---

## 9. Open items

- [ ] Verify the trailing-snapshot rule on a **live in-session** response (probes were post-close;
      the weekday live workflow now runs during the US session).
- [ ] Verify the CodeQL merge gate on a **docs-only PR** (every PR since the fix contained code).
- [ ] The resilience pipeline's retry behaviour is configured but not exercised by a test (the
      standard backoff would make it slow); covered by the library's own tests and the live tests.

---

## 10. Manual changes (outside AI)

Changes made directly by the author, detected when reviewing merged code.

| Where | Change | Rationale |
|---|---|---|
| `StockSymbol.MaxLength` (PR #6) | 20 → **14** | _(author to fill in)_ |
| `DailySummaryAggregator.Aggregate` (PR #6) | Added an XML `<summary>` | _(author to fill in)_ |

---

## 11. End-of-project checklist

- [ ] Generate `PROMPT_LOG.md` from the session's prompts — for each: the prompt, what it aimed to
      achieve, what was kept/changed/rejected and why, plus an empty **`My rationale:`** block for
      the author.
- [ ] Write the README (setup and run for both paths, API reference, design & trade-offs from this
      file).
- [ ] Author fills in the rationale for the manual changes (§10).
