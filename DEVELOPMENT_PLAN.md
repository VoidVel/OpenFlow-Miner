# OpenFlow Miner — Engineering Development Plan

> Engineering response to the Product & Business Analysis Document (v1 Draft).
> This document owns the *technology, architecture, and stack* decisions the PRD deliberately left open.
> Product intent is authoritative; everything below is in service of it.

---

## 0. Guiding engineering principles (derived from the PRD)

These are the non-negotiables that every code review checks against. They map directly to the Product Principles (§4) and Success Criteria (§10).

| # | Principle | Concrete enforcement |
|---|---|---|
| E-1 | **The mining engine is a library, the API is a host.** | `OpenFlowMiner.Core` has **zero** third-party NuGet dependencies and no reference to ASP.NET. You could `dotnet add package` it into a console app. (Success §10.4) |
| E-2 | **Domain-agnostic by type system, not discipline.** | The core model literally cannot express `claimType`. The only optional escape hatch is a generic `Attributes` dictionary — never a typed domain field. (Principle §4.1) |
| E-3 | **The API contract is the deliverable.** | OpenAPI spec is generated and committed; contract tests fail the build if the shape drifts. (Principle §4.2, NFR-1) |
| E-4 | **Zero-friction read path.** | The public demo needs no signup to *read* a pre-loaded real dataset. (Principle §4.3) |
| E-5 | **Honest about scale.** | A documented, benchmarked ceiling. No silent degradation, no unverified claims. (Principle §4.4, NFR-2/6) |
| E-6 | **Deviation is the endgame.** | Conformance checking (v2) is designed-for now — the model doesn't need rework to add it later. (Principle §4.5) |

---

## 1. Stack decisions & rationale

| Concern | Decision | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Current LTS as of 2026; long support window suits an OSS project people will self-host for years. |
| Language | **C# 14**, `nullable` + `ImplicitUsings` enabled, warnings-as-errors in CI | Records + non-nullable reference types give us a self-documenting, immutable domain model for near-free. |
| API style | **ASP.NET Core Minimal API** | The whole surface is ~6 endpoints. Minimal API keeps the host thin and the contract readable in one file; no MVC ceremony. |
| API docs | **Built-in OpenAPI** (`Microsoft.AspNetCore.OpenApi`) + **Scalar** UI | Native OpenAPI generation in .NET 9/10; Scalar is a modern, dependency-light doc UI. Swagger UI is a fine alternative. (NFR-1) |
| CSV parsing | **Sylvan.Data.Csv** | Fastest managed CSV reader; streaming, low-allocation — matches the "honest about scale" value better than a convenience-first library. Isolated in the Ingestion project so Core stays pure. |
| XES parsing (v2) | `System.Xml.XmlReader` (streaming) | XES is XML; a streaming reader handles the large BPI logs without loading the whole DOM. No library needed. |
| Storage (default) | **In-memory store with TTL**, behind an interface | Zero-config self-host + protects the public demo from unbounded growth. Interface means no lock-in. (NFR-4, Open Q4) |
| Reference client | **Static HTML + vanilla JS + Cytoscape.js**, served from `wwwroot` | No build step, no npm toolchain, no framework churn. Proves the API with `fetch()` alone — reinforcing "API is the product." (§8, FR-10) |
| Tests | **xUnit** + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) | Unit-test the engine in isolation; integration-test the real HTTP contract. |
| License | **MIT** | Maximally permissive, zero ambiguity, best for adoption/discoverability. Apache-2.0 is the alternative if patent-grant is wanted. (NFR-5) |

**Deliberately avoided for MVP:** a real database, a message queue, background job framework, Docker-compose sprawl, any auth provider, any frontend build pipeline. Each is a v2 pluggable concern, not a v1 blocker (NFR-6).

---

## 2. Solution architecture

Dependency direction is strictly **inward** — the arrow of dependency always points at `Core`, which points at nothing.

