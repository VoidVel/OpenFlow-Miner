# Benchmarks — honest scale (NFR-2)

> Every number here comes from an actual run of [`benchmarks/OpenFlowMiner.Benchmarks`](../benchmarks/OpenFlowMiner.Benchmarks), not an estimate. The v1 size ceiling is a **measured** figure, not a guessed one — reproduce it with `dotnet run -c Release --project benchmarks/OpenFlowMiner.Benchmarks`.

## What is measured

For synthetic logs of growing size (~8–16 events per case, 8 activities, a couple of alternate paths so variants and bottlenecks are non-trivial), the harness measures:

- **build ms** — time to group + chronologically sort events into an `EventLog`.
- **mine ms** — time to run **all four** miners end-to-end (DFG + variants + statistics + bottlenecks).
- **retained MB** — live heap held by one stored log after building. This is the ceiling driver for the default in-memory store, not the CPU time.

## Results

Measured on: **8 logical cores, Windows 11 (10.0.26200), .NET 10.0.9**, Release build.

| Events | Cases | Build (ms) | Mine all 4 (ms) | Total (ms) | Retained / log (MB) |
|-------:|------:|-----------:|----------------:|-----------:|--------------------:|
| 1,000 | 85 | 0.5 | 1.6 | 2.2 | 0.1 |
| 10,000 | 838 | 1.9 | 28.9 | 30.8 | 0.7 |
| 50,000 | 4,189 | 12.6 | 41.8 | 54.4 | 3.1 |
| 100,000 | 8,326 | 22.2 | 110.1 | 132.2 | 3.8 |
| 250,000 | 20,783 | 91.6 | 129.9 | 221.5 | 11.6 |
| 500,000 | 41,607 | 158.1 | 215.8 | 373.9 | 19.4 |
| 1,000,000 | 83,331 | 364.9 | 346.3 | **711.3** | **38.8** |

## The v1 ceiling — and why

**Tested responsive up to 1,000,000 events / ~83k cases**: full end-to-end mining in **under 1 second** (~0.7s) with **~39 MB** retained per stored log. This is the figure the API enforces.

The engine is comfortably faster than the honest-scale target (NFR-6 asked only for "tens of thousands of events responsively" — it does 1M in sub-second). The practical limit for the default deployment is **memory, not CPU**: because logs are held in memory, retained footprint (~39 MB per 1M-event log) is what bounds how many logs a small self-host or demo box can hold at once, not mining time.

### How the API applies this

- `POST /api/v1/event-logs` rejects a log above the configured ceiling with **`413 Payload Too Large`** and a `problem+json` body stating the limit — it degrades *loudly*, never silently (NFR-2).
- The default ceiling is **1,000,000 events**, set in `appsettings.json` (`Ingestion:MaxEvents`). Operators running on constrained hardware can lower it; nothing about the contract changes.

### Caveats (stated, not hidden)

- Synthetic data with 8 activities and short cases is a favourable, regular shape. Real logs with very high activity cardinality or very long cases will allocate more per event; treat the retained figures as an optimistic-but-representative lower bound.
- These are single-log, single-threaded figures. Concurrent uploads on the in-memory store add retained memory linearly — the TTL sweeper and the size cap exist to bound that on a public demo.
- Mining is synchronous in v1 (Open Question Q2): at this ceiling that is well within a request timeout. If a future dataset exceeds the sync budget, the async job pattern noted in the plan applies without changing existing endpoints.
