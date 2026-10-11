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
- Yahoo emits bars with `null` OHLC (observed on `EURUSD=X`; also known to occur on halts and
  illiquid symbols). A bar with a null `low` or `high` is excluded so it can't drag averages to
  zero.

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

### 2.3 Error handling: hybrid (exceptions for upstream failures, values for validation)
- **Input validation** (bad symbol format) is an expected user mistake → handled as a value at
  the endpoint (`TypedResults.ValidationProblem`), no exception thrown.
- **Upstream failures** are translated in Infrastructure into a small set of typed exceptions
  (`SymbolNotFoundException`, `UpstreamRateLimitedException`, `UpstreamUnavailableException`) and
  mapped to RFC 7807 ProblemDetails by a single `IExceptionHandler`.
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

### 2.5 Caching via `HybridCache` decorator on the provider
- `CachedMarketDataProvider` decorates `YahooMarketDataProvider`; 5-min TTL (configurable),
  1-min negative cache for not-found.
- Stampede protection matters because Yahoo rate-limits per IP. In-memory today; swappable to
  Redis via configuration.
- Caches the **raw series**, not summaries, so future endpoints can reuse it.

### 2.6 Resilience via `Microsoft.Extensions.Http.Resilience`
- Standard handler: retry with jittered backoff on 429/5xx (not 404), per-attempt + total
  timeout, circuit breaker.

### 2.7 Other host concerns
- Our own per-IP rate limiter (protects the shared Yahoo quota from a single client).
- `/health` endpoint; built-in OpenAPI with Scalar UI in Development.
- Config via Options pattern with `ValidateOnStart` (base URL, User-Agent, interval, range, TTLs).
- CORS policy from config (dev uses the Vite proxy instead).
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
- Retry only on 502/503/504 (never 400/404); `staleTime` 5 min to match the backend cache;
  `placeholderData` keeps the previous result visible while a new symbol loads.

### 3.4 Runtime response validation: zod
- TS types vanish at runtime; zod makes contract drift fail loudly instead of rendering `NaN`.
  `z.infer<>` is the single source of the `DailySummary` type.

### 3.5 Typed `ApiError` from ProblemDetails → friendly messages
| Status | Message |
|---|---|
| 400 | "That doesn't look like a valid ticker." |
| 404 | "No data found for 'XYZ'. Check the symbol (e.g. `BRK-B`, not `BRK.B`)." |
| 503 | "Market data provider is busy — try again in a moment." |
| network / 5xx | Generic message + Retry button |

### 3.6 URL state (`?symbol=TSLA`)
- Shareable, bookmarkable, back-button and refresh friendly. A small `URLSearchParams` hook — no
  router needed for a single view.

### 3.7 Visualization: chart **and** table
- **Recharts** `ComposedChart`: shaded band between `lowAverage` and `highAverage` + volume bars on
  a secondary axis. Declarative, typed, testable.
  - Rejected: TradingView `lightweight-charts` (built for OHLC candles we don't have; imperative
    canvas API), Chart.js (imperative, weaker React integration).
- Semantic `<table>` (newest first, right-aligned tabular numerals) — doubles as the chart's
  accessible equivalent.
- Optional summary stats (period high/low averages, total/avg volume).

### 3.8 Formatting
- `Intl.NumberFormat`: 4 dp for prices; compact volume on axes ("49.1M"), full in the table.
- **No currency symbol** — the bare-array contract carries no currency and some symbols are not
  USD (VOD.L is in GBp/pence). Showing `$` would be wrong.
- `day` is treated as a **string label**, never `new Date("yyyy-MM-dd")` (parses as UTC midnight →
  renders as the previous day for users west of UTC).

### 3.9 Styling: Tailwind CSS (v4, `@tailwindcss/vite`)
- Chosen over CSS Modules for development speed and colocated styling; design tokens via
  Tailwind theme variables; dark mode via the `dark:` variant (follows `prefers-color-scheme`).
- No component library (MUI etc.) — unnecessary bundle weight for a single view.

### 3.10 Input UX & accessibility
- Trim + upper-case; client-side regex mirrors the backend (UX only — backend remains the
  authority). Submit-based search, example chips (TSLA, AAPL, ^GSPC, VOD.L), disabled submit while
  in flight; new search cancels the old one.
- Labelled input, `role="alert"` error region, chart `aria-label` with the table as text
  equivalent, keyboard-operable, contrast-checked in both themes.

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
- **Live Yahoo contract test** tagged `Category=Live`, **excluded from PR CI**, run nightly +
  on demand as an early warning for upstream shape changes.
