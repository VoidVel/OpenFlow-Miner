# Demo dataset attribution

## `receipt.csv` — Receipt phase of an environmental permit application process (WABO), CoSeLoG project

This is a **real, publicly published event log** — the receipt phase of the building-permit
("WABO") application process at a Dutch municipality, collected in the CoSeLoG research project.
OpenFlow Miner uses it (not fabricated data) to back its live demo, per the project's requirement
that the demo mine a genuine process.

- **1,434 cases · 8,577 events** — comfortably within the tested scale ceiling (see `docs/benchmarks.md`).
- **Columns** map cleanly onto OpenFlow Miner's flexible ingestion (and exercise the optional
  resource dimension via `org:resource`):
  | Role | Column |
  |---|---|
  | Case ID | `case:concept:name` |
  | Activity | `concept:name` |
  | Timestamp | `time:timestamp` |
  | Resource | `org:resource` |

### Citation

> Buijs, J.C.A.M. (2014). *Receipt phase of an environmental permit application process ('WABO'),
> CoSeLoG project.* 4TU.ResearchData. Dataset.
> https://doi.org/10.4121/uuid:a07386a5-7be3-4367-9535-70bc9e77dbe6

### License

Published under **Creative Commons Attribution (CC BY)** — redistribution is permitted with
attribution, which this file provides. The CSV rendition here was obtained via the
[pm4py](https://github.com/pm4py/pm4py-core) project's public test data.
