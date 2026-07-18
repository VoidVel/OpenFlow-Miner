# OpenFlow Miner

> A free, open-source, **domain-agnostic** process-mining API for the .NET ecosystem.
> Point a CSV of timestamped events at it and get back a process map — what your process *actually* does, reconstructed from evidence, not from the flowchart someone drew once.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

> **Status: v1 in active development.** See the [milestone roadmap](#roadmap) below. This README describes the target; sections fill in as milestones land.

---

## What it does

Every organization has two versions of a process: the one in the onboarding deck, and the one that actually happens. OpenFlow Miner reconstructs the second from raw event data:

- **Directly-Follows Graph** — which step actually leads to which, weighted by frequency.
- **Process variants** — the distinct end-to-end paths cases really took, ranked by how common they are.
- **Statistics** — case/event counts, average & median durations, most common start/end steps.
- **Bottlenecks** — the steps where time actually piles up.

It is **infrastructure, not an application.** The deliverable is a documented public API; a small reference client exists only to prove the API works. If you deleted the UI, the product would be fully intact.

### Domain-agnostic by construction

The core model cannot express `claimType` or `orderStatus` — only `CaseId`, `Activity`, `Timestamp`, and a generic optional attribute bag. A hospital, a game studio, and a support team all use the exact same API. This is enforced by a test, not by good intentions.

---

## Quickstart

> _Filled in at Milestone 3, when the API surface lands. The whole product will be usable with `curl` and this README alone — no UI required._

---

## Architecture

Three projects, dependencies point strictly inward at a dependency-free core:

```
OpenFlowMiner.Api   ─┐  ASP.NET Core Minimal API + reference client
                     ├─▶ OpenFlowMiner.Core   (domain + mining engine, ZERO 3rd-party deps)
OpenFlowMiner.Ingestion ─┘  CSV (v1) / XES (v2) → Core.Event
```

The mining engine is a standalone library — you can embed it in a console app or another project without dragging in a web server. See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the full engineering rationale.

---

## Roadmap

**v1 (MVP) — in progress**

- [ ] **M0** Skeleton: solution, projects, CI, license
- [ ] **M1** Core mining engine (DFG, variants, statistics, bottlenecks) + console demo
- [ ] **M2** CSV ingestion with flexible column mapping and clear validation errors
- [ ] **M3** Public versioned API, in-memory storage, OpenAPI docs — **plus a real benchmark** whose measured scale ceiling is published in `docs/benchmarks.md`
- [ ] **M4** Reference client + a real [BPI Challenge](https://data.4tu.nl/) dataset backing a no-signup demo
- [ ] **M5** Polish, docs, `v1.0.0`

**v2 (fast-follow, explicitly deferred)**

- Conformance checking — diff mined reality against an expected process model
- XES format support (unlocks the full public benchmark corpus)
- Filtering — by date range, minimum variant frequency (noise reduction), case subset
- Resource/actor dimension — handoff patterns between people or systems
- Streaming / incremental ingestion for live dashboards

---

## License

[MIT](LICENSE). Free to use, self-host, and build on.