- Coverage via coverlet + ReportGenerator — **reported, not gated** (thresholds encourage
  coverage-chasing tests).

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
| 1 | **Decimal keeps its scale in JSON** (verified: `382.70001220703125m` → `382.7000`; Yahoo `2910.0` → `2910.0`) — inconsistent output | `PriceJsonConverter` writes canonical shortest form; rounding stays in the domain; raw-JSON integration test |
| 2 | **`HybridCache` serializes values even in L1** unless `sealed` + `[ImmutableObject(true)]`; `TimeZoneInfo` isn't serializable | Cache a serializable DTO with the timezone **id string**; mark immutable; resolve `TimeZoneInfo` after retrieval |
| 3 | **CORS headers missing on error responses** → browser shows opaque network error instead of 404 message; `Retry-After` unreadable cross-origin | Middleware order (CORS before exception handler/endpoints), `WithExposedHeaders("Retry-After")`, integration test |
| 4 | **macOS AirPlay occupies port 5000** | API on **5080**, Vite on 5173 |
| 5 | **HTTPS dev-cert friction** for reviewers | HTTP launch profile locally; HTTPS redirect/HSTS only outside Development |
| 6 | **Vite proxy absent in `vite preview`** | Configure `preview.proxy`; `VITE_API_BASE_URL` for deployments |
| 7 | **Retry amplification** (frontend × backend retries; retrying 429 worsens throttling) | Backend owns the retry budget (≤2, honours `Retry-After`, circuit breaker); frontend retries once on 502/504 only |
| 8 | **Per-IP rate limit behind a proxy** collapses all users into one bucket | `UseForwardedHeaders` with configurable known proxies |
| 9 | **Missing tzdata/ICU in slim containers** → silent fallback to fixed offset (wrong across DST) | Debian-based image or install `tzdata`; log a warning on fallback |
| 10 | **Today's row is partial** during market hours | UI footnote; round once after averaging |
| 11 | **Fewer days/bars than expected** (holidays, half-days, new listings) | No code assumes fixed counts; tests for short/empty series |
| 12 | **Special characters in symbols** (`^`, `=`, `-`) | `encodeURIComponent` / `Uri.EscapeDataString`; route accepts them; tested both sides |
| 13 | **Client aborts logged as 500s** | Treat request-aborted cancellation as non-error (499, debug log) |
| 14 | **Chart y-axis from 0** flattens price lines | Padded `dataMin`/`dataMax` price domain; volume on a secondary axis |
| 15 | **Recharts in jsdom / React 19** | Recharts 3.x; `ResizeObserver` stub in Vitest setup |
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
  - `api`: multi-stage `sdk:10.0` → `aspnet:10.0`, **Debian-based** (tzdata + ICU present), non-root
    `app` user, port 8080, `/health` healthcheck.
  - `web`: `node:24` build → `nginx:alpine` serving static files and reverse-proxying `/api` →
    `api:8080` (same-origin, no CORS — mirrors production topology).
  - `web` waits on `api` being healthy.
- No Makefile/task runner: not available on Windows by default; explicit README commands are clearer.

### 6.2 GitHub
- **Public repo**, default branch **`main`**. Conventional Commits. **PR per milestone.**
- **Dependabot**: weekly, grouped per ecosystem (nuget, npm, github-actions, dotnet-sdk; docker
  added with the Dockerfiles).
- **CodeQL default setup** (C#, TypeScript, Actions).

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
| Code scanning (CodeQL) | high-or-higher security alerts / errors block merge | Added after CodeQL's first analysis completed |
| Bypass list | **empty** | Rules apply to the admin too; changing the ruleset is itself audited |

- `require_extra_approval_for_unattributed_changes` is a newer, undocumented ruleset parameter that
  GitHub defaults to `true`; left at the default (our commits are attributed) and verified it does
  not block normal merges.

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
- **docker**: `compose build` → `up -d --wait` → `curl /health` through nginx → `down`
  (keeps Dockerfiles/proxy config honest without calling Yahoo).
- **No path filters**: path-filtered *required* checks leave PRs stuck "waiting for status"; both
  jobs are fast enough to always run.

### 6.4 Live contract workflow (`live-contract.yml`)
- Nightly `schedule` + `workflow_dispatch`; runs `Category=Live` against real Yahoo with one retry.
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

---

## 9. End-of-project checklist

- [ ] **Generate `PROMPT_LOG.md`** from this session's prompts. For each entry include: the prompt,
      what it was trying to achieve, what was kept/changed/rejected from the AI output and why —
      **plus an empty `My rationale:` block beside each prompt for the author to fill in manually.**
- [ ] Write README "Design & Trade-offs" section from this file (sections 1–7).
- [ ] Document any manual (non-AI) changes and their reasoning (deliverable #4).
