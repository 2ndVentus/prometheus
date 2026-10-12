# Intraday Daily Summary

A full-stack app that takes a stock symbol, pulls the last month of 15-minute intraday data from
Yahoo Finance, and summarises it per trading day: the average of the bar lows, the average of the
bar highs, and the total volume.

- **Backend**: .NET 10 Web API (Minimal APIs), layered as Api → Application ← Infrastructure.
- **Frontend**: React 19 + TypeScript + Vite, with a synced price/volume chart, a table, and the
  instrument's name, exchange, type and quote currency.
- **Run it** with Docker alone, or natively with the .NET SDK and Node.

```json
GET /api/v1/stocks/TSLA/daily-summary

[
  { "day": "2026-10-08", "lowAverage": 371.4284, "highAverage": 372.9249, "volume": 22329809 },
  { "day": "2026-10-09", "lowAverage": 383.0768, "highAverage": 384.9723, "volume": 36336060 }
]
```

---

## Quick start (Docker only)

Requires Docker (Desktop or Engine with Compose v2).

```bash
docker compose up --build
```

Open **http://localhost:8080** and enter a ticker (try `TSLA`, `^GSPC`, `VOD.L`, `7203.T`).

nginx serves the frontend and proxies `/api` to the backend, so there is one origin and no CORS
setup. The API container isn't published to the host. Stop with `Ctrl+C`, or
`docker compose down` if started with `-d`.

---

## Local development

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.x | Pinned in `global.json` (roll-forward to the latest 10.0 feature band) |
| Node.js | 24 LTS | Pinned in `.nvmrc` — run `nvm use` in the repo root |

### Backend (http://localhost:5080)

```bash
dotnet run --project backend/src/StockData.Api --launch-profile http
```

- Health: http://localhost:5080/health
- API reference (Development only): http://localhost:5080/scalar — OpenAPI JSON at `/openapi/v1.json`
- Port 5080, not 5000: macOS AirPlay Receiver occupies 5000.

### Frontend (http://localhost:5173)

In a second terminal:

```bash
cd frontend && npm ci && npm run dev
```

Vite proxies `/api` to `http://localhost:5080`, so no environment variables or CORS settings are
needed. To point at another backend, set `API_PROXY_TARGET` (see `frontend/.env.example`).
`npm run build && npm run preview` serves the production build on :4173 with the same proxy.

### Tests and checks

Backend (from `backend/`):

```bash
dotnet test --solution StockData.slnx
```

Frontend (from `frontend/`):

```bash
npm run lint && npm run typecheck && npm test
```

| Suite | What runs |
|---|---|
| Backend | xUnit v3 on Microsoft.Testing.Platform: unit, fixture-based (real captured Yahoo responses) and `WebApplicationFactory` integration tests. No network. |
| Live contract (opt-in) | Real Yahoo calls through the production pipeline: `dotnet test --project tests/StockData.Infrastructure.Tests -- --explicit only` |
| Frontend | Vitest + Testing Library + MSW, run in the `America/Los_Angeles` timezone to catch date-shifting bugs. |
| Coverage | `npm run test:coverage`; backend: add `-- --coverage --coverage-settings coverage.settings.xml --coverage-output-format cobertura` |

