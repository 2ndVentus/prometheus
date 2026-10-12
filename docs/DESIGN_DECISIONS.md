# Design Decisions

Running log of design decisions and the reasoning behind them. Source material for the README's
"Design & Trade-offs" section.

---

## 1. Upstream API (Yahoo Finance chart API)

Findings from probing `https://query1.finance.yahoo.com/v8/finance/chart/{symbol}` across 13+
symbols (US, Toronto, London, Frankfurt, Tokyo, Hong Kong, India, Sydney, crypto, FX, futures).

### 1.1 Request `interval=15m&range=1mo`
- The example curl in the brief (no `range`) returns **only the latest trading day**; it does not
  satisfy "last month". `range=1mo` returns ~22 trading days.
- `range=1mo` is preferred over computing `period1`/`period2` ourselves: it aligns with Yahoo's own
  definition of a month and keeps clock logic (and `TimeProvider` plumbing) out of our code.
- 15m data is only retained for **60 days** (`range=3mo` → HTTP 422). Any future configurable range
  must be validated against an allow-list.
- `includePrePost` is left at its default (`false`): extended-hours bars are mostly zero-volume and
  would distort the averages.
- A browser-like `User-Agent` header is **required**; without it Yahoo returns 429.

### 1.2 Group by **exchange-local** calendar day
- Days are derived by converting each bar's UTC timestamp into `meta.exchangeTimezoneName`
  (IANA, DST-aware via `TimeZoneInfo`), falling back to `meta.gmtoffset`.
- Grouping by UTC or server-local time happens to work for US equities but is wrong for
  Asia/Pacific exchanges and 24/7 instruments (crypto, FX).

### 1.3 Aggregation semantics
- `lowAverage` = arithmetic mean of the 15m bar **lows** for the day.
- `highAverage` = arithmetic mean of the 15m bar **highs** for the day.
- `volume` = **sum** of 15m bar volumes for the day.
- Computed in `decimal` (not `double`) and rounded to **4 dp** with
  `MidpointRounding.AwayFromZero` (financial convention; .NET's default is banker's rounding).
- Days returned in ascending order; a day with no usable bars is omitted rather than emitted with
  zero/NaN values.
- The brief's sample date (`2009-01-30`) is illustrative of format only — 15m history doesn't go
  back that far.

### 1.4 Response is the **bare array** from the brief
- The brief says "returns JSON in this format", so the endpoint returns exactly
  `[{ day, lowAverage, highAverage, volume }]` — no envelope. Metadata (currency, name, timezone)
  is deliberately not added to this endpoint; a separate endpoint can expose it later without
  breaking the contract.

### 1.5 Null bars are skipped
- Yahoo emits bars with `null` OHLC. More common than first thought: building fixtures showed
  **every Hong Kong and Tokyo session has lunch-break null bars** (4 and 3 per day), plus FX gaps
  (`EURUSD=X`), halts and illiquid symbols. A bar with **any** null price is excluded so it can't
  drag averages to zero (domain bars are complete by construction). Null volume is treated as 0.
- **Filter order: trailing-snapshot first, then incomplete-bar.** The snapshot rule must judge the
  series exactly as Yahoo sent it; if nulls were removed first, a genuine final bar could become
  "last" and be judged as a snapshot.

### 1.6 Trailing "snapshot" bar is removed — narrowly
- Yahoo appends a final **last-price snapshot** to the series: `volume == 0` and
  `open == high == low == close`. Observed on US, Toronto, London, Frankfurt, Tokyo, Sydney,
  futures and crypto; **not** present on Hong Kong or India (their final bar is a real auction /
  trading bar with volume). Its timestamp is not always on the 15m grid (e.g. BHP.AX 16:12,
  GC=F 16:59).
- Rule: **drop only the last element of the series, and only if it is zero-volume and flat.**
- Rejected alternatives:
  - *Drop all zero-volume bars* — would delete legitimate bars (Tokyo lunch break, HK, FX, index
    bars).
  - *Match on `meta.regularMarketTime`* — unreliable (off by 497–2301 s for VOD.L, SAP.DE, BHP.AX).
- During market hours the last bar is the in-progress partial bar, which has volume, so it is
  kept.

### 1.7 Symbol handling
- Symbols are trimmed/upper-cased (Yahoo normalises `tsla` → `TSLA` anyway) and URL-escaped
  (`^GSPC`, `EURUSD=X`).
- Yahoo uses `-` for share classes (`BRK-B`, not `BRK.B`), which is worth surfacing in the UI hint.

---

## 2. Backend (.NET 10, LTS)

### 2.1 Three projects: Api → Application ← Infrastructure
- Compiler-enforced dependency direction: core logic knows nothing about Yahoo or HTTP. A second
  provider or new endpoint can be added without touching the core (Open/Closed, Dependency
  Inversion).
- **No separate Domain project (yet).** The domain is currently three records and a pure
  aggregation function; a dedicated project would be ceremony. It lives in a `Domain/` folder
  inside Application and can be split out mechanically if it grows real behaviour (indicators,
  portfolios, corporate-action adjustments). This is a reversible decision.