```
                 ┌─────────────────────────────┐
                 │   OpenFlowMiner.Api          │  ASP.NET Core Minimal API
                 │   (HTTP, OpenAPI, wwwroot)   │  + reference client
                 └───────────────┬─────────────┘
                                 │ depends on
              ┌──────────────────┴───────────────────┐
              ▼                                       ▼
 ┌──────────────────────────┐          ┌──────────────────────────┐
 │ OpenFlowMiner.Ingestion  │          │ OpenFlowMiner.Core        │
 │ CSV (v1) / XES (v2)      │──────────▶│ Domain model              │
 │ external format ▶ Event  │  uses    │ Mining engine             │
 └──────────────────────────┘          │ Store abstraction         │
                                        │ ZERO 3rd-party deps       │
                                        └──────────────────────────┘
```

Why this exact split:
- **Core** is the thing the PRD says must be extractable (§10.4). It knows about `Event`, `Trace`, `DirectlyFollowsGraph`, `Variant`, statistics — and *nothing* about HTTP, CSV, or storage engines.
- **Ingestion** is where all the messy real-world format-mapping lives (flexible column names, bad timestamps, XES quirks). Keeping it out of Core means Core never grows a `CsvHelper` dependency (E-1).
- **Api** is a thin host: routing, validation-to-HTTP translation, OpenAPI, and serving the static client.

---

## 3. Core domain model (the part that must stay domain-agnostic)

All records, all immutable. This is the entire vocabulary of the product.

```csharp
// The atom. Note: no domain fields. Resource + Attributes are the ONLY optionality.
public sealed record Event(
    string CaseId,
    string Activity,
    DateTimeOffset Timestamp,
    string? Resource = null,                                   // FR-14 (v2) — optional actor
    IReadOnlyDictionary<string, string>? Attributes = null);  // generic escape hatch — never typed

// One case = one process instance, events in time order.
public sealed record Trace(string CaseId, IReadOnlyList<Event> Events);

// An ingested log: the traces + identity + provenance.
public sealed record EventLog(string Id, IReadOnlyList<Trace> Traces, LogProvenance Provenance);

// Mining outputs
public sealed record ActivityNode(string Activity, int Frequency);
public sealed record DfEdge(string From, string To, int Frequency);
public sealed record DirectlyFollowsGraph(
    IReadOnlyList<ActivityNode> Nodes,
    IReadOnlyList<DfEdge> Edges,
    IReadOnlyList<string> StartActivities,
    IReadOnlyList<string> EndActivities);

public sealed record Variant(int Rank, IReadOnlyList<string> Sequence, int CaseCount, double Percentage);

public sealed record LogStatistics(
    int CaseCount, int EventCount,
    TimeSpan AverageCaseDuration, TimeSpan MedianCaseDuration,
    IReadOnlyList<ActivityNode> TopStartActivities,
    IReadOnlyList<ActivityNode> TopEndActivities);

public sealed record Bottleneck(string Activity, TimeSpan AverageTimeToNext, int Observations);
```

> **The domain-agnostic guarantee, made testable:** a unit test (`DomainModel_ExposesNoIndustrySpecificFields`) reflects over the `Core` domain types and asserts the public property set is exactly this whitelist. A PR that adds `OrderStatus` fails CI. That turns Principle §4.1 from a hope into a gate.

---

## 4. Mining engine

Four small, independently testable, pure functions. No I/O, no state, deterministic.

| Component | FR | Algorithm | Complexity |
|---|---|---|---|
| `DirectlyFollowsMiner` | FR-4, FR-5 | Group events by case → sort by timestamp → count consecutive `(A→B)` pairs; first/last activity per case feed start/end sets. | O(n log n) |
| `VariantAnalyzer` | FR-6 | Reduce each trace to its activity sequence → hash the sequence → group → rank by count. | O(n) |
| `StatisticsCalculator` | FR-7 | Per-case first/last timestamp for durations; frequency tallies for start/end activities; median via sorted durations. | O(n log n) |
| `BottleneckDetector` | FR-8 | For each `(A→B)` transition accumulate `B.time − A.time`; average per source activity; rank descending. | O(n) |