CI runs all of this on every PR (plus a Docker build and smoke test) — see
[CI and repository](#ci-and-repository).

---

## API

### `GET /api/v1/stocks/{symbol}/daily-summary`

Groups the last month of 15-minute bars by the **exchange's local trading day** and returns one
entry per day, oldest first.

| Field | Meaning |
|---|---|
| `day` | Trading day in the exchange's timezone, `yyyy-MM-dd` |
| `lowAverage` | Mean of the day's 15-minute bar lows, at most 4 decimal places |
| `highAverage` | Mean of the day's 15-minute bar highs, at most 4 decimal places |
| `volume` | Total volume traded that day |

**Symbols** are Yahoo tickers, case-insensitive, 1–14 characters of letters, digits and
`. - ^ =`, with at least one letter or digit — e.g. `TSLA`, `BRK-B` (not `BRK.B`), `^GSPC`, `VOD.L`, `EURUSD=X`.

**Errors** are RFC 7807 ProblemDetails (`application/problem+json`) with a `traceId`:

| Status | When |
|---|---|
| 400 | The symbol isn't a valid ticker format (`errors.symbol` explains) |
| 404 | Yahoo has no data for the symbol |
| 429 | You exceeded the per-client rate limit (60/min by default — each symbol lookup in the UI uses two calls); `Retry-After` says when to retry |
| 502 | Yahoo returned an error or an unexpected response |
| 503 | Yahoo is rate-limiting us; `Retry-After` when Yahoo provided one |
| 504 | Yahoo didn't respond in time |

A valid symbol with no trades in the window returns `200 []`.

### `GET /api/v1/stocks/{symbol}`

Descriptive details of the instrument, as reported by the data provider.

```json
{"symbol":"VOD.L","longName":"Vodafone Group Public Limited Company","currency":"GBp","exchangeName":"LSE","instrumentType":"EQUITY"}
```

| Field | Meaning |
|---|---|
| `symbol` | The normalised ticker |
| `longName` | Full name (falls back to the provider's short name) |
| `currency` | Quote currency code; `GBp` means pence sterling |
| `exchangeName` | Exchange, e.g. `NasdaqGS`, `Tokyo` (indices, FX and crypto report terse codes such as `SNP`, `CCY`, `CCC`) |
| `instrumentType` | e.g. `EQUITY`, `ETF`, `INDEX`, `CURRENCY`, `CRYPTOCURRENCY` |

Fields the provider doesn't report are `null`. The symbol rules, errors and rate limit are the same
as for the daily summary.

Both endpoints are served from the **same cached upstream response**, so fetching both for a symbol
(as the UI does) costs a single Yahoo request.

---

## How it works

```
React (Vite / nginx)
  │  GET /api/v1/stocks/{symbol}/daily-summary      GET /api/v1/stocks/{symbol}
  ▼
Api            endpoints · symbol validation · ProblemDetails · rate limit · CORS
  ▼
Application    DailySummaryService → DailySummaryAggregator (pure)       StockSymbol, domain records
               InstrumentService  → series.Instrument (name, currency, exchange, type)
  ▼  IMarketDataProvider
Infrastructure CachedMarketDataProvider (HybridCache, 5 min)
                 └─ YahooMarketDataProvider (typed HttpClient + resilience)
                      └─ YahooChartMapper → bar filters → clean IntradaySeries + InstrumentInfo
```

Data rules worth knowing (details and evidence in [DESIGN_DECISIONS §1](docs/DESIGN_DECISIONS.md#1-upstream-api-yahoo-finance-chart-api)):

- **Days are exchange-local**, using the exchange's IANA timezone, so Tokyo or Sydney sessions that
  cross UTC midnight stay on one day.
- **Prices are averaged in `decimal` and rounded once** to 4 dp, midpoints away from zero.
- **Bars with missing prices are dropped** (lunch breaks in Hong Kong and Tokyo, FX gaps, halts).
- **Yahoo's trailing "last price" snapshot bar is dropped** — only the final bar, only when it has
  zero volume and open = high = low = close. Real flat bars mid-session are kept.

### Project structure

```
backend/
  src/
    StockData.Api/             Minimal API host: endpoints, contracts, error handling, hosting setup
    StockData.Application/     Domain (StockSymbol, records, aggregator), service, provider port, errors
    StockData.Infrastructure/  Yahoo client, DTOs, mapper, bar filters, caching decorator, DI
  tests/                       Api / Application / Infrastructure test projects (+ Yahoo fixtures)
frontend/
  src/
    api/                       fetch client and typed ApiError
    features/daily-summary/    schema, symbol rules, query hook, URL state, form, stats, chart, table
    features/instrument/       instrument details: schema, query hook, labels, header
    components/ lib/ test/     generic UI, formatting, test setup (MSW)
  nginx.conf                   production server + /api reverse proxy
docs/DESIGN_DECISIONS.md       every decision, its reasoning, hidden issues and deferrals
docker-compose.yml
```

---

## Configuration

Backend settings live in `backend/src/StockData.Api/appsettings.json` and can be overridden with
environment variables (`Section__Key`, e.g. `RateLimiting__PermitLimit=60`). All are validated at
startup.

| Setting | Default |
|---|---|
| `MarketData:Yahoo:Interval` / `Range` | `15m` / `1mo` (allow-listed) |
| `MarketData:Yahoo:MaxRetryAttempts` / `AttemptTimeout` / `TotalRequestTimeout` | 2 / 4 s / 10 s |
| `MarketData:Cache:Duration` | 5 minutes |
| `RateLimiting:PermitLimit` / `Window` | 60 / 1 minute, per client |
| `Cors:AllowedOrigins` | none (frontend is same-origin via a proxy) |
| `ForwardedHeaders:KnownNetworks` | none (Docker Compose trusts its own subnet) |

Frontend: `VITE_API_BASE_URL` (build time) is only needed if the API is hosted on a different origin.

---

## Design and trade-offs

The full record — every decision, the alternatives considered, and what changed while building —
is in **[docs/DESIGN_DECISIONS.md](docs/DESIGN_DECISIONS.md)**. Highlights:

- **The daily summary is exactly the brief's array.** Instrument details (name, currency, exchange,
  type) come from a separate endpoint that shares the same cached upstream call. The UI shows the
  currency as a unit in headers rather than a symbol on numbers, so London prices in pence
  (`GBp`) never read as pounds.
- **Three backend projects, no separate Domain project yet**: the compiler enforces that core logic
  doesn't know about Yahoo or HTTP, without ceremony for a small domain.
- **Yahoo's data quirks are handled in Infrastructure**, so the domain only ever sees complete bars.
- **Errors**: invalid input is a value (400); upstream failures are provider-neutral exceptions
  mapped by one handler, with a test guaranteeing every failure type has an HTTP mapping.
- **Resilience**: the backend owns the retry budget (≤ 2 retries, honouring `Retry-After`), caches
  for 5 minutes with stampede protection, and rate-limits per client to protect Yahoo's per-IP quota.
- **Charts**: price and volume are two charts on one time axis rather than a dual-axis chart, with
  a colour-vision-validated palette and a full table as the accessible equivalent.
- **Testing**: real captured Yahoo payloads as fixtures, with expected values computed independently.
- **Deliberately deferred** (with triggers to revisit): a Domain project, `Result<T>`, Redis, OpenTelemetry, OpenAPI client generation, Playwright, authentication, CD — see
  [§8 Deferrals](docs/DESIGN_DECISIONS.md#8-deliberate-deferrals).

Known limitation: Yahoo's chart API is **unofficial** — no SLA, terms that restrict automated use,
and it can change without notice. The `IMarketDataProvider` abstraction keeps a licensed provider
one class away, and a scheduled live contract test gives early warning of changes.

---

## CI and repository

- **CI** (every PR and push to `main`): backend (format, build with warnings as errors, tests in
  the Tokyo timezone, coverage summary), frontend (lint, format, typecheck, tests, build), and a
  Docker job that builds the stack and smoke-tests it through nginx.
- **CodeQL** (C#, TypeScript, Actions) on every PR; high-severity alerts block merging.
- **Live contract** workflow: real Yahoo calls on weekdays during US market hours, and on demand;
  never blocks merges.
- **`main` is protected** by a ruleset kept as code (`.github/rulesets/main.json`): PRs only, linear
  history, required checks, resolved conversations, no bypass.
- **Dependabot** weekly for NuGet, npm, GitHub Actions, the .NET SDK and Docker base images (pinned
  by digest). Actions are pinned to commit SHAs.

---

## AI collaboration

This project was built with Claude (Claude Code, Opus 5.5) as a pair: a design review in six steps (API,
backend, frontend, testing, hidden issues, setup/CI), then seven milestones and an instrument
metadata feature, each delivered as a PR the author reviewed before it was committed. **[PROMPT_LOG.md](PROMPT_LOG.md)** lists every prompt,
what it was for, and what was kept, changed or rejected. The full conversation is in
**[docs/CHAT_TRANSCRIPT.md](docs/CHAT_TRANSCRIPT.md)**.

## Manual changes (outside AI)

Changes the author made directly, outside the AI collaboration:

| Where | Change | Reasoning |
|---|---|---|
| `StockSymbol.MaxLength` (PR #6) | 20 → 14 characters | Matched FINRA standards |
