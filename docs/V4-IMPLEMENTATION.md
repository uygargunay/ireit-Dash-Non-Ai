# V4.2 underwriting pilot implementation

V4.2 adds an authenticated seven-page workspace at `/v4.html`. It reads the supplied V4.2 schema, calculates financial outputs on the server and preserves proposed mappings, source uncertainty and working-assessment status. The V2 and V3 routes and persisted records remain available.

## Source contract

The implementation follows `IREI_V4_Business_Rules_and_Build_Instructions_for_Uygar_Rev4.docx`, the finalized V4.2 blank template and the populated NGOB test workbook. `NGOB_Revision3_Board_Pages_FY2026-27.pdf` supplies the navy sidebar, teal cards, panels, interpretation strips, building-detail disclosure and report hierarchy. The logo is extracted unchanged from the PDF's embedded JPEG; it is not re-typeset.

The separate Revision 4 Page 1 PNG and original logo file named in the instructions were not attached. Page 1 therefore implements the written Revision 4 portfolio snapshot and baseline/decision/outcome requirements using the supplied PDF's styling. Source-linked records control where PDF numbers or unsupported statements disagree with the workbook.

## Import and calculation behavior

- Headers are recognized by names rather than by the densest row. Source-linked input formulas are followed to numeric staging cells; literal numeric settings are supported. Formula text, source cells, mapping review, source notes and the original checksum remain available. Arbitrary Excel formula caches are not executed as financial methods.
- Each upload retains its immutable `source.xlsx`. The whole source/mapping/register snapshot is retained alongside canonical facts and generated metrics. V4.2 is the supported structured intake; arbitrary legacy source models continue through the existing V3 mapping boundary rather than being auto-approved.
- Scope, assessment, organization, period, per-year scenario, currency and record grain are checked before calculation. Explicit `Excluded` inputs are omitted. Rejected/conflicting mappings cannot supply usable results. Proposed mappings can produce explicitly working pilot calculations; they are not promoted to approved facts.
- Portfolio `REVENUE` and the two `OPEX` controls are distinct from component revenue and building facts. The annual schedule recomputes principal plus interest. Duplicate or ambiguous annual schedule controls are withheld rather than allocated to buildings or summed into portfolio controls.
- Decimal calculations precede display rounding. Cash shortfall is `max(0, payments − NOI)`. Required NOI at the configured coverage target and the target gap are separate results. Near-one coverage displays as `0.996x` when two-decimal rounding would hide a shortfall.
- The placeholder sensitivity uses distinct flagged project subsidy inputs. Confirmation restores the excluded amount once. A one-year bridge changes cash funding but not operating NOI/coverage; recurring income and payment relief recompute their financial effects.
- Sheets 55/56 are implemented as named sensitivity entities with the supplied revenue, cost, payment, subsidy-loss and additional-cash formulas. Sheet 57 option results are sized to cash shortfall, preserving missing option amounts. Sheet 58 implements all four preliminary capacity gates; missing permission, reserve floor, security or repayment evidence withholds the safe amount.
- Financial evidence status measures completeness across every configured period. Projected figures remain projected and analyst review remains separate. Field-specific allowed-value findings are retained; generated/output caches do not become facts.
- Reserve continuity is a terminal diagnostic: the staged opening is compared with the prior closing, with source references. It does not infer opening balances, permitted reserve cash or an operating shortfall from a closing-to-closing movement.

## Data model and storage migration

The existing application uses JSON/OOXML persistence, not a relational database. This change adds a separate versioned V4 store without rewriting V2/V3 records:

```text
App_Data/V4Assessments/<runtime-id>/
  source.xlsx
  assessment.json
  Versions/000001.json
  Versions/000002.json
```

`V4Assessment` stores workbook assessment/organization identifiers separately from the runtime upload ID. Named entities include configuration, canonical facts, metrics, registers, an original baseline, append-only events and outcome observations. `V4Row` preserves named fields and formula text, so sheet numbers are not reassigned to outcome entities.

`V4Baseline` freezes the original metric values, states, period, scope, methodology and input IDs. The immutable first version preserves the associated source/fact evidence. Selecting another reporting year, editing review records, recording a decision or appending an outcome does not replace this baseline. Each upload creates an independent assessment; automatic cross-upload/organization baseline merging is not implemented.

Writes use a process-wide lock, expected-version checks, immutable `CreateNew` historical snapshots and an atomic latest-file replacement. Decisions and observations are append-only. This pilot store is intended for a single application instance; distributed transactions and database migrations are future production work.

## API and review schema

All `/api/v4` endpoints require the configured `X-Admin-Key` header. The browser retains the key only in memory for the current tab. It is never put in a URL, source file or browser storage.