Design notes:
- **Timestamp ties** (two events, same case, identical timestamp) are resolved by stable input order, and this rule is *documented* — process logs have coarse timestamps and this is a real correctness edge case.
- Engine operates over `IReadOnlyList<Event>` / `IEnumerable<Event>`, so it's streaming-friendly and ready for FR-15 (incremental ingestion) without a redesign.
- Every miner is a stateless class (or static method) → trivially unit-testable with hand-authored 5-event fixtures, matching the PRD's Section-5 worked example exactly (so the docs example *is* a test).

---

## 5. API surface (the contract that is the product)

Versioned under `/api/v1`. **Synchronous mining for MVP** — justified in §6.

| Method | Route | Purpose | FR |
|---|---|---|---|
| `POST` | `/api/v1/event-logs` | Ingest a CSV (multipart). Body/query carries the **column mapping** (`caseId`, `activity`, `timestamp`, optional `resource`, `timestampFormat`). Returns `201` + `{ id, summary }`. | FR-1, FR-2 |
| `GET` | `/api/v1/event-logs/{id}` | Retrieve stored log metadata for repeat analysis. | FR-3 |
| `GET` | `/api/v1/event-logs/{id}/dfg?format=json\|dot\|mermaid` | The directly-follows graph. | FR-5 |
| `GET` | `/api/v1/event-logs/{id}/variants?top=N` | Ranked process variants. | FR-6 |
| `GET` | `/api/v1/event-logs/{id}/statistics` | Case/event counts, durations, start/end activities. | FR-7 |
| `GET` | `/api/v1/event-logs/{id}/bottlenecks?top=N` | Ranked bottleneck activities. | FR-8 |

**Fascinating-but-simple touch:** the `dfg` endpoint offers `format=mermaid` and `format=dot` in addition to JSON. A user can paste the Mermaid output straight into GitHub/Notion and *see their real process* with zero tooling. Near-zero implementation cost, high delight, and it underlines "usable via `curl` alone" (Principle §4.2). JSON stays the canonical machine format (FR-5).

**Error contract (NFR-3):** every failure returns RFC 9457 `application/problem+json`:
```json
{ "type": "https://openflowminer.dev/errors/unparseable-timestamp",
  "title": "Unparseable timestamp",
  "status": 422,
  "detail": "Row 47: value '2020-13-01' is not a valid date for column 'timestamp'.",
  "row": 47 }
```
Validation errors (FR-2) are `422` with the offending row and column — never a generic `500`.

---

## 6. Answers to the PRD's Open Questions (§12)

**Q1 — Size ceiling for v1.** The ceiling is a **hypothesis, not an assertion**. NFR-2 commits us to honesty about *tested* scale, not confidence about *guessed* scale — so v1 does **not** publish a number up front. Milestone 3 runs an actual benchmark (§10, §11-M3) and `docs/benchmarks.md` reports the **measured** ceiling, whatever it turns out to be. Once measured, the API enforces it with a `413 Payload Too Large` carrying the documented limit — degrade *loudly*, per NFR-2. (Working expectation is "comfortably covers tens of thousands of events per NFR-6," but no specific figure is claimed until a run backs it.)

**Q2 — Sync vs async mining.** **Synchronous for v1.** At the §Q1 ceiling, full DFG + variants + stats compute in well under a request timeout, so `POST /event-logs` mines eagerly and the artifacts are then served from cache. This keeps the contract dead-simple (no job/polling model to document or test). *The contract is designed so async is additive:* if a v2 dataset exceeds the sync budget, `POST` can return `202 + { jobId }` and a `GET /jobs/{id}` appears — existing endpoints are untouched.

**Q3 — Auth model.** **No auth for MVP self-host.** For the *public demo*, protect it without a signup wall (E-4) via: (a) request **rate limiting** (built-in ASP.NET Core rate limiter), (b) the §Q1 **size cap**, and (c) the §Q4 **TTL** on uploads. An optional API-key middleware is stubbed behind config for operators who want it — off by default.