### 2.2 Yahoo data-cleaning filters live in **Infrastructure**, not the core
- `NullBarFilter` and `TrailingSnapshotBarFilter` exist because of *Yahoo's* data quirks, not
  because of a business rule. Another provider (Polygon, Alpha Vantage…) wouldn't need them.
- Therefore `YahooMarketDataProvider` applies them and returns **clean bars**; the Application
  layer never knows the upstream was messy. Each filter is still its own small class
  (`IBarFilter`) with focused unit tests.
- Consequence for the domain model: `IntradayBar` has **non-nullable** OHLC/volume. Incomplete
  bars are dropped in Infrastructure before mapping, so "a bar is complete" is an invariant the
  type system enforces rather than something the aggregator must re-check.

### 2.2a `StockSymbol` value object (domain primitive)
- Symbols are parsed once at the edge into a `StockSymbol` (trim, upper-case, max 14 chars,
  `[A-Z0-9.\-^=]`) and passed as that type everywhere after — no raw strings reach the provider,
  and validation lives in one place shared by the endpoint and any future callers. Avoids
  primitive obsession; equality is by normalised value (`tsla` == `TSLA`).
- `DailySummaryAggregator` is a **static pure function** (no I/O, no state): there is nothing to
  mock or swap, so an interface would be ceremony. `DailySummaryService` (behind
  `IDailySummaryService`) orchestrates provider → aggregator and is the seam the API depends on.

### 2.3 Error handling: hybrid (exceptions for upstream failures, values for validation)
- **Input validation** (bad symbol format) is an expected user mistake → handled as a value at
  the endpoint (`TypedResults.ValidationProblem`), no exception thrown.
- **Upstream failures** are translated in Infrastructure into a small set of typed exceptions
  and mapped to RFC 7807 ProblemDetails by a single `IExceptionHandler`. The exception types live in
  **Application** (`Abstractions/MarketDataExceptions.cs`) and are provider-neutral —
  `SymbolNotFoundException`, `MarketDataRateLimitedException` (with `RetryAfter`),
  `MarketDataUnavailableException`, `MarketDataTimeoutException`, all deriving from
  `MarketDataException` — so the API maps them without knowing Yahoo exists. (Renamed from the
  planned `Upstream*` names during milestone 3 for that reason.)
- Why not `Result<T>` throughout: C# has no `?` propagation operator, so every layer gains
  unwrap/re-wrap boilerplate; and `HttpClient`/JSON/cancellation throw regardless, so a global
  handler is needed anyway — you'd run both mechanisms. Exceptions + `IExceptionHandler` is the
  idiomatic ASP.NET Core path.
- Mitigation for "forgot to map a new exception → silent 500": a unit test enumerates every
  exception type and asserts the handler maps it.
- Revisit if the API grows richer validation or multiple failure types per call; switching the
  service boundary to `Result<T>` is a contained change.

| Upstream condition | Our response |
|---|---|
| Invalid symbol format | 400 ValidationProblem |
| Yahoo 404 / `chart.error` "Not Found" | 404 |
| Yahoo 429 | **503** + `Retry-After` (our dependency is throttled, not the caller) |
| Yahoo 5xx / bad payload / contract change | 502 |
| Timeout | 504 |
| Valid symbol, no bars | 200 `[]` |

### 2.4 Endpoint: `GET /api/v1/stocks/{symbol}/daily-summary`
- Minimal APIs with `MapGroup` + `TypedResults` (modern default, less ceremony than controllers,
  typed results flow into OpenAPI). Versioned path prefix for forward compatibility.
- The route takes a raw `string` and parses it with `StockSymbol.TryParse` inside the handler, so an
  invalid symbol returns a **descriptive 400 ValidationProblem** (`errors.symbol`, allowed
  characters, examples). Binding `StockSymbol` directly via a static `TryParse` would also work but
  yields a generic 400 with no explanation.
