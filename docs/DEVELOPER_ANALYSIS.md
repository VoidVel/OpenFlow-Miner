# OpenFlow Miner — End-to-End Developer Analysis

> **Purpose of this document.** A complete, implementation-level walkthrough of the codebase as it
> actually exists after Milestones 0–4. It is written to be the single source of truth from which
> user-facing documentation (README, API reference, tutorials, architecture guide) can be authored.
> It describes *what is built and why*, down to method behaviour, error codes, status codes, ordering
> guarantees, and measured numbers. Where a value is asserted (e.g. a benchmark figure or a mined
> statistic), it traces to a real run or a committed ground-truth fixture, not an estimate.
>
> Audience: engineers writing docs, reviewing the design, extending the product, or onboarding.
> Companion documents: [`DEVELOPMENT_PLAN.md`](../DEVELOPMENT_PLAN.md) (the forward-looking plan),
> [`docs/benchmarks.md`](benchmarks.md) (scale), [`expected_results.md`](../expected_results.md)
> (hand-computed ground truth), [`samples/data/ATTRIBUTION.md`](../samples/data/ATTRIBUTION.md)
> (demo dataset provenance).

---

## Table of contents

1. [What the product is](#1-what-the-product-is)
2. [Solution topology & dependency rules](#2-solution-topology--dependency-rules)
3. [The core domain model](#3-the-core-domain-model)
4. [The mining engine](#4-the-mining-engine)
5. [The ingestion layer](#5-the-ingestion-layer)
6. [The storage layer](#6-the-storage-layer)
7. [Graph export (Mermaid / DOT)](#7-graph-export-mermaid--dot)
8. [The HTTP API](#8-the-http-api)
9. [Configuration reference](#9-configuration-reference)
10. [The reference client](#10-the-reference-client)
11. [Demo data & seeding](#11-demo-data--seeding)
12. [Benchmark & measured scale](#12-benchmark--measured-scale)
13. [End-to-end data flows](#13-end-to-end-data-flows)
14. [Testing](#14-testing)
15. [Build, tooling & CI](#15-build-tooling--ci)
16. [Cross-cutting design decisions](#16-cross-cutting-design-decisions)
17. [Known limitations & non-goals](#17-known-limitations--non-goals)
18. [Extension points & v2 roadmap](#18-extension-points--v2-roadmap)
19. [Glossary](#19-glossary)
20. [Appendix: file-by-file index](#20-appendix-file-by-file-index)

---

## 1. What the product is

OpenFlow Miner is a **domain-agnostic process-mining API** for the .NET ecosystem. You submit a
timestamped event log (CSV), and it reconstructs — purely from evidence — what a process *actually*
does: the graph of which step follows which, the distinct end-to-end paths cases take, summary
statistics, and where time concentrates.

Three properties define its identity and every design choice serves them:

- **The API is the product.** A minimal reference web client exists only to prove the API works; if
  it were deleted, the product would be intact. Everything is reachable with `curl` + docs alone.
- **Domain-agnostic by construction.** The core model can express only *case / activity / timestamp*
  (+ optional resource / generic attributes). It has no way to represent `orderStatus` or `claimType`.
  This is enforced by a test, not by convention.
- **The mining engine is a standalone library.** `OpenFlowMiner.Core` has **zero third-party
  dependencies** and no web/storage/serialization concerns. It is embeddable in any .NET app.

Runtime: **.NET 10 (LTS)**, C# 14, `nullable` enabled, `TreatWarningsAsErrors` on.

---

## 2. Solution topology & dependency rules

Solution file: `OpenFlowMiner.slnx` (the .NET 10 XML solution format). Eight projects:

| Project | Kind | Depends on | Third-party packages |
|---|---|---|---|
| `src/OpenFlowMiner.Core` | class lib | *(nothing)* | **none** |
| `src/OpenFlowMiner.Ingestion` | class lib | Core | `Sylvan.Data.Csv` |
| `src/OpenFlowMiner.Api` | ASP.NET Core web | Core, Ingestion | `Microsoft.AspNetCore.OpenApi`, `Microsoft.OpenApi`, `Scalar.AspNetCore` |
| `samples/OpenFlowMiner.Demo` | console | Core, Ingestion | none |
| `benchmarks/OpenFlowMiner.Benchmarks` | console | Core | none |
| `tests/OpenFlowMiner.Core.Tests` | xUnit | Core | xUnit |
| `tests/OpenFlowMiner.Ingestion.Tests` | xUnit | Core, Ingestion | xUnit |
| `tests/OpenFlowMiner.Api.Tests` | xUnit | Core, Api | xUnit, `Microsoft.AspNetCore.Mvc.Testing` |

**The dependency rule (enforced by project references): the arrow always points inward toward Core,
and Core points at nothing.** This is what makes Core genuinely extractable.

```
        Api  ──►  Ingestion  ──►  Core  ──►  (BCL only)
         └──────────────────────►  ▲
   Demo ──►────────────────────────┤
   Benchmarks ──►──────────────────┘
```

Design rationale for the split:
- **Core** owns the *problem domain*: what an event is and how to mine it. No format parsing, no HTTP.
- **Ingestion** absorbs all the messy real-world I/O (flexible CSV columns, bad timestamps). Keeping it
  out of Core is what lets Core stay dependency-free — the `Sylvan` CSV dependency lives here only.
- **Api** is a thin host: routing, serialization, OpenAPI, static files, and translating domain
  results / ingestion errors into HTTP.

Shared build settings live in [`Directory.Build.props`](../Directory.Build.props) (see §15).

---

## 3. The core domain model

Namespace `OpenFlowMiner.Core.Domain`. All types are **immutable `record`s**. This is the entire
vocabulary of the product — deliberately small.

### 3.1 `Event` — the atom

```csharp
public sealed record Event(
    string CaseId,
    string Activity,
    DateTimeOffset Timestamp,
    string? Resource = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
```

The single most important type. It carries only the three universals of any process — *which case*,
*what happened*, *when* — plus two **generic** optional extension points: `Resource` (an actor/system,
FR-14) and `Attributes` (a free-form bag). There is intentionally **no** typed industry field.
`DateTimeOffset` (not `DateTime`) is used so offsets are preserved/normalized unambiguously.

### 3.2 `Trace` — one case

```csharp
public sealed record Trace(string CaseId, IReadOnlyList<Event> Events);
```

Represents one process instance with its events **in chronological order**. Computed members:
- `ActivitySequence` → `IReadOnlyList<string>` of activity names in order (allocates a fresh array).
- `Start` / `End` → first/last event timestamp, `null` for an empty trace.
- `Duration` → `End - Start`, or `TimeSpan.Zero` if fewer than two events.

### 3.3 `EventLog` — the unit of work

```csharp
public sealed record EventLog(string Id, IReadOnlyList<Trace> Traces, LogProvenance Provenance);
```

Computed: `CaseCount` (= `Traces.Count`), `EventCount` (sum of events).

The canonical constructor is the static factory:

```csharp
public static EventLog FromEvents(string id, IEnumerable<Event> events, LogProvenance provenance)
```

It **groups events by `CaseId` (ordinal)** and **orders each case by `Timestamp`**. Because
`OrderBy` in .NET is a **stable sort**, events with identical timestamps within a case keep their
original input order. This is the documented tie-break rule and matters because real logs have
coarse timestamps. Guards: throws `ArgumentException` on empty `id`, `ArgumentNullException` on null
`events`/`provenance`. An empty event sequence yields an empty (0-case) log — a valid state.

### 3.4 `LogProvenance` — origin/attribution

```csharp
public sealed record LogProvenance(string Source, string? Attribution = null);
public static LogProvenance Unknown { get; } = new("unknown");
```

Carries a human-readable origin and an optional citation line — used so the demo can honestly say
"this is mining a real municipal permit process" and credit the dataset.

### 3.5 Mining output types

| Type | Shape | Notes |
|---|---|---|
| `ActivityNode` | `(string Activity, int Frequency)` | a node + total occurrence count |
| `DfEdge` | `(string From, string To, int Frequency)` | directed, frequency-weighted |
| `DirectlyFollowsGraph` | `(Nodes, Edges, StartActivities, EndActivities)` | the DFG |
| `Variant` | `(int Rank, IReadOnlyList<string> Sequence, int CaseCount, double Percentage)` | 1-based rank, % of 0–100 |
| `LogStatistics` | `(CaseCount, EventCount, AverageCaseDuration, MedianCaseDuration, TopStartActivities, TopEndActivities)` | durations are `TimeSpan` |
| `Bottleneck` | `(string Activity, TimeSpan AverageTimeToNext, int Observations)` | avg wait to the next event |

### 3.6 The domain-agnostic guarantee (enforced)

[`DomainAgnosticGuardTests`](../tests/OpenFlowMiner.Core.Tests/DomainAgnosticGuardTests.cs) reflects
over the domain assembly and asserts:
1. `Event`'s public data properties are **exactly** `{CaseId, Activity, Timestamp, Resource,
   Attributes}` (compiler-generated `EqualityContract` excluded).
2. **No** public property on any `OpenFlowMiner.Core.Domain` type contains a forbidden industry term
   (`order, claim, ticket, patient, invoice, customer, loan, payment, product, policy, account,
   shipment, diagnosis`).

A PR adding `ClaimType` fails CI. This converts Product Principle 4.1 from an intention into a gate.

---

## 4. The mining engine

Namespace `OpenFlowMiner.Core.Mining`. Four **static, pure, deterministic** classes — no I/O, no
shared state, fully reproducible ordering. All operate over an `EventLog`.

### 4.1 `DirectlyFollowsMiner.Mine(EventLog) → DirectlyFollowsGraph` (FR-4/FR-5)

Algorithm (single pass over traces):
- For each trace: increment the **start** counter for `events[0].Activity` and the **end** counter for
  `events[^1].Activity`.
- For each position `i`: increment the **node** counter for `events[i].Activity`; if `i+1` exists,
  increment the **edge** counter for `(events[i].Activity → events[i+1].Activity)`.
- Emit nodes ordered by **frequency desc, then activity name (ordinal)**; edges ordered by **frequency
  desc, then From, then To**; start/end activity lists ranked by frequency desc then name.

Complexity: **O(n log n)** dominated by the final sorts (the counting pass is O(n)). Empty traces are
skipped. A single-event case contributes a node and a start=end activity but **no edge**.

### 4.2 `VariantAnalyzer.Analyze(EventLog, int? top = null) → IReadOnlyList<Variant>` (FR-6)

Groups traces by their **activity sequence** using a custom `IEqualityComparer<IReadOnlyList<string>>`
(`SequenceComparer` — element-by-element ordinal equality, `HashCode`-combined). Ranks groups by
**count desc**, with a deterministic **lexicographic** tie-break (`SequenceOrder`,
`IComparer<IReadOnlyList<string>>`). Assigns 1-based `Rank`; `Percentage = 100 * count / totalCases`.
`top` optionally truncates. Empty log → empty list. Negative `top` → `ArgumentOutOfRangeException`.

> **Implementation note / rationale.** An earlier version keyed groups by joining the sequence with a
> control-char separator; it was replaced with structural comparison because it is robust to any
> activity name (including odd characters) and avoids an unprintable literal in source.

### 4.3 `StatisticsCalculator.Calculate(EventLog, int topActivities = 5) → LogStatistics` (FR-7)

- `CaseCount` / `EventCount` from the log.
- **Average** case duration: sums `Duration.Ticks` as `long` (avoids `TimeSpan` intermediate overflow
  on large logs) and divides by count.
- **Median**: sorts durations; even count → mean of the two middle values; odd → the middle value.
- **Top start/end activities**: frequency of each trace's first/last activity, ranked desc then name,
  taking `topActivities`.
- Empty traces are excluded from duration/endpoint stats; an all-empty log yields zeros and empty lists.

### 4.4 `BottleneckDetector.Detect(EventLog, int? top = null) → IReadOnlyList<Bottleneck>` (FR-8)

For every adjacent pair `(a → b)` in a trace, accumulates `(b.Timestamp - a.Timestamp).Ticks` keyed by
the **source** activity `a`, and counts observations. `AverageTimeToNext = totalTicks / count`. Ranks
by average desc, then activity name. Because events are pre-sorted chronologically, every delta is
non-negative. A **terminal** activity (always last) has no outgoing transition and therefore never
appears — verified by test.

### 4.5 Determinism & ground truth

Every miner produces a stable, fully-ordered result for a given input (no reliance on hash iteration
order in outputs). Correctness is pinned to **two independent hand-authored fixtures**:
- The Section-5 worked example (2 cases) — see [`SampleLogs.cs`](../tests/OpenFlowMiner.Core.Tests/SampleLogs.cs).
- `sample_event_log.csv` (3 cases, 13 events) — cross-checked against [`expected_results.md`](../expected_results.md):
  DFG edges, 2 variants (66.7% / 33.3%), **avg 58h20m**, **median 55h**, worst bottleneck
  **Payment Received 34h30m**, `Delivered` excluded.

---

## 5. The ingestion layer

Namespace `OpenFlowMiner.Ingestion`. Converts an external CSV into `Event`s, validating each row and
reporting problems specifically. Backed by `Sylvan.Data.Csv` (fast, streaming, low-allocation).

### 5.1 `CsvColumnMapping`

```csharp
public sealed record CsvColumnMapping(
    string CaseId = "case_id",
    string Activity = "activity",
    string Timestamp = "timestamp",
    string? Resource = null,
    IReadOnlyList<string>? TimestampFormats = null);
public static CsvColumnMapping Default { get; } = new();
```

Columns are matched **by header name, case-insensitively**, so callers are never forced into a fixed
order or fixed names (FR-1). `TimestampFormats`, when supplied, switches parsing to exact-format mode.

### 5.2 `CsvEventLogReader.Read(...)`

Overloads accept a `Stream` or a `TextReader` (+ optional mapping; defaults to `Default`). Flow:

1. Create a `CsvDataReader`. A `CsvFormatException` (e.g. unreadable content) → single `empty-file`
   error. `FieldCount == 0` → `empty-file`.
2. **Resolve columns** (`TryResolveColumns`): build a case-insensitive header→ordinal map; each
   unresolved required column produces a `missing-column` error (row 0). If any are missing, return
   failure immediately (rows can't be interpreted).
3. **Read rows** (`ReadRows`): iterate; a per-row counter starts at **1 for the first data row**
   (the header is not counted). For each row, trim `caseId` / `activity` / `timestamp`:
   - empty `caseId` → `missing-case-id`
   - empty `activity` → `missing-activity`
   - empty `timestamp` → `missing-timestamp`; else unparseable → `unparseable-timestamp`
   - a row with **no** errors contributes an `Event` (optional resource captured, empty→null).
   A structurally broken row (too few columns, etc.) is caught and reported as `malformed-row` — it
   never crashes the parse.
4. Header present but **zero data rows** → `empty-file`.

### 5.3 Validation model & error codes

`IngestionError(string Code, string Message, int Row = 0, string? Column = null)`. `Row` is the
**1-based data-row index, header excluded**; `Row = 0` means file-level. Codes
(`IngestionErrorCode`): `empty-file`, `missing-column`, `missing-case-id`, `missing-activity`,
`missing-timestamp`, `unparseable-timestamp`, `malformed-row`.

`IngestionResult` is **atomic**: `Success(events)` (all rows valid) **or** `Failure(errors)` with
`Events` empty. There is no partial/silently-trimmed load. `IsSuccess => Errors.Count == 0`. It
deliberately returns a flat `IReadOnlyList<Event>` (not an `EventLog`) so the log **identity is
assigned by the storage layer**, keeping ingestion free of any storage concern.

### 5.4 Timestamp parsing

`DateTimeOffset` parsing with `DateTimeStyles.AssumeUniversal | AdjustToUniversal` and
`CultureInfo.InvariantCulture`. If `TimestampFormats` is set, uses `TryParseExact` over those formats;
otherwise `TryParse` (general). This handles ISO-8601 `2026-01-01T09:00:00Z` **and** the real-world
`2011-10-11 13:45:40.276000+02:00` (space-separated, microseconds, offset) — both verified.
`2026-01-13-01T09:05:00Z` (month 13, extra segment) correctly fails → `unparseable-timestamp`.

### 5.5 The FR-2 contract, proven

Running the committed [`malformed_event_log.csv`](../malformed_event_log.csv) through ingestion yields
exactly three distinct, row-numbered errors — **no stack trace, no generic message, nothing dropped**:

| Data row | Content | Code |
|---|---|---|
| 2 | `case-101,Payment Received,2026-01-13-01T09:05:00Z` | `unparseable-timestamp` |
| 3 | `case-102,Order Placed,` | `missing-timestamp` |
| 5 | `,Payment Received,2026-01-01T09:10:00Z` | `missing-case-id` |

Row 4 (`case-102,Shipped,<valid>`) is **well-formed on its own** and is therefore *not* an error — the
implemented policy is **per-row independent validation** (a valid row is accepted even if a sibling in
the same case is broken; the broken sibling is still reported). This is one of the two acceptable
behaviours the fixture's spec calls out, and it is documented here as the chosen one.

---

## 6. The storage layer

Namespace `OpenFlowMiner.Core.Storage`. Lives in Core (interface + default impl) so the public API
never assumes a database or cloud (NFR-4).

### 6.1 `IEventLogStore`

```csharp
Task<string> SaveAsync(EventLog log, TimeSpan? ttl = null, CancellationToken ct = default);
Task<EventLog?> GetAsync(string id, CancellationToken ct = default);
Task<bool> ExistsAsync(string id, CancellationToken ct = default);
```

`ttl == null` **pins** an entry permanently (used for demo logs); otherwise it expires after the span.

### 6.2 `InMemoryEventLogStore`

`ConcurrentDictionary<string, Entry>` where `Entry = (EventLog Log, DateTimeOffset? ExpiresAt)`. A
`TimeProvider` (default `TimeProvider.System`, injectable for tests) supplies "now". Expiry is enforced
**lazily** on `Get`/`Exists` (an expired entry is removed on access) **and** proactively via
`RemoveExpired()` (returns count removed). `Count` reports live (non-expired) entries. This is the
only shipped store; durable adapters (SQLite/Postgres/blob) are a v2 concern and would be new
implementations of the same interface — no contract change.

### 6.3 Sweeper (API host)

[`EventLogStoreSweeper`](../src/OpenFlowMiner.Api/EventLogStoreSweeper.cs) is a `BackgroundService`
that calls `RemoveExpired()` on a **10-minute** `PeriodicTimer`, logging when it evicts anything. It
depends on the concrete `InMemoryEventLogStore` (because `RemoveExpired` is impl-specific), which is
why the API registers the store both concretely and as the interface (§8.2).

---

## 7. Graph export (Mermaid / DOT)

Namespace `OpenFlowMiner.Core.Export`. `GraphExport` renders a `DirectlyFollowsGraph` into two
shareable text formats — the "fascinating but simple" feature. JSON remains the canonical machine
format; these are zero-friction human formats.

- **`ToMermaid`** → a `graph LR` flowchart. Nodes are declared with stable ids `n0, n1, …` (assigned
  in node order) and quoted labels; edges render `nX -->|frequency| nY`. Quotes in labels are escaped
  to `&quot;` so Mermaid parsing can't break. Output is directly pasteable into GitHub/Notion.
- **`ToDot`** → Graphviz `digraph process { rankdir=LR; … }` using quoted activity names as node ids
  (with `\` and `"` escaped), node labels `"Activity (freq)"`, edge labels = frequency.

Content types over HTTP: Mermaid → `text/plain`; DOT → `text/vnd.graphviz`.

---

## 8. The HTTP API

Project `OpenFlowMiner.Api`, ASP.NET Core **Minimal API** on .NET 10. Entry point
[`Program.cs`](../src/OpenFlowMiner.Api/Program.cs); endpoints in
[`EventLogEndpoints.cs`](../src/OpenFlowMiner.Api/Endpoints/EventLogEndpoints.cs).

### 8.1 Middleware pipeline (order matters)

`UseExceptionHandler` → `UseStatusCodePages` → `UseRateLimiter` → `MapOpenApi` →
`MapScalarApiReference` → `UseDefaultFiles` → `UseStaticFiles` → `MapEventLogEndpoints` → seed demo
logs → `Run`.

### 8.2 Dependency injection

- `InMemoryEventLogStore` as a **singleton**, and `IEventLogStore` resolved to the *same* instance
  (so endpoints use the interface and the sweeper uses the concrete type).
- `CsvEventLogReader` singleton (stateless).
- `EventLogStoreSweeper` hosted service.
- `AddProblemDetails()` — consistent RFC 9457 bodies for framework-generated errors too.
- `AddOpenApi()` — the OpenAPI document (NFR-1).
- `AddRateLimiter(...)` — see §8.5.
- Options bound from config: `IngestionOptions` ← `Ingestion`, `StorageOptions` ← `Storage`.

### 8.3 Endpoint reference

All under `/api/v1/event-logs`. Responses are JSON (`JsonSerializerDefaults.Web`, camelCase) unless noted.

| Method | Route | Query | Success | Error statuses |
|---|---|---|---|---|
| `POST` | `/` | — (multipart form) | `201 Created` + `EventLogSummaryDto` + `Location` | 400, 413, 415, 422 |
| `GET` | `/{id}` | — | `200` + `EventLogSummaryDto` | 404 |
| `GET` | `/{id}/dfg` | `format=json\|mermaid\|dot` | `200` + `DfgResponse` / text | 400 (bad format), 404 |
| `GET` | `/{id}/variants` | `top` (int > 0) | `200` + `VariantDto[]` | 400 (top≤0), 404 |
| `GET` | `/{id}/statistics` | — | `200` + `StatisticsResponse` | 404 |
| `GET` | `/{id}/bottlenecks` | `top` (int > 0) | `200` + `BottleneckDto[]` | 400 (top≤0), 404 |

**POST specifics.** The handler reads the multipart form manually (`request.ReadFormAsync`, endpoint
marked `.DisableAntiforgery()`), takes the `file` field (or the first file), and reads optional
`caseId` / `activity` / `timestamp` / `resource` form fields to build the `CsvColumnMapping` (defaults
when blank). It parses via `CsvEventLogReader`; on failure returns `422` (see §8.4); on success checks
`Events.Count` against `IngestionOptions.MaxEvents` (→ `413` if exceeded); otherwise generates a
12-char id (`Guid.NewGuid().ToString("N")[..12]`), builds the `EventLog`, saves with the configured
upload TTL, and returns `201` with the summary and a `Location` header.

`EventLogSummaryDto` is computed by mining the log (`DirectlyFollowsMiner` + `VariantAnalyzer`) to
report `activityCount` and `variantCount` alongside case/event counts and provenance.

### 8.4 Error contract (RFC 9457 `application/problem+json`)

Every failure is a `problem+json` body with a stable `type` URL under
`https://openflowminer.dev/errors/`. Problem type slugs: `event-log-not-found` (404),
`event-log-validation` (422), `event-log-too-large` (413), `invalid-top` (400), `unsupported-format`
(400), `unsupported-media-type` (415), `no-file` (400).

The **validation** problem (422) carries a machine-readable `errors` array — the ingestion errors
surfaced verbatim:

```json
{
  "type": "https://openflowminer.dev/errors/event-log-validation",
  "title": "Event log validation failed",
  "status": 422,
  "detail": "3 row(s) could not be ingested. Nothing was stored.",
  "errors": [
    { "code": "unparseable-timestamp", "message": "Timestamp '2026-01-13-01T09:05:00Z' is not a valid date/time.", "row": 2, "column": "timestamp" },
    { "code": "missing-timestamp", "message": "Timestamp is empty.", "row": 3, "column": "timestamp" },
    { "code": "missing-case-id", "message": "Case ID is empty.", "row": 5, "column": "case_id" }
  ]
}
```

### 8.5 Rate limiting

`AddRateLimiter` with a global **fixed-window** partitioned limiter keyed by client IP: **100
requests / minute**, rejection status **429**. This protects a public demo without a signup wall
(Open Question Q3): the read path stays free and unauthenticated.

### 8.6 Response DTOs (`Contracts/`)

The API exposes explicit DTOs rather than serializing domain types directly, so the public contract is
deliberate and stable. Durations are exposed **both** as machine-friendly seconds and a human string
(e.g. `averageCaseDurationSeconds: 210000`, `averageCaseDuration: "2.10:20:00"`). `ResponseMapper`
converts each mined artifact. `IngestionErrorDto(Code, Message, Row, Column)` is the serialized error
shape.

### 8.7 Docs endpoints

- `/openapi/v1.json` — generated OpenAPI document (verified 200; a contract test asserts this).
- `/scalar/v1` — Scalar interactive API reference UI.
- `/` — the reference client (`wwwroot/index.html`).

---

## 9. Configuration reference

[`appsettings.json`](../src/OpenFlowMiner.Api/appsettings.json):

| Section : Key | Type | Default | Meaning |
|---|---|---|---|
| `Ingestion:MaxEvents` | int | `1000000` | Upload event-count ceiling; above it → `413`. The measured, responsive ceiling (see §12). |
| `Storage:UploadTtlHours` | int | `24` | Retention for uploaded logs. Pinned demo logs ignore this (`ttl=null`). |

Bound to `IngestionOptions` / `StorageOptions` ([`Configuration/ApiOptions.cs`](../src/OpenFlowMiner.Api/Configuration/ApiOptions.cs)).
Operators on constrained hardware can lower `MaxEvents`; nothing about the contract changes.

---

## 10. The reference client

Static assets under [`wwwroot/`](../src/OpenFlowMiner.Api/wwwroot/): `index.html`, `style.css`,
`app.js`, and a **locally bundled** `lib/cytoscape.min.js` (v3.30.2). No build step, no npm, no CDN —
it works offline and self-hosted, and uses **only the public HTTP API** via `fetch` (no privileged
backchannel).

Behaviour (`app.js`):
- On load, and on "Load demo", it calls `GET /{id}` + `/dfg` + `/variants?top=5` + `/bottlenecks?top=5`
  in parallel and renders: a **summary** panel, the **DFG** via Cytoscape (green start nodes / red end
  nodes / blue steps, **edge thickness ∝ frequency**, frequency edge labels, `breadthfirst` layout),
  a **top-variants** list, and a **bottlenecks** list. A "view as Mermaid" link points at
  `/dfg?format=mermaid`.
- Upload form posts a `multipart/form-data` file (+ optional column-mapping fields), then loads the
  returned id. API errors are surfaced from the `problem+json` body (including per-row validation
  errors).
- It defaults to loading the **real** `receipt` dataset on first paint (Success 10.2).

**A real bug caught by visual verification:** the graph originally rendered crammed into a corner
because `renderGraph` ran while its container was still `display:none` (zero size), so Cytoscape laid
out in a zero viewport. Fix: reveal the panels *before* rendering, and force `cy.resize()` +
`cy.fit()` on `layoutstop`. Unit tests could not have caught this — it required driving the actual UI.

---

## 11. Demo data & seeding

Two pinned logs are seeded at startup ([`DemoData.cs`](../src/OpenFlowMiner.Api/DemoData.cs)), both
`ttl=null`:

- **`sample`** — a small, clearly-labelled **illustrative** order-fulfilment log (3 cases, 13 events).
  Built in code; provenance attribution states it is *not* real.
- **`receipt`** — the **real hero dataset**: the receipt phase of a Dutch environmental-permit process
  (WABO, CoSeLoG project), **1,434 cases / 8,577 events / 27 activities / 116 variants**. Shipped as
  [`samples/data/receipt.csv`](../samples/data/receipt.csv) (CC BY; cited in
  [`ATTRIBUTION.md`](../samples/data/ATTRIBUTION.md)), copied into the app output as
  `demo-data/receipt.csv` via a `Content` item in the `.csproj`, and loaded at startup with the mapping
  `case:concept:name` / `concept:name` / `time:timestamp` / `org:resource` (exercising the optional
  resource dimension). Loading is **defensive**: if the file is missing or fails to parse, the app logs
  a warning and starts without it rather than failing.

Seeding is idempotent per process start and logged (`Seeded real demo log 'receipt': 1434 cases,
8577 events.`). A contract test asserts the real log seeds and mines.

---

## 12. Benchmark & measured scale

[`benchmarks/OpenFlowMiner.Benchmarks`](../benchmarks/OpenFlowMiner.Benchmarks) is a **timed harness**
(not micro-benchmarking). It generates synthetic logs (~8–16 events/case, 8 activities, alternate
paths so variants/bottlenecks are non-trivial; deterministic, seeded), and for each size measures
**build ms** (group+sort into an `EventLog`), **mine ms** (all four miners), and **retained MB**
(live heap held by one stored log — baseline taken *before* generation so the delta includes the
events the log retains).

Measured on 8 logical cores, Windows 11, .NET 10.0.9, Release (full table in [`docs/benchmarks.md`](benchmarks.md)):

| Events | Cases | Build (ms) | Mine all 4 (ms) | Total (ms) | Retained / log (MB) |
|-------:|------:|-----------:|----------------:|-----------:|--------------------:|
| 100,000 | 8,326 | 22.2 | 110.1 | 132.2 | 3.8 |
| 500,000 | 41,607 | 158.1 | 215.8 | 373.9 | 19.4 |
| 1,000,000 | 83,331 | 364.9 | 346.3 | **711.3** | **38.8** |

**The v1 ceiling is measured, not guessed** (this was an explicit requirement): tested responsive to
**1,000,000 events** (~0.7 s end-to-end, ~39 MB/log). That figure is what the API enforces via
`Ingestion:MaxEvents`, returning `413` above it. The limiting factor is **retained memory** (the
in-memory store), not CPU. Caveats are documented honestly: synthetic data is a favourable shape;
concurrent uploads add retained memory linearly (bounded by the size cap + TTL sweeper); mining is
synchronous (fine within a request timeout at this ceiling).

An earlier measurement bug (baseline taken *after* the events existed → negative retained deltas) was
found and fixed before publishing.

---

## 13. End-to-end data flows

### 13.1 Ingest (POST) flow
```
client → POST /api/v1/event-logs (multipart CSV + optional mapping)
  → read form, pick file, build CsvColumnMapping
  → CsvEventLogReader.Read(stream, mapping)
       ├─ failure → 422 problem+json { errors:[{code,message,row,column}] }   (nothing stored)
       └─ success → events
  → if events.Count > MaxEvents → 413 problem+json
  → id = 12-char guid; EventLog.FromEvents(id, events, provenance)   (group by case, stable sort)
  → IEventLogStore.SaveAsync(log, ttl = UploadTtlHours)
  → 201 Created + EventLogSummaryDto + Location: /api/v1/event-logs/{id}
```

### 13.2 Query (GET) flow
```
client → GET /api/v1/event-logs/{id}/<artifact>[?params]
  → IEventLogStore.GetAsync(id)   (lazy TTL check; expired → removed → null)
       ├─ null → 404 problem+json
       └─ log  → run the relevant miner (DFG / variants / stats / bottlenecks)
  → map to DTO (or Mermaid/DOT text) → 200
```

### 13.3 Startup seed flow
```
app start → seed 'sample' (in-code, ttl=null)
          → TryLoadReceiptLog(demo-data/receipt.csv) → CsvEventLogReader → EventLog → save ttl=null
          → background sweeper begins (RemoveExpired every 10 min)
```

### 13.4 Library-only flow (no HTTP)
`EventLog.FromEvents(...)` → `DirectlyFollowsMiner.Mine(...)` etc. — demonstrated by
[`samples/OpenFlowMiner.Demo`](../samples/OpenFlowMiner.Demo) (`dotnet run --project
samples/OpenFlowMiner.Demo -- <file.csv>` mines a CSV, or with no arg mines the built-in example).

---

## 14. Testing

**59 tests, all green.** xUnit throughout; the API project uses `WebApplicationFactory<Program>`
(hence `public partial class Program;` in `Program.cs`).

| Project | Count | What it guards |
|---|---:|---|
| `Core.Tests` | 33 | domain factory (grouping, **stable tie order**), the four miners against the §5 worked example + edge cases (empty log, single-event case, odd/even median, top-N), the **domain-agnostic guard**, storage TTL semantics (fake `TimeProvider`), Mermaid/DOT export incl. quote escaping |
| `Ingestion.Tests` | 14 | valid parse, each error code, custom mapping, **column-order independence**, header-only file, **atomicity** (valid rows discarded on any failure), optional resource; fixture-driven tests over the committed `malformed_event_log.csv` (rows 2/3/5 distinct) and `sample_event_log.csv` (mined output = `expected_results.md`) |
| `Api.Tests` | 12 | real HTTP round-trips: demo summary/DFG/mermaid/statistics/bottlenecks, 404 problem+json, `top=0` → 400, POST valid (201 + retrievable), POST malformed (422 with row-numbered errors), POST no-file (400), OpenAPI served, **real receipt log seeded & mineable (1434/8577)** |

Fixtures (`sample_event_log.csv`, `malformed_event_log.csv`) are copied into the Ingestion test output
via a `<None ... CopyToOutputDirectory>` item and located at runtime under `AppContext.BaseDirectory`.

The testing philosophy: **the documentation examples are tests** (the §5 example and
`expected_results.md` are asserted numerically, so they cannot silently rot), and the product's
identity (domain-agnosticism) is a test, not a guideline.

---

## 15. Build, tooling & CI

- **[`Directory.Build.props`](../Directory.Build.props)** centralizes: `TargetFramework=net10.0`,
  `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, **`TreatWarningsAsErrors=true`**,
  `EnforceCodeStyleInBuild=true`, `InvariantGlobalization=true`.
- **[`.editorconfig`](../.editorconfig)** enforces file-scoped namespaces, sorted usings,
  accessibility modifiers, brace style.
- **NU1903 handling.** `Microsoft.OpenApi` (a transitive dependency of Microsoft's own
  `Microsoft.AspNetCore.OpenApi`) carries an **unfixed** security advisory — even the latest version is
  flagged, so it cannot be resolved by pinning. Because warnings-as-errors is a project value, the code
  `NoWarn`s **only NU1903** in **only** the two affected projects (`Api`, `Api.Tests`), documented in
  the `.csproj`. CI keeps the advisory visible via a non-failing `dotnet list package --vulnerable`
  step. Removing the suppression restores the hard gate once a fix ships.
- **[CI](../.github/workflows/ci.yml)** (GitHub Actions): setup .NET 10 → restore → report vulnerable
  packages (informational) → build `-warnaserror` (Release) → test → export OpenAPI artifact (so
  contract drift is visible in PRs).

---

## 16. Cross-cutting design decisions

| Decision | Rationale | Traces to |
|---|---|---|
| Core has **zero** third-party deps | Makes the engine genuinely embeddable/extractable | Principle 4.x, Success 10.4 |
| Explicit inward dependency graph | Format/HTTP/storage concerns never leak into the domain | §2 |
| Domain-agnosticism enforced by reflection test | Turns a principle into a build gate | Principle 4.1 |
| Immutable records everywhere | Thread-safe, self-documenting, cheap value semantics | §3 |
| Stable timestamp tie-break | Deterministic mining for coarse real-world timestamps | §3.3, §4 |
| Ingestion returns events, not an `EventLog` | Storage owns identity; ingestion stays storage-free | §5.2 |
| Atomic ingestion + per-row errors | Loud, specific failures; no half-loaded logs | FR-2, NFR-3 |
| `problem+json` everywhere | Predictable, machine-readable errors for strangers | NFR-3 |
| Durations as seconds **and** string | Friendly to both machines and humans | §8.6 |
| In-memory store behind an interface | Zero-config self-host; no vendor lock-in | NFR-4, Q4 |
| Rate-limit, size-cap, TTL instead of auth | Zero-friction public demo without a signup wall | Q3 |
| Sync mining in v1 | Simplest contract; fine within the measured ceiling | Q2 |
| Measured (not guessed) scale ceiling | Honesty about tested scale | NFR-2 |
| Mermaid/DOT export | High-delight, near-zero-cost, `curl`-usable | Principle 4.2 |
| Bundled Cytoscape, no build step | Self-contained, offline-capable client | §10 |
| Client uses only the public API | Proves "the API is the product" | Principle 4.2 |

---

## 17. Known limitations & non-goals

**Deferred to v2 (explicit):** conformance checking (FR-11), XES ingestion (FR-12), filtering by
date/frequency/case-subset (FR-13), first-class resource analytics (FR-14; the field exists, analytics
don't), streaming/incremental ingestion (FR-15), durable storage adapters, async mining jobs.

**Current limitations (honest):**
- **In-memory only** — logs are lost on restart (except re-seeded demo logs); horizontal scaling isn't
  supported by the default store.
- **Synchronous mining** — a single request mines end-to-end; fine to the measured 1M-event ceiling.
- **CSV only** — no XES yet, so the large public XES benchmark corpus isn't natively consumable.
- **No persistence of computed artifacts** — DFG/variants/stats are recomputed per request (fast at the
  ceiling; a cache is a straightforward future optimization).
- **Node id stability in Mermaid** — ids are positional (`n0…`), stable for a given graph but not a
  durable external identifier.
- **Rate limit is per-process** — a multi-instance deployment would need a distributed limiter.

**Non-goals:** a vertical/industry product, a commercial SaaS, an enterprise-scale Celonis competitor,
a UI-first product, real-time streaming at MVP.

---

## 18. Extension points & v2 roadmap

- **New ingestion formats** (XES, JSON): add a reader in `OpenFlowMiner.Ingestion` producing
  `IReadOnlyList<Event>` + `IngestionError`s; the API/storage are untouched.
- **Durable storage**: implement `IEventLogStore` in a new project; register it instead of the
  in-memory store. No contract change.
- **Conformance checking** (FR-11): a new `OpenFlowMiner.Core.Mining` component comparing a mined DFG /
  variant set against a supplied reference sequence, returning a diff (skipped steps, unexpected loops,
  rare paths). The immutable model already supports it.
- **Async mining**: if a future dataset exceeds the sync budget, `POST` can return `202 + { jobId }`
  with a `GET /jobs/{id}` — existing endpoints unchanged (designed for in §8 / Q2).
- **Filtering** (FR-13): additional query params on the artifact endpoints (date range, min variant
  frequency, case subset) feeding a filtered `EventLog` into the same miners.
- **Artifact caching**: memoize mined artifacts per log id in the store to avoid recomputation.

---

## 19. Glossary

- **Case** — one end-to-end process instance (an order, a permit application, a ticket).
- **Event** — one recorded occurrence within a case (an activity at a timestamp).
- **Activity** — the label/type of an event, independent of case.
- **Trace** — the time-ordered sequence of events for a single case.
- **DFG (Directly-Follows Graph)** — nodes = activities; edge A→B (weighted by frequency) exists if B
  was ever observed immediately after A within a case.
- **Variant** — a distinct end-to-end activity sequence that ≥1 case followed; cases with identical
  sequences are grouped and ranked by frequency.
- **Bottleneck** — an activity ranked by average elapsed time until the next event.
- **Conformance checking** — comparing mined (actual) behaviour against a reference (expected) model.
- **XES** — the XML-based event-log interchange format standard in process-mining research (v2).
- **Provenance** — the origin/attribution of a log.

---

## 20. Appendix: file-by-file index

**`OpenFlowMiner.Core`** (no third-party deps)
- `Domain/Event.cs`, `Trace.cs`, `EventLog.cs`, `LogProvenance.cs`, `DirectlyFollowsGraph.cs`,
  `Variant.cs`, `LogStatistics.cs`, `Bottleneck.cs` — the immutable model (§3).
- `Mining/DirectlyFollowsMiner.cs`, `VariantAnalyzer.cs`, `StatisticsCalculator.cs`,
  `BottleneckDetector.cs` — the four pure miners (§4).
- `Storage/IEventLogStore.cs`, `InMemoryEventLogStore.cs` — persistence abstraction + default (§6).
- `Export/GraphExport.cs` — Mermaid/DOT (§7).

**`OpenFlowMiner.Ingestion`** (dep: Sylvan.Data.Csv)
- `CsvColumnMapping.cs`, `CsvEventLogReader.cs`, `IngestionError.cs`, `IngestionResult.cs` (§5).

**`OpenFlowMiner.Api`** (deps: AspNetCore.OpenApi, OpenApi, Scalar)
- `Program.cs` — host wiring, DI, pipeline, seeding (§8).
- `Endpoints/EventLogEndpoints.cs` — the `/api/v1` surface + problem+json helpers (§8.3–8.4).
- `Contracts/Responses.cs`, `IngestionErrorDto.cs` — response DTOs + `ResponseMapper` (§8.6).
- `Configuration/ApiOptions.cs` — `IngestionOptions`, `StorageOptions` (§9).
- `DemoData.cs` — sample + real receipt seed (§11).
- `EventLogStoreSweeper.cs` — TTL sweeper (§6.3).
- `wwwroot/index.html`, `style.css`, `app.js`, `lib/cytoscape.min.js` — reference client (§10).
- `appsettings.json` — configuration (§9).

**`samples/OpenFlowMiner.Demo`** — CLI that mines a CSV or the built-in example (§13.4).
**`benchmarks/OpenFlowMiner.Benchmarks`** — the scale harness (§12).
**`samples/data/receipt.csv` + `ATTRIBUTION.md`** — the real demo dataset (§11).
**Root fixtures** — `sample_event_log.csv`, `malformed_event_log.csv`, `expected_results.md` (§14).

**Tests** — `Core.Tests/*` (33), `Ingestion.Tests/*` (14), `Api.Tests/*` (12) (§14).

---

*This analysis reflects the codebase through Milestone 4. Milestone 5 (README, CONTRIBUTING, request
samples, v1.0.0 tag) will layer polish on top without changing the architecture described here.*