**Q4 — Storage/retention.** **Ephemeral, TTL'd, in-memory** by default (e.g. sliding 24h expiry), behind `IEventLogStore` (NFR-4). The public demo keeps one **pinned, permanent** real BPI log (E-4, §10.2) that never expires, plus TTL'd user uploads. Swapping in a durable store (SQLite/Postgres/blob) is an interface implementation, never a contract change.

---

## 7. Storage abstraction

```csharp
public interface IEventLogStore
{
    Task<string> SaveAsync(EventLog log, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<EventLog?> GetAsync(string id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
}
```
- **`InMemoryEventLogStore`** (default, ships in v1): `ConcurrentDictionary` + a background sweeper honoring TTL; a `ttl: null` entry is the pinned demo log.
- Computed artifacts (DFG/variants/stats) are **cached per log id** so repeat GETs don't re-mine.
- Interface lives in **Core**; the in-memory impl can live in Core too (no external deps). Durable adapters (v2) go in their own project so Core stays pristine (E-1).

---

## 8. Reference client (secondary, by design — §8/FR-10)

- Single `wwwroot/index.html` + `app.js` + `style.css`. **No framework, no bundler.**
- Upload a CSV → map columns in a tiny form → `fetch` the API → render the DFG with **Cytoscape.js** (frequency = edge thickness), plus a variants table and a stats panel.
- It uses **only the public API** — no privileged backchannel. If someone deletes `wwwroot`, the product is fully intact (Principle §4.2, Non-goal §9). The client is a *proof*, not a feature.

---

## 9. Repository layout

```
OpenFlowMiner.sln
README.md                     # what v1 does + explicit v2 roadmap (§10.3), quickstart, curl examples
DEVELOPMENT_PLAN.md           # this file
LICENSE                       # MIT
CONTRIBUTING.md               # incl. the "no domain-specific fields" rule as contributor law
.editorconfig                 # style + warnings-as-errors
.github/workflows/ci.yml      # build, test, publish OpenAPI artifact
src/
  OpenFlowMiner.Core/         # domain, mining engine, IEventLogStore, InMemory store — no 3rd-party deps
  OpenFlowMiner.Ingestion/    # CsvEventLogReader (v1), XesEventLogReader (v2)
  OpenFlowMiner.Api/          # Minimal API, OpenAPI, Problem Details, rate limiting, wwwroot/
tests/
  OpenFlowMiner.Core.Tests/         # engine correctness + the domain-agnostic reflection test
  OpenFlowMiner.Ingestion.Tests/    # malformed-CSV / timestamp-format cases
  OpenFlowMiner.Api.Tests/          # WebApplicationFactory contract + error-shape tests
samples/
  data/                       # small trimmed real BPI slice for tests/demo, WITH attribution
  requests/                   # ready-to-run .http / curl scripts
docs/
  benchmarks.md               # the honest scale numbers (E-5)
```

---

## 10. Testing strategy

- **Engine unit tests** drive the exact §5 worked example from the PRD as fixtures — the documentation example is a passing test, so docs can't rot.
- **The domain-agnostic guard test** (reflection whitelist, §3) — the single most important test for the product's identity.
- **Ingestion tests** cover the FR-2 promises: missing timestamp, unparseable date, empty case id, reordered/renamed columns, alternate timestamp formats → each maps to a specific `problem+json`.
- **API contract tests** via `WebApplicationFactory`: real HTTP round-trips asserting status codes, Problem Details shape, and `format=mermaid/dot` output.
- **A tiny benchmark** (BenchmarkDotNet or a timed smoke test) that produces the published scale numbers (E-5, NFR-2).
- CI: `dotnet build -warnaserror` + `dotnet test`, and export the generated `openapi.json` as a build artifact so contract drift is visible in PRs (E-3).

---

## 11. Delivery roadmap (milestones)