- Response contract is `DailySummaryResponse` (separate from the domain record), serialized with a
  source-generated `ApiJsonContext`. Prices use `CanonicalDecimalJsonConverter` (see hidden issue
  #1): `382.7`, not `382.7000`. Precision is decided by the domain; the converter only normalises
  representation.
- Upstream 5xx-class problems return **generic details**; the exception (which may contain provider
  internals) is logged, never returned. 404s include the symbol and a `BRK-B` hint. Every problem
  carries `instance` and `traceId` for correlating with logs.

### 2.5 Caching via `HybridCache` decorator on the provider
- `CachedMarketDataProvider` decorates `YahooMarketDataProvider`; 5-min TTL (configurable via
  `MarketData:Cache:Duration`). "Not found" is cached with the **same** TTL (changed from the
  planned 1-min negative TTL: HybridCache sets expiry per call, not per result, so a separate TTL
  needs a second cache key and lookup; symbols don't spring into existence within 5 minutes, so
  the simpler single TTL was chosen). Rate-limit/timeout/unavailable failures are never cached.
- The cache entry stores the timezone as its **system id** (or a fixed UTC offset for the fallback
  zone). `TimeZoneInfo.ToSerializedString`/`FromSerializedString` was tried first and **fails for
  ICU-backed IANA zones on Linux/macOS** (e.g. `Europe/London`: "AdjustmentRule array must be in
  chronological order") — caught by a cache round-trip test.
- Stampede protection matters because Yahoo rate-limits per IP. In-memory today; swappable to
  Redis via configuration.
- Caches the **raw series**, not summaries, so future endpoints can reuse it.

### 2.6 Resilience via `Microsoft.Extensions.Http.Resilience`
- Standard handler: retry with jittered backoff on 429/5xx (not 404), per-attempt + total
  timeout, circuit breaker.

### 2.7 Other host concerns
- Our own per-IP fixed-window rate limiter on `/api/v1/stocks` only (`RateLimiting:PermitLimit`,
  default 30/min; `/health` exempt). Rejections are 429 ProblemDetails with `Retry-After`.
- **Forwarded headers** trusted only from `ForwardedHeaders:KnownNetworks` (empty by default), so
  rate limiting and logs see the real client IP behind nginx without letting arbitrary clients spoof
  `X-Forwarded-For`. Docker Compose sets the network (milestone 6).
- **No HTTPS redirection / HSTS in the app** (changed from the plan in hidden issue #5): TLS is
  terminated at the reverse proxy, where HSTS belongs. In-app redirection behind an HTTP-only proxy
  just logs "failed to determine the https port" and can cause redirect loops.
- Middleware order: forwarded headers → exception handler → status code pages → CORS → rate
  limiter → endpoints. Verified by tests that CORS headers (and exposed `Retry-After`) survive on
  error responses.
- `/health` endpoint; built-in OpenAPI with Scalar UI in Development.
- Config via Options pattern with `ValidateOnStart` (base URL, User-Agent, interval, range, TTLs).
- CORS default policy from `Cors:AllowedOrigins` (empty by default — local dev and Docker are
  same-origin via proxies), GET only, exposes `Retry-After`.
- `CancellationToken` flows end-to-end so abandoned requests cancel the upstream call.
- Source-generated `JsonSerializerContext` for Yahoo DTOs; wire DTOs kept separate from domain
  records.
- Nullable enabled, warnings-as-errors, analyzers, central package management, sealed-by-default,
  records for data.

---

## 3. Frontend (React 19 + TypeScript + Vite)

### 3.1 Tooling
- **Vite + React 19 + TypeScript (strict)**. CRA is deprecated; Next.js adds SSR/server we don't need.
- **Node 24 LTS**, pinned via `.nvmrc` and `engines`. npm.
- **oxlint + Prettier** (changed from the planned ESLint during scaffolding): the current Vite
  template ships oxlint by default; it is one dependency instead of ~6, 50–100x faster, and covers
  what we need (react hooks, typescript, jsx-a11y, vitest, import-cycle rules). Run with
  `--deny-warnings`. Prettier with `prettier-plugin-tailwindcss` for class ordering.
- Vite 8, TypeScript 6 (`strict`, `noUncheckedIndexedAccess`).

### 3.2 Feature-based structure
- `src/api/` (fetch wrapper, zod schemas) → `src/features/daily-summary/` (api, hook, components)
  → `src/components/` (generic UI) → `src/lib/` (formatting).
- Components never call `fetch` directly — mirrors the backend layering. New features become new
  folders next to `daily-summary`.

### 3.3 Data fetching: TanStack Query
- Built-in loading/error/cache states, request cancellation via `signal` when the symbol changes,
  no hand-rolled `useEffect` race conditions.
- Retry **once, only on 502/504** (never 4xx, and not 503 — the backend owns the retry budget and
  has already retried before returning 503; see hidden issue #7). `staleTime` 5 min to match the
  backend cache; `placeholderData` keeps the previous result on screen, dimmed and `aria-busy`,
  while a new symbol loads (no spinner flash).

### 3.4 Runtime response validation: zod
- TS types vanish at runtime; zod makes contract drift fail loudly instead of rendering `NaN`.
  `z.infer<>` is the single source of the `DailySummary` type.

### 3.5 Typed `ApiError` from ProblemDetails → friendly messages
| Status | Message |
|---|---|
| 400 | "Invalid symbol" — no retry |
| 404 | "No data for XYZ" + `BRK-B` / `VOD.L` hints — no retry |
| 429 / 503 | "Slow down" / "provider is busy" + **"Try again in N seconds"** from `Retry-After` |
| 504 / other 5xx | Generic timeout / unavailable text (server detail never shown) + Try again |
| network / unparseable or off-contract body | "Can't reach the server" / "Unexpected response" + Try again |
| 502/503/504 **without** ProblemDetails | "Can't reach the server", no automatic retry — see below |

- **A gateway status without ProblemDetails means our own API is down**, not the market data
  provider. Our API answers every error with ProblemDetails, so a bare 502/503/504 came from a proxy
  in front of it (Vite dev proxy, nginx). Found by stopping the backend during manual testing: the
  UI said "Market data unavailable" and retried, which was both wrong and pointless. These are now
  `kind: 'network'` and aren't retried.

### 3.6 URL state (`?symbol=TSLA`)
- Shareable, bookmarkable, back-button and refresh friendly. A small `URLSearchParams` hook — no
  router needed for a single view.

### 3.7 Visualization: chart **and** table
- **Two charts on one shared time axis, not a dual-axis chart** (changed during build: the dataviz
  guidance lists dual axes as the #1 chart mistake — two y-scales on one plot invite false
  correlations). Price chart: `highAverage` and `lowAverage` lines (2px) with the daily range as a
  10% wash; volume chart below (columns ≤24px, 4px rounded tops). `syncId` links the crosshair, and
  **one tooltip lists every value** for the hovered day.
- **Palette validated, not eyeballed**: categorical slots blue/orange/aqua checked with the palette
  validator in both modes, all pairs (worst CVD ΔE 9.2 light / 9.4 dark; normal-vision ≥ 20.9).
  Light-mode aqua is < 3:1 against the surface, which is permitted because the table carries every
  value. Colours live in `chartTheme.ts` as concrete values: SVG presentation attributes don't
  reliably resolve CSS custom properties.
- **Round axis ticks** (`niceTicks`: steps of 1/2/2.5/5 × 10ⁿ) — Recharts spaces ticks evenly between
  the domain ends, which produced labels like `376.6388`. The price axis hugs the data (never from 0).
  Every tick on an axis uses the same number of decimals (`9.990 / 9.995 / 10.000`, not
  `9.99 / 9.995 / 10`) — found testing PAXGU, a ~$10 SPAC unit.
- **Short history note**: fewer than 15 trading days (a month is ~20–23, Tokyo had 19 with
  holidays) shows "Only N trading days of data…" so the "last month" heading doesn't mislead for
  new listings (PAXGU had 4) or suspended symbols. The API has no listing date, so the note doesn't
  claim which reason applies.
- Accessibility: `<figure>`/`<figcaption>` + Recharts `title`/`desc` on each SVG (not `role="img"`,
  which would hide Recharts' keyboard navigation layer); legend with line keys; table is the full
  text equivalent.
- **Lazy-loaded** (`React.lazy`): Recharts is most of the bundle — initial JS went from 212 kB to
  105 kB gzipped; the chart chunk (111 kB) loads on demand behind a same-size placeholder.
- **Error boundary** around the chart (keyed by symbol): a chart render error or a failed lazy-chunk
  download after a deploy degrades to "chart unavailable — see table" instead of unmounting the app.
  - Rejected: TradingView `lightweight-charts` (built for OHLC candles we don't have; imperative
    canvas API), Chart.js (imperative, weaker React integration).
- Semantic `<table>` (newest first, right-aligned tabular numerals) — doubles as the chart's
  accessible equivalent. On phones it shows short dates and compact volume so all four columns fit;
  screen readers always get the full date and number.
- Summary stat tiles: trading days (range), lowest low average, highest high average, average daily
  volume. Headline prices ≥ 1,000 show **2 dp** (4 dp is noise on `86,099.2480` and overflowed the
  tile for BTC-USD); the exact value is on hover and in the table/tooltip. Tiles size their value
  with a **container query** (font steps with the tile's own width) and sit two per row until `lg`,
  so long figures shrink instead of overflowing — verified at 375/640/1024/1440px.

### 3.8 Formatting
- `Intl.NumberFormat`: prices at a **fixed 4 dp** in the table/tooltip/stats (the API's precision;
  fixed so numbers align and compare at a glance); compact volume on axes ("49.1M"); full volume
  in the table on wider screens.
- **No currency symbol** — the bare-array contract carries no currency and some symbols are not
  USD (VOD.L is in GBp/pence). Showing `$` would be wrong.
- `day` is treated as a **string label**, never `new Date("yyyy-MM-dd")` (parses as UTC midnight →
  renders as the previous day for users west of UTC).

### 3.9 Styling: Tailwind CSS (v4, `@tailwindcss/vite`)
- Chosen over CSS Modules for development speed and colocated styling; design tokens via
  Tailwind `@theme` variables (values from the dataviz reference chrome); dark mode redefines the
  same variables under `prefers-color-scheme: dark` — separately chosen dark steps, not an inversion.
- No component library (MUI etc.) — unnecessary bundle weight for a single view.

### 3.10 Input UX & accessibility
- Trim + upper-case; client-side regex mirrors the backend (UX only — backend remains the
  authority). Submit-based search, example chips (TSLA, AAPL, ^GSPC, VOD.L), disabled submit while
  in flight; new search cancels the old one.
- Labelled input with `aria-invalid`/`aria-describedby`, example chips in a `<fieldset>`, loading in
  an `<output>` (implicit `role="status"`), errors in `role="alert"`, keyboard-operable, contrast
  checked in both themes. (oxlint's jsx-a11y rules caught several `role=` attributes that had native
  element equivalents.)
- State in the URL (`useSyncExternalStore` over `popstate`); the form is keyed by symbol so
  back/forward resets the input. Invalid `?symbol=` values are ignored.

### 3.11 API base URL
- Dev: Vite proxy `/api` → backend (no CORS, zero env config to run).
- Prod: `VITE_API_BASE_URL` + backend CORS policy.

---

## 4. Testing

### 4.1 Principles
- Test pyramid weighted to pure logic (aggregation, filters, parsing, formatting).
- **No real network in CI**: Yahoo is replaced by **trimmed real payloads** captured during the
  API investigation (TSLA, 0700.HK, EURUSD=X, BHP.AX, 7203.T, not-found, rate-limited), so tests
  exercise genuine upstream quirks. A `BarBuilder` covers synthetic edge cases.
- Behaviour over implementation; `Method_Scenario_Expected` naming; Arrange/Act/Assert.

### 4.2 Backend: xUnit v3 + Shouldly + NSubstitute + hand-written `StubHttpMessageHandler`
- **Microsoft.Testing.Platform (MTP)**, not VSTest (discovered during scaffolding): xUnit v3 runs
  on MTP, and the .NET 10 SDK no longer supports VSTest-mode `dotnet test` for MTP projects.
  `global.json` opts into the MTP runner; coverage comes from
  `Microsoft.Testing.Extensions.CodeCoverage` (coverlet's VSTest collector doesn't apply);
  `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are not needed. The Live filter is
  `--filter-not-trait "Category=Live"`.
- MTP fails a run where **zero tests executed** (exit code 8). Kept as a guard against filters that
  silently exclude everything — so test projects are added in the milestone that adds their first
  tests, rather than scaffolded empty.
- **Shouldly, not FluentAssertions**: FluentAssertions v8+ requires a paid commercial licence.
- **NSubstitute, not Moq**: avoids the SponsorLink controversy; only needed at the provider seam.
- No HTTP mocking library — a small stub handler returning fixtures/status/headers is enough.
- `WebApplicationFactory<Program>` integration tests run the real pipeline and assert the
  **raw JSON string** (the contract), ProblemDetails per status, `Retry-After` on 503, CORS
  headers on error responses, and `/health`.
- **Exhaustiveness test**: every custom exception type is mapped by the `IExceptionHandler`.
- Key unit cases: exchange-local grouping (Tokyo/Sydney across UTC midnight, US DST day),
  `AwayFromZero` rounding, snapshot-filter keep/drop cases (incl. HK final bar and Tokyo lunch
  bar), null bars, mismatched arrays, timezone fallback, request URL/User-Agent/escaping, cache
  hit / stampede / negative caching.
- **Live Yahoo contract tests** (`Live/YahooLiveContractTests.cs`) are xUnit v3 **explicit** tests
  (`[Fact(Explicit = true)]`) tagged `Category=Live`: they never run in a normal `dotnet test`
  (local or PR CI) and run with `-- --explicit only` nightly and on demand, as an early warning for
  upstream shape changes. They exercise the full production pipeline (DI, resilience, cache,
  mapping).
- **Fixture expected values are computed independently** (Python with exact decimal arithmetic over
  the same JSON files) rather than by running the C# code and pasting its output — so a bug in the
  C# aggregation can't silently become the expected value.
- Coverage via MTP CodeCoverage + ReportGenerator — **reported, not gated** (thresholds encourage
  coverage-chasing tests).
  - Each test project writes to its **own** `TestResults/` (set via
    `TestingPlatformCommandLineArguments` in `tests/Directory.Build.props`). With a shared results
    directory, a second test project silently **overwrote** the first's `coverage.cobertura.xml`
    (found when Application.Tests was added).
  - `backend/coverage.settings.xml` excludes compiler/source-generated code (OpenAPI and Regex
    source generators, `[CompilerGenerated]`) so the number reflects hand-written code only
    (the overall figure went from 26% to 95.6% line / 100% branch once generated code was
    excluded).

### 4.3 Frontend: Vitest + React Testing Library + user-event + MSW
- MSW intercepts at `fetch`, so tests exercise the real httpClient → zod → TanStack Query →
  component chain.
- Cases: formatters, zod schema accept/reject, ProblemDetails → `ApiError`, no retry on 400/404,
  cancellation on symbol change, form normalisation/validation/disabled state, one error-message
  test per status, Retry button, table order and verbatim `day`, URL sync, chart renders with
  `aria-label` (no SVG snapshots).
- Vitest `globalSetup` sets **`TZ=America/Los_Angeles`** (cross-platform, unlike an inline env var
  in an npm script), and a sanity test asserts it. Any `new Date("yyyy-MM-dd")` regression then
  fails (the date shifts back a day). Backend tests likewise run under a non-UTC process timezone.

---

## 5. Hidden issues & mitigations

| # | Issue | Mitigation |
|---|---|---|
| 1 | **Decimal keeps its scale in JSON** (verified: `382.70001220703125m` → `382.7000`; Yahoo `2910.0` → `2910.0`) — inconsistent output | `CanonicalDecimalJsonConverter` writes canonical shortest form; rounding stays in the domain; raw-JSON integration tests ✅ |
| 2 | **`HybridCache` serializes values even in L1** unless `sealed` + `[ImmutableObject(true)]`; `TimeZoneInfo` isn't serializable | Cache a serializable DTO with the timezone **id string**; mark immutable; resolve `TimeZoneInfo` after retrieval |
| 3 | **CORS headers missing on error responses** → browser shows opaque network error instead of 404 message; `Retry-After` unreadable cross-origin | CORS applies headers in `OnStarting`, so they survive the exception handler clearing the response; `WithExposedHeaders("Retry-After")`; integration tests ✅ |
| 4 | **macOS AirPlay occupies port 5000** | API on **5080**, Vite on 5173 |
| 5 | **HTTPS dev-cert friction** for reviewers | HTTP launch profile locally; TLS/HSTS at the reverse proxy, not in the app (see §2.7) |
| 6 | **Vite proxy absent in `vite preview`** | Configure `preview.proxy`; `VITE_API_BASE_URL` for deployments |
| 7 | **Retry amplification** (frontend × backend retries; retrying 429 worsens throttling) | Backend owns the retry budget (≤2, honours `Retry-After`, circuit breaker); frontend retries once on 502/504 only |
| 8 | **Per-IP rate limit behind a proxy** collapses all users into one bucket | `UseForwardedHeaders` trusting only the compose subnet; verified per-client buckets and spoof resistance behind nginx ✅ |
| 9 | **Missing tzdata/ICU in slim containers** → silent fallback to fixed offset (wrong across DST) | Full `aspnet` image (Ubuntu 24.04, tzdata + ICU); warning logged on fallback; verified none in Docker ✅ |
| 10 | **Today's row is partial** during market hours | UI footnote; round once after averaging |
| 11 | **Fewer days/bars than expected** (holidays, half-days, new listings) | No code assumes fixed counts; tests for short/empty series |
| 12 | **Special characters in symbols** (`^`, `=`, `-`) | `encodeURIComponent` / `Uri.EscapeDataString`; route accepts them; tested both sides |
| 13 | **Client aborts logged as 500s** | `MarketDataExceptionHandler` treats request-aborted cancellation as non-error (499, debug log) ✅ |
| 14 | **Chart y-axis from 0** flattens price lines | Round-tick price domain hugging the data; volume in its own chart (no dual axis) ✅ |
| 15 | **Recharts in jsdom / React 19** | Recharts 3.x; `ResizeObserver` stub in Vitest setup; chart tests mock `ResponsiveContainer` with a fixed size ✅ |
| 16 | **`VITE_*` env baked at build time** | Accepted for MVP (see deferrals) |
| 17 | **Yahoo API is unofficial** (no SLA, ToS restrictions, may add cookie/crumb auth) | `IMarketDataProvider` abstraction; nightly live contract test; configurable host |
| 18 | **GitHub Actions IPs throttled by Yahoo** | Live job non-blocking, single retry, clear reporting |
| 19 | **Float32 artifacts** in Yahoo numbers | Parsed directly into `decimal` (verified exact); averaged at full precision; rounded once |
| 20 | Volume overflow | Not an issue: `long` in .NET; JS safe to 9e15 vs ~1e11 max observed |
| 21 | StrictMode double-fetch in dev | Expected; TanStack Query de-duplicates |

---

## 6. Setup & run, GitHub, CI

### 6.1 Two supported ways to run
- **Native (dev loop)** — .NET 10 SDK + Node 24: `dotnet run` the API on **:5080** (HTTP profile),
  `npm run dev` on :5173 with the Vite proxy. No certs, no env vars.
- **Docker Compose (one command, reviewer-friendly)** — only Docker required, which removes the
  ".NET 10 SDK not installed" failure mode.
  - `api`: multi-stage `sdk:10.0` → `aspnet:10.0` (the full image — **Ubuntu 24.04** on .NET 10, not
    the Debian assumed in planning — with tzdata + ICU; verified Asia/Tokyo resolves and no
    fixed-offset fallback is logged). Restore runs from project + lock files only for layer caching
    (`--locked-mode`). Non-root `app` user (uid 1654), port 8080, **not published to the host**.
    Healthcheck uses bash's `/dev/tcp` (no curl/wget in the image).
  - `web`: `node:24-alpine` build → **`nginx-unprivileged`** (non-root, :8080) serving the SPA and
    reverse-proxying `/api` → `api:8080` (same origin, no CORS — mirrors a production topology).
    Security headers (CSP, nosniff, frame-deny, referrer policy) from one included snippet, since
    nginx doesn't inherit `add_header` into locations that set their own; hashed assets cached for a
    year, `index.html` `no-cache`; gzip. CSP allows inline *styles* only (React/Recharts style
    attributes) — verified no CSP violations in the browser.
  - Both containers: `read_only` root filesystem + tmpfs `/tmp`, `cap_drop: [ALL]`,
    `no-new-privileges` (verified: `CapEff 0`, `NoNewPrivs 1`). `web` waits on `api` healthy.
  - Fixed compose subnet (`172.28.0.0/24`) so the API trusts `X-Forwarded-For` only from that
    network. **Verified**: with the limit lowered to 3/min, the host client got 429 on its 4th
    request, spoofing `X-Forwarded-For` didn't help (nginx appends the real IP; the API reads the
    last hop), and a second client (inside the `web` container) still had its own allowance.
  - Base images pinned by **tag + digest**; Dependabot (`docker` ecosystem) bumps both.
- No Makefile/task runner: not available on Windows by default; explicit README commands are clearer.

### 6.2 GitHub
- **Public repo**, default branch **`main`**. Conventional Commits. **PR per milestone.**
- **Dependabot**: weekly, grouped per ecosystem (nuget, npm, github-actions, dotnet-sdk; docker
  added with the Dockerfiles).
- **CodeQL advanced setup** (`.github/workflows/codeql.yml`; C#, TypeScript, Actions;
  `security-and-quality` queries; C# uses `build-mode: none`). Runs on **every** PR, pushes to
  `main` and weekly. See the code-scanning note below for why not default setup.

#### Branch protection — a repository **ruleset** on `main`, kept as code
- Defined in `.github/rulesets/main.json` and applied with
  `gh api -X POST repos/{owner}/{repo}/rulesets --input .github/rulesets/main.json` (use `PUT
  …/rulesets/{id}` to update). Rulesets over classic branch protection: auditable bypasses,
  layerable, visible to readers of a public repo, and reproducible from a checked-in file.

| Rule | Setting | Why |
|---|---|---|
| Restrict deletions / block force pushes | on | `main` can't be deleted or have history rewritten |
| Require a pull request | on, **0 approvals** | Solo repo — GitHub doesn't allow approving your own PR, so ≥1 would force a bypass on every merge (see deferrals for team settings) |
| Require conversation resolution | on | Open review threads (incl. AI code-review comments) block merge |
| Required status checks `backend`, `frontend` | on, **pinned to the GitHub Actions app** (integration 15368) | A status posted by another integration under the same name can't satisfy the check; `docker` added in milestone 6 |
| Require branches up to date | on | PRs are tested against current `main`, not a stale base |
| Require linear history | on | Pairs with the merge methods below |
| Code scanning (CodeQL) | high-or-higher security alerts / errors block merge | Requires CodeQL results on every PR — hence advanced setup (below) |
| Bypass list | **empty** | Rules apply to the admin too; changing the ruleset is itself audited |

- `require_extra_approval_for_unattributed_changes` is a newer, undocumented ruleset parameter that
  GitHub defaults to `true`; left at the default (our commits are attributed) and verified it does
  not block normal merges (PR #5 merged under it).

**Lesson — code-scanning gate vs. CodeQL default setup.** The first version of the ruleset used
CodeQL *default* setup. Default setup did not analyse PR #5 (JSON/Markdown only), so the
`code_scanning` rule waited for results that never came and **blocked a docs-only PR
indefinitely**. Fix: rule temporarily removed, CodeQL moved to an advanced-setup workflow that
runs on every PR, default setup disabled (the two can't coexist), and the rule re-added once the
workflow had produced results. Takeaway: a merge gate has to be exercised against *every kind*
of PR (code, docs, config, Dependabot) before it's considered done.

#### Merge settings
- **Merge commits off** (linear history). **Squash** (PR title + body) for Dependabot/fix-up PRs;
  **rebase** for milestone PRs so the curated test-first commit sequence stays visible on `main`.
- Delete head branches on merge; "suggest updating PR branches" on; auto-merge off.

### 6.3 CI (`.github/workflows/ci.yml`, PRs + pushes to `main`)
- `permissions: contents: read`; `concurrency` with `cancel-in-progress`; **actions pinned to
  commit SHAs** (supply-chain hardening; Dependabot keeps them current).
- **backend**: setup-dotnet from `global.json` + NuGet cache → `restore --locked-mode`
  (`packages.lock.json` enabled) → `dotnet format --verify-no-changes` → Release build
  (warnings as errors) → tests excluding `Category=Live`, with coverage, under `TZ=Asia/Tokyo`
  → ReportGenerator (pinned as a local tool in `dotnet-tools.json`) summary to the job summary; TRX + coverage artifacts.
- **frontend**: setup-node from `.nvmrc` + npm cache → `npm ci` → lint → typecheck → tests with
  coverage (under `TZ=America/Los_Angeles`) → build.
- **docker**: `compose build` → `up -d --wait` → smoke test through nginx (`/healthz`, `index.html`,
  and an invalid symbol that the API itself rejects with 400 — proves nginx → API without calling
  Yahoo) → logs on failure → `down`.
- **No path filters**: path-filtered *required* checks leave PRs stuck "waiting for status"; both
  jobs are fast enough to always run.

### 6.4 Live contract workflow (`live-contract.yml`)
- Weekdays at 15:00 UTC (during the US session, so in-session responses are exercised — changed
  from "nightly") + `workflow_dispatch`; runs the explicit `Category=Live` tests
  (`-- --explicit only`) against real Yahoo with one retry.
- **Not a required check** — never blocks merges; failure notifies the owner (early warning for
  upstream changes; tolerant of runner-IP throttling).

### 6.5 Build order (one PR each)
1. Scaffold — `global.json`, `.editorconfig`, `.gitattributes`, empty solution + Vite app, CI green
   on empty projects.
2. Backend core — domain records, `DailyAggregator`, service (test-first).
3. Yahoo infrastructure — client, DTOs, filters, error mapping, resilience, cache, fixture tests.
4. API host — endpoint, validation, ProblemDetails, CORS, rate limiting, OpenAPI, health,
   integration tests.
5. Frontend — API layer, hook, form, table, chart, states, tests.
6. Docker/Compose + docker CI job + live contract workflow.
7. Docs — README, `PROMPT_LOG.md` (with author rationale blocks), manual-changes section.

---

## 7. Deliberate deferrals

Things consciously **not** built for the MVP, why, and what would trigger revisiting them.

| Deferred | Why not now | Revisit when |
|---|---|---|
| Separate `Domain` project | Domain is 3 records + a pure function; a project would be ceremony | Domain gains real behaviour (indicators, portfolios, corporate actions) |
| `Result<T>` across layers | Boilerplate without `?` propagation; global handler needed anyway | Richer validation / multiple failure types per call |
| Metadata in the response (currency, name, timezone) | Brief mandates the bare-array format | Add a separate `/stocks/{symbol}` metadata endpoint |
| Currency display in the UI | Not in the contract (see above); GBp vs USD makes guessing wrong | Metadata endpoint exists |
| Configurable `range` / `interval` | Brief fixes "last month"; 15m data limited to 60 days | Product asks for other windows — use a validated allow-list |
| Extended-hours data (`includePrePost`) | Mostly zero-volume bars that distort averages | Explicit product need; would be a separate, opt-in series |
| Distributed cache (Redis) | Single instance; in-memory `HybridCache` suffices | Horizontal scale-out — config-only switch |
| Serilog / OpenTelemetry exporters | Built-in `ILogger` structured logging is enough for MVP | Deployed to an environment with a log/trace backend |
| OpenAPI-generated TS client | Adds codegen to build/CI for a single endpoint | ~3+ endpoints, or a second consumer of the API |
| TanStack Table (sorting/paging) | ~22 rows; semantic `<table>` suffices | Larger datasets or interactive table needs |
| Client-side router | Single view; URL search params cover shareable state | A second page/route is added |
| Component library (MUI etc.) | Bundle weight for one view; Tailwind covers it | Design system requirements emerge |
| Light/dark theme toggle | Follows the OS setting (`prefers-color-scheme`); a toggle adds state and a persisted preference | Users ask for it |
| MSW in the browser (dev mock mode) | Tests use MSW in Node only; postinstall worker copy denied | Frontend work needs to proceed without the backend |
| Authentication / API keys | Public read-only data; per-IP rate limit protects upstream quota | Any user-specific data or paid upstream |
| Playwright end-to-end tests | Integration tests on both sides of the HTTP boundary cover the contract; E2E adds CI complexity (both apps + stubbed upstream) | Multiple user flows / pages |
| Mutation testing (Stryker) | Heavyweight for an MVP | Test-suite quality becomes a concern |
| Hard coverage thresholds | Encourage coverage-chasing tests; coverage is reported instead | Team agrees on a meaningful gate (e.g. Application/Infrastructure only) |
| Runtime frontend config (`config.json`) | `VITE_*` build-time env is fine for a single deployment | Build-once / deploy-to-many-environments pipeline |
| .NET Aspire orchestration | Extra SDK workload for reviewers; Compose already gives one-command run | Multiple services / need for the Aspire dashboard & telemetry |
| CD / deployment, image publishing (GHCR) | No target environment | A hosting target exists — publish images on tags |
| Release versioning / changelog automation | Single MVP release | Regular releases (e.g. release-please) |
| Required PR approvals, stale-review dismissal, CODEOWNERS review | Solo repo — self-approval isn't possible | Second contributor joins: 1+ approvals, dismiss stale, require code owners |
| Required signed commits | GitHub's "rebase and merge" produces unsigned commits, conflicting with the merge strategy; optional SSH signing locally | Team policy requires provenance — switch to squash-only + required signatures |
| Dependabot auto-merge | Want eyes on dependency updates while the project is young | Stable test suite — auto-merge green patch updates |

---

## 8. Open items / verification

- [ ] Verify the trailing-snapshot rule against a **live (in-session)** Yahoo response — all probes
      so far were post-close (weekend).
- [ ] Verify the CodeQL code-scanning merge gate on a **docs-only PR** (PR #6 contained code, so it
      didn't prove the deadlock fix).
- [x] Forwarded-headers trust verified behind nginx in Docker (see §6.1).
- [ ] Add `docker` to the ruleset's required checks once the job has run on a PR.
- [ ] Retry behaviour of the resilience pipeline is configured but not exercised by a test (the
      standard handler's exponential backoff would make it slow); covered by library tests and the
      live contract test.

---

## 9. Manual changes (outside AI)

Changes made directly by the author, detected when reviewing merged code. Rationale to be filled
in by the author.

| Where | Change | Rationale |
|---|---|---|
| `StockSymbol.MaxLength` (PR #6) | 20 → **14** | _(author to fill in)_ |
| `DailySummaryAggregator.Aggregate` (PR #6) | Added XML `<summary>` | _(author to fill in)_ |

---

## 10. End-of-project checklist

- [ ] **Generate `PROMPT_LOG.md`** from this session's prompts. For each entry include: the prompt,
      what it was trying to achieve, what was kept/changed/rejected from the AI output and why —
      **plus an empty `My rationale:` block beside each prompt for the author to fill in manually.**
- [ ] Write README "Design & Trade-offs" section from this file (sections 1–7).
- [ ] Document any manual (non-AI) changes and their reasoning (deliverable #4) — see §9.