| Endpoint | Behavior |
| --- | --- |
| `POST /api/v4/assessments` | Retain and import a V4.2 workbook; return runtime ID/version |
| `GET /api/v4/assessments/{id}` | Authorized canonical record |
| `GET /api/v4/assessments/{id}/pages/P1…P7` | Shared context, ordered section contracts, metrics, source references, registers and permitted actions |
| `GET /api/v4/assessments/{id}/metrics/{metricId}/evidence` | Result/method → mapped facts → source/formula chain → exceptions |
| `PUT /api/v4/assessments/{id}/configuration` | Synchronized reporting-year selection; invalidate stored narrative for re-review |
| `PUT /api/v4/assessments/{id}/records` | Allowlisted interpretation/action/mapping/exception edits; append client decisions |
| `POST /api/v4/assessments/{id}/options/test` | Bounded year/case-specific cash, recurring-income, payment-relief or subsidy test |
| `POST /api/v4/assessments/{id}/outcomes` | Append an evidence observation linked to an approved client decision/action |
| `POST /api/v4/assessments/{id}/approve` | Reject production approval until methods/source evidence are authorized |
| `POST /api/v4/assessments/{id}/reports/release` | Reject external release; generation and external delivery remain deferred |

The page response separates value state, certainty, review, working assessment, pathway readiness and external authorization. Display formatting and layout are allowed in JavaScript; financial formulas and chart dimensions are server-owned.

The shared-key gate is the repository's pilot authorization model. The recorded reviewer is an audit field, not an identity-provider-verified user or a separately enforced analyst/methodology-owner/approver role. Production role separation, identity authentication, registry-owner administration and workflow authorization remain required.

## Seven pages

1. Executive summary: portfolio snapshot, current financial context, cash path, borrowing evidence, mission/stewardship, sized tests, preserved baseline and shared actions.
2. Portfolio/mission: property register, source classification, homes over time and honest domain evidence gaps. Canonical addresses are absent in the supplied fixture, so the PDF's city list is not invented.
3. Financial/debt: current metrics, annual outlook, income/payment chart, revenue mix, closing reserves, continuity review, precise sensitivity, registered stress cases and option testing. Building rows are collapsed behind `View details`.
4. Evidence: period completeness, dynamically counted eligible open questions, source/mapping review and field validation findings.
5. Risks/options: near-term/current/build-up/full-operation context, base and excluded-subsidy cases, cash-sized options and shared actions.
6. Updates/actions: shared actions, client decisions, immutable update history and evidence-backed outcome observations. Empty management records remain empty.
7. Reports: four audience pathways, separate readiness and release states. No external report generation or release is claimed.

Source page/section registries and interpretation records are retained. Supported narrative sections reuse stored records with visible draft status. The initial supplied 45–49 registers are empty, so generated screening explanations are separate from recorded management preferences, actions and decisions.

## Baseline and outcome boundaries

Observations store definition, domain, unit, direction, scope, methodology, baseline/current values, period, evidence source, action, decision and contribution state. Missing observations display `Not yet measured`; no numeric zero or success badge is invented. A definition/unit/scope/method mismatch suppresses a delta. The first unregistered outcome definition requires baseline review; later matching observations can show a draft comparison.

Client pages do not expose non-user benchmarks or a program-evaluation cohort. Contribution is limited to direct facts, client-reported contribution or IREI-supported decisions; no causal evaluation claim is enabled.

## Waiting on source evidence

The current `33_DEBT` fixture contains payment aggregates and no maturity dates. It cannot support a passed-term count, routine-renewal classification for Jessica, a maturity-date principal amount or a current renewal outcome. The maturity candidate reader preserves conflicting dates, fiscal-year grouping and an explicit as-of requirement, but maturity balance output remains missing until a confirmed maturity balance method/input is staged. Do not mark the two mortgage/date acceptance tests as passed on this fixture.

No currently supplied canonical rent roll, occupied-home evidence, governance permissions, current appraisal, permitted reserve floor or resident outcome evidence is invented. Repair per home and an actual average rent conclusion remain excluded.

## Run and validate

```bash
export Irei__AdminKey="choose-a-long-local-secret"
dotnet build IreiMvp.sln
dotnet run --project src/IreiMvp.Web --urls http://localhost:5078
# Open /v4.html, enter authorized access, upload the supplied populated workbook.

dotnet run --project tests/IreiMvp.V4.Tests -- .
python tests/smoke_v4.py
python tests/verify_v2.py
node --check src/IreiMvp.Web/wwwroot/v4.js
```

The populated fixture and unchanged blank template are included under `Data/Examples` and `Data/Templates`. They are test/reference assets, not embedded customer defaults. Do not use the NGOB fixture as another organization's data.

Optional Playwright checks use an existing installation: set `V4_BROWSER_TEST` to a screenshot directory and run `tests/smoke_v4.py`. `CHROME_EXECUTABLE` can point to an installed compatible Chromium. Tests inspect seven routes, mobile overflow, collapsed building rows, evidence, synchronized year selection, an option test and shared action persistence.

## Deployment and rollback

Publish from source with `dotnet publish`; the checked-in `smarterasp-publish` and `artifacts` binaries are legacy snapshots. Their embedded pilot keys have been cleared; provide the key through environment/configuration at deployment. Preserve the persistent `App_Data` volume. No pre-existing V2/V3 migration is required. To roll back, redeploy the previous code while retaining V4 snapshots/source files for audit. No reports are externally released by this migration.