Each milestone is independently demoable and leaves the repo in a shippable state.

### Milestone 0 — Skeleton (½ day)
Solution, 3 src + 3 test projects, `.editorconfig`, CI, MIT license, README stub with roadmap. `dotnet build` green.

### Milestone 1 — Core engine, no API (1–2 days) → *proves E-1/§10.4*
Domain records; `DirectlyFollowsMiner`, `VariantAnalyzer`, `StatisticsCalculator`, `BottleneckDetector`; the domain-agnostic guard test; §5 worked-example tests. **Deliverable:** a console `Program` that reads a CSV and prints the DFG — the engine works before any HTTP exists.

### Milestone 2 — Ingestion + validation (1 day) → *FR-1, FR-2*
`CsvEventLogReader` with flexible column mapping; the full malformed-input test suite → Problem Details.

### Milestone 3 — API + storage + **benchmark** (2 days) → *FR-3, FR-5–9, NFR-1/2/3*
Minimal API endpoints; `InMemoryEventLogStore` + TTL sweeper; OpenAPI + Scalar; Problem Details middleware; rate limiter; `dot`/`mermaid` exporters; contract tests. **Plus a dedicated benchmarking step** (BenchmarkDotNet or a timed harness) that mines synthetic logs of growing size to find the point where sync mining / memory stops being responsive → the **measured** ceiling is written to `docs/benchmarks.md` and becomes the enforced `413` limit (§6-Q1). **Deliverable:** full product usable via `curl` + docs (Success §10.1), and a real scale number that traces to an actual run.

### Milestone 4 — Reference client + real demo data (1–2 days) → *FR-10, §10.2, §11*
Cytoscape.js client in `wwwroot`; pin a real trimmed **BPI Challenge** log as the no-signup demo, with attribution. **Deliverable:** visitor sees a real mined process instantly.

### Milestone 5 — Polish & publish (1 day) → *§10.3, NFR-2*
README with quickstart + roadmap + honest scale numbers; `CONTRIBUTING.md` (the domain-agnostic rule as law); `benchmarks.md`; sample `.http` files; tag `v1.0.0`.

**MVP total: ~7–9 focused days.**

### v2 fast-follow (roadmap, README-visible — PRD §7)
FR-11 Conformance checking · FR-12 XES ingestion (unlocks the full BPI corpus) · FR-13 Filtering (date range / min-frequency noise reduction / case subset) · FR-14 Resource dimension · FR-15 Streaming ingestion (+ optional durable store).

---

## 12. Traceability matrix (every FR/NFR has a home)

| Req | Where it's satisfied |
|---|---|
| FR-1 | §5 `POST /event-logs`, §Ingestion, M2 |
| FR-2 | §5 error contract, §10 ingestion tests, M2 |
| FR-3 | §5 `GET /event-logs/{id}`, §7 store, M3 |
| FR-4/5 | §4 `DirectlyFollowsMiner`, §5 `dfg` endpoint, M1/M3 |
| FR-6 | §4 `VariantAnalyzer`, §5 `variants`, M1/M3 |
| FR-7 | §4 `StatisticsCalculator`, §5 `statistics`, M1/M3 |
| FR-8 | §4 `BottleneckDetector`, §5 `bottlenecks`, M1/M3 |
| FR-9 | §5 whole surface, OpenAPI, M3 |
| FR-10 | §8 reference client, M4 |
| FR-11–15 | §11 v2 roadmap; model designed to absorb them (E-6, §3/§4 notes) |
| NFR-1 | OpenAPI + Scalar, CI artifact, M3/M5 |
| NFR-2 | §6-Q1, `docs/benchmarks.md`, M5 |
| NFR-3 | §5 Problem Details, M3 |
| NFR-4 | §7 `IEventLogStore`, M3 |
| NFR-5 | MIT `LICENSE`, M0 |
| NFR-6 | Sync/in-memory MVP, v2 deferrals, whole plan |

---

*End of engineering plan. Ready to scaffold Milestone 0 on approval.*
