# V1 → V2 engineering requirements map

Source reviewed: `IREI_Stage1_MVP_V1_to_V2_Engineering_Change_Spec 20026-08-20.docx`, the V2 blank/demo workbooks, the eight page screenshots, and the condensed visual summary.

| Requirement | Priority | Implementation | Verification |
|---|---:|---|---|
| FR-01 assessment versioning | P0 | `OrganizationPortfolioState`, `AssessmentVersionRecord`, working/approved lifecycle | API smoke test creates and approves a version |
| FR-02 compare reviews | P0 | `VersionComparisonEngine` produces deterministic `FIELD_HISTORY` and `CHANGE_EVENT` records | Second-version comparison covered by source/static checks and workflow tables |
| FR-03 effective/source/verified dates | P0 | source cutoff, created, approved and verified dates stored separately; evidence has effective/source/verified dates | API output and workbook sync |
| FR-04 action / decision persistence | P0 | organization-level `ActionRecord`; create/update/complete APIs; material changes auto-link to actions | API smoke test creates and persists an action |
| FR-05 evidence provenance | P0 | source document hashes, metric/entity/source/location mappings, evidence replacement lineage | API output omits storage paths |
| FR-06 report snapshots | P0 | report registry, frozen workbook copy, approval and sharing gates, supersedes link | API smoke test approves and downloads a valid workbook |
| Property drill-through | P1 | clickable property register row and evidence-based property detail panel | DOM/static UI verification |
| Forward events | P1 | debt, agreement and obligation events with date/amount/source fields | Overview and financial page event tables |
| Eight distinct screens | P0 | exact 1–8 sidebar navigation and dedicated renderers | `tests/verify_v2.py` |
| No synthetic score | P0 | status-based assessment areas; no `/100` or readiness-score calculation | `tests/verify_v2.py` |
| Non-AI mode | P0 | AI advisor/configuration removed; canonical and header/pattern rules only | `tests/verify_v2.py` |
| Missing evidence remains visible | P0 | nullable metrics and `Not assessed` display; warnings and review queues | UI rendering and source checks |
| Preserve template formulas | P0 | canonical mapper skips formula cells; patcher requests full recalculation | workbook/formula verification |

## Screen deltas

| Page | V2 behavior |
|---:|---|
| 1 Portfolio Overview | current condition, prior-version changes, priorities, alerts, property risk and forward calendar |
| 2 Organization & Governance | organization profile, governance/capacity evidence, decision rights and persistent obligation register |
| 3 Properties / Portfolio | persistent asset register, included scope, unit classifications, risk, exceptions and row drill-through |
| 4 Financial Resilience | reporting-period financials, DSCR trend, change drivers, debt maturity and forward events |
| 5 Mission & Public Value | affordability/supportive classifications, agreement restrictions and source-backed outcomes |
| 6 Decisions, Risks & Actions | active and completed lifecycle with owner, due date, decision body, status, risk and evidence |
| 7 Evidence & Data Quality | source uploads, metric lineage, verification status, missing/conflicting evidence and review queue |
| 8 Reports & Authorized Use | report/version registry, approval/share controls, recipient/purpose/scope and frozen downloads |
