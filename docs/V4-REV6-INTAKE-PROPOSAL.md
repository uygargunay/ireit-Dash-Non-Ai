# Revision 6: original-document intake proposal

Status: engineering review and proposed delivery plan, 16 October 2026. This document does not implement intake or certify extraction accuracy. Reviewed against master `a47b990de7616f3542eb9dd86e49b73e0623c662` and Revision 6 Section 12, BR-V4-073–086, IN-01–11.

## Recommendation and first boundary

Proceed with a separate intake layer feeding the existing V4.2 calculation engine. Keep original documents immutable, AI proposals provisional and financial methods deterministic. Prove the compatible-workbook route before direct canonical-store integration. Keep the seven dashboard pages and V5 scope unchanged.

The smallest useful milestone is an **analyst-operated XLSX/CSV vertical slice**: multiple original files, every worksheet inventoried, table/period proposals, source preview, reviewed property aliases, conflict resolution, compatible export, import, one revised upload and a synthetic second organization. Support financial inputs, debt-payment components, reserves and home counts needed for existing calculations. Do not require a client to populate an IREI template. Do not require every financial result to be available when evidence is missing.

This milestone is a proposed sequencing decision, **not the full Revision 6 first release**. The full contract also requires DOCX, text PDFs, image-PDF OCR, replaceable AI proposals, organization/role controls and the complete IN suite. Unsupported documents must be retained with explicit conversion/manual-review status, never appear successfully extracted. Do not describe the XLSX/CSV milestone as full original-document intake acceptance.

## Evidence inspected and prerequisites

The five supplied attachments are the Revision 6 DOCX, read-first TXT, Revision 4 visual PDF and two Excel references. The read-first TXT names two additional originals: the V18 financial model and 2025 property summary. Neither original is in these five attachments. Obtain authorized copies before IN-01, source-reference reconciliation and the mortgage-conflict test. Do not silently substitute the structured fixture. Another organization's real sources are also required for the unfamiliar-organization pilot; synthetic data tests architecture but does not establish real extraction performance.

The reattached Excel files match the earlier attachments byte-for-byte:

| Reference | SHA-256 |
| --- | --- |
| Blank V4.2 | `b24b5b59dee7ec8653cf0919c6ac89a83b9aff6844b026583b5d628e82d6c0d4` |
| NGOB V4.2 fixture | `cccd23beffd6bcf3b532af6531032eb445bc788c5205e936bf72a64238b9b9c8` |

The PDF remains the rendering contract, not a source of numeric inputs. No visual redesign is proposed here; visual parity with the newly supplied PDF still needs its own implementation review.

## Current-code findings that affect the design

| Component | Observed behavior | Consequence |
| --- | --- | --- |
| `V4WorkbookImporter.Read` | Requires named V4.2 sheets and recognized headers. Iterates workbook sheets; silently skips unrecognized headers. | It is a structured importer, not an original-layout extractor. Add a separate per-sheet inventory and extraction outcome. |
| `V4WorkbookImporter.Resolve` | Resolves quoted direct cell references and numeric literals; arbitrary formulas return missing. | Preserve formulas/caches as evidence. Export analyst-reviewed typed values into permitted input fields; do not depend on source formula evaluation. |
| `V4WorkbookImporter.Read` fact schemes | Uses financial amounts, debt principal plus interest, closing reserve amounts and portfolio-unit records. | Exact adapters are required. A total debt-service amount alone cannot be silently split into principal/interest. Unsupported totals remain gaps until an explicit adapter/method decision. |
| Import review states | Proposed/under-review numeric fixture inputs can enter pilot calculations; enum validation adds findings instead of universally rejecting values. | The new export boundary must enforce reviewed/included records and valid field-specific enums. Do not infer intake approval from the importer's pilot behavior. Preserve legacy fixture regression behavior. |
| `V4Store.ImportAsync` | Each upload creates a new runtime assessment ID. | A document update needs a version lineage and baseline-preservation adapter, not an unrelated re-import labeled an update. |
| `V4Store` | JSON snapshots and process-local semaphore; no durable job queue or cross-worker transactions. | Use durable intake tables/queue and explicit transaction boundaries before concurrent workers. Existing dashboard store can remain behind an adapter during the pilot. |
| `V4Endpoints` | Shared `X-Admin-Key`; no user-to-organization membership enforcement. | Staff-only isolated demo possible; multiple customer organizations require authenticated membership/role authorization on every route and download. A batch Organization_ID is not authorization. |
| `XlsxWorkbookService.Read` | Retains cells/formulas; does not provide a complete merged-region/table/visual extraction model. | Extend the source parser's structure metadata. Do not hardcode NGOB coordinates. |

## Source-to-engine contract

Use named platform entities; do not create additional numbered Excel sheets. Keep a schema-versioned sidecar manifest for concepts the existing template cannot store.

| Intake fields | Existing destination / integration | Required behavior |
| --- | --- | --- |
| Source_ID, document date, filename, version, hash, supersedes, storage reference | `10_SOURCE_REGISTER` exact corresponding headers | Date of document differs from upload date. Immutable version. Source authority is field-specific review, not upload recency. |
| Candidate_ID, Source_ID, raw locator, label, text/number/date, context, capture method | `11_RAW_STAGING`: Raw_Record_ID, Source_ID, Source_Location, Raw_Label, Raw_Value_Text/Number, Raw_Date, Context_Text, Capture_Method | Preserve original sign, units, formula text, cache state and notes; use typed values and explicit value state. |
| Proposed canonical table/field/value; confidence; reviewer/date; exception | `20_FIELD_MAPPING`: Mapping_ID, Raw_Record_ID, Canonical_Entity/Field, Canonical_Value_Text/Number/Date, Confidence_Level, Reviewer, Review_Date, Exception_ID | Validate against `00_FIELD_GUIDE`. Extraction confidence does not confer certainty or approval. |
| Reviewed repeatable matching rules | `21_MAPPING_PROFILES` plus versioned profile entity | Scope by organization/document family. Changed layout requires validation; no automatic approval. |
| Stable property identity, name/address and alias decisions | `31_PROPERTY` plus PropertyAlias, Entity and Instrument entities | Initials are candidate matches. Building, legal owner, loan instrument and replacement project stay distinct. |
| Reviewed metric, period, scenario, currency, grain, status | `32_FINANCIAL` exact Amount, Metric_Code, Period_Start/End, Fiscal_Year, Scenario, Record_Grain, Input_Status, Mapping_ID and source fields | Full-key match; portfolio controls never aggregate with their component detail. |
| Reviewed principal and interest by instrument and period | `33_DEBT_SCHEDULE` and `33_DEBT` | Preserve original total separately; reconcile components. Never assume absent component equals zero. |
| Reserve amount, as-of date, restrictions, fiscal year/scenario | `34_CASH_RESERVES` | Closing balance is not opening balance or permission to spend. Preserve Cash_Type and Restriction_Status meanings. |
| Homes for scope/period/scenario | `31_PORTFOLIO_UNITS` | Occupancy remains unconfirmed unless supported. Do not convert planned homes to occupied homes. |
| Conditions, conflicts, requests and validation | `12_SOURCE_NOTES`, `50_VALIDATION`–`54_INFO_REQUESTS` | Review records do not become financial inputs. Unresolved critical conflicts block affected facts. |
| Job/run/model/prompt/schema version, scale/basis conversion, role audit, full fact key, candidate links, target cell manifest | Named IntakeJob, ExtractionRun, Candidate, ReviewEvent, ChangeSet, ExportManifest entities | Retain explicitly outside the workbook where no existing field represents the concept. |

Minimum candidate key: organization, assessment lineage, legal entity, property/portfolio scope, instrument where applicable, field, period/as-of date, scenario, currency, unit and grain. Source version identifies evidence, not a different economic fact. Store exact decimal strings in the interchange schema and parse to decimal; never use binary floating point for financial validation.

Candidate states are separate from workbook enums: Proposed, NeedsClarification, Accepted, Rejected, Superseded. An adapter maps these to valid destination-specific statuses. Accepted requires authorized reviewer, time, rationale, valid source locator, complete context, schema validation and a source-choice decision for competing candidates. Approval of a group applies only to an explicit immutable selection/version, not unseen later proposals.

## Smallest end-to-end flow

1. Create a batch within an authorized organization and assessment. Register immutable originals, safe filenames, byte limits, hashes and document dates. Deduplicate within the same source context; never reveal another organization's file existence.
2. Enqueue extraction. Inventory every file and every worksheet/page, including hidden/empty content. Preserve merged headings, cells/ranges, tables, formula text and cache presence; do not execute macros, external links or embedded instructions. CSV retains row/column and encoding/delimiter decisions.
3. Generate table, label, property and period proposals. Deterministic parsing preserves structure; a replaceable AI adapter can suggest meaning using a constrained schema and approved service. Manual entry/review remains possible without AI. Missing source caches and OCR uncertainty are explicit states.
4. Show file list, worksheet outcomes, candidate grid and source preview. Filter by property, year, field and review state. Allow correction, rejection, group acceptance, alias linking, clarification and source-choice review. Capture before/after and rationale.
5. Validate the accepted selection against the template schema and field-specific enums. Display included/excluded facts, gaps and affected results. Preserve unrelated supported work; do not make unsupported financial results look complete.
6. Copy the approved blank, write allowed input cells only, preserve formulas and registries, and emit a candidate-to-target-cell manifest. Add rows only through an explicitly tested capacity adapter, including range/formula/reference updates; overflow must fail visibly rather than truncate. Inspect protected formulas/registry content before and after export.
7. Run the existing importer in a staging assessment, reconcile supported outputs, then atomically publish the new assessment version and update lineage. Keep the original baseline unchanged. Export or intake approval does not authorize external reports.
8. A revised document produces Added, Changed, AbsentInPartialUpdate and Conflict candidates. A removed fact needs an explicit complete-scope declaration and reviewer-approved tombstone. Preserve prior values until replacement is accepted; mark dependent narratives for re-review.

Job lifecycle: Queued → Extracting → AwaitingReview → ReadyToExport → Exporting → Imported; Failed and Cancelled are explicit outcomes. Retry creates a new run linked to the same immutable source; it cannot duplicate accepted facts. Cancellation prevents publication even if an external request finishes later. Use durable leases, optimistic versions, idempotency keys and atomic publication; a durable outbox should bridge intake storage and the legacy assessment store with recoverable reconciliation.

## API and storage changes proposed

These endpoints are a contract proposal, not available routes:

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v4/intake/batches` | Create authorized batch/assessment association and scope. |
| `POST /api/v4/intake/batches/{id}/files` | Add originals with idempotency key; return per-file outcomes. |
| `POST /api/v4/intake/batches/{id}/runs` | Queue a pinned parser/AI configuration. |
| `GET /api/v4/intake/batches/{id}` | Jobs, files, worksheet/page inventory, gaps and processing summary. |
| `GET /api/v4/intake/batches/{id}/candidates` | Filtered proposals and conflict groups; paginated. |
| `POST /api/v4/intake/batches/{id}/reviews` | Version-checked corrections, source selections and review decisions. |
| `POST /api/v4/intake/batches/{id}/changesets` | Compare reviewed facts to maintained version with partial/full update scope. |
| `POST /api/v4/intake/batches/{id}/exports` | Create validated workbook and manifest; separate explicit commit to assessment. |
| `POST /api/v4/intake/runs/{id}/cancel` and `/retry` | Audited cancellation and safe retry. |
| `GET /api/v4/intake/sources/{id}/preview` and `/download` | Authorized immutable source evidence; no public storage URLs. |
| `POST /api/v4/intake/batches/{id}/effort` | Task/role effort and processing-cost measurements. |

Persist batches, documents/versions, runs, candidates, aliases, review events, change sets, exports and effort events in a transactional store. Start with the deployment-supported SQL database and a durable worker, rather than introducing an unverified database dependency. Store originals separately with application-mediated access and tested backups. Migration is additive: retain V4 snapshots and structured import. Rollback disables new intake routes/workers, preserves originals/audits and leaves the last published V4 version active. Database choice, durable worker support, file size limits, encryption/key handling and backup/restore must be confirmed with the hosting operator.

## Processing-service choices and operating cost

Choose with Uygar after the authorized source benchmark; no provider is selected by this proposal.

| Option | Benefit | Dependencies / limitations |
| --- | --- | --- |
| Deterministic extraction in IREI environment | Predictable raw structure and local processing | XLSX/CSV first; approved DOCX/PDF libraries and PDF table handling still required. |
| Local OCR and locally hosted model | Greater processing-location control | Available CPU/GPU, model quality, deployment permissions and ongoing operations must be established. Shared hosting may not support these workers. |
| Approved hosted OCR/document service plus schema-constrained model | Less model infrastructure to operate; structured proposals | Contract, processing location, retention/data use, subprocessors, credentials, spend caps and evaluation are gates before sending client documents. |

AI receives only the authorized document content required for a task, with no approval or publication credentials. Validate response shape, locator existence, units and candidate references. Ignore document instructions attempting to change rules or call tools. A schema-valid response can still be wrong; analyst review and benchmark exactness are mandatory.

Do not quote an operating price before service and volumes are known. Measure each run: text pages, OCR pages, input/output tokens, retries, unit rates and rate date/currency, compute seconds, storage volume and monitoring. Cost per batch = OCR usage + model usage + worker compute + allocated storage/operations. Report machine cost separately from loaded staff cost, with failed runs included. Set a batch cost cap and route oversized files to manual review. Obtain current vendor quotes at selection time; this review contains no verified vendor pricing or data-location assurance.

## Engineering estimate

Planning range, not a fixed quote. Assumes one experienced full-stack engineer at 40 hours/week, timely product/underwriting decisions, accessible originals, existing deployment access and a narrow supported financial field set. Calendar time increases with part-time capacity, procurement and reviewer availability. Estimates include implementation and tests, not guaranteed accuracy for arbitrary layouts.

| Spreadsheet-first milestone | Engineer hours |
| --- | ---: |
| Freeze regressions; expected dataset, schema and adapter contract | 20–32 |
| Durable source/job storage, safe uploads and staff authorization boundary | 24–40 |
| XLSX/CSV structure extraction and table/context proposals | 32–48 |
| Analyst review, aliases, conflict/full-key consolidation | 32–48 |
| Compatible export, capacity handling and source-cell manifest | 24–40 |
| Versioned update adapter, measurement, end-to-end tests/setup | 28–40 |
| **Subtotal** | **160–248** |

Approximately 4–7 full-time engineering weeks; at 20 hours/week approximately 8–13 weeks, excluding external delays. Separately allow 20–40 senior-underwriter hours for reference labels, acceptance and exceptions, and 4–8 founder hours for scope/access decisions. These are delivery assumptions to measure, not free capacity.

| Additions to reach the full Revision 6 release | Additional engineer hours |
| --- | ---: |
| DOCX/text-PDF extraction, locators and preview fixtures | 40–64 |
| OCR route, page failure controls and quality evaluation | 48–80 |
| Replaceable schema-constrained AI proposals, prompts and evaluation | 24–40 |
| Customer organization/role enforcement, isolation and deployment hardening | 32–56 |
| Real second-organization pilot, broad regression/failure tests and contingency | 40–64 |
| **Additional subtotal** | **184–304** |
| **Combined engineering range** | **344–552** |

Approximately 9–14 full-time engineering weeks in total, with 20–40 additional underwriting hours for broader formats and real-client evaluation. There is overlap between security foundations and customer hardening; scope them once in ticket estimates. Identity-provider setup or hosting replacement outside these assumptions needs re-estimation. No pricing, revenue or productivity forecast is changed by this estimate.

## Rule-linked build tickets and acceptance

| Ticket / rules | Component and observable acceptance | Fixture / dependency |
| --- | --- | --- |
| A / 073, 082, 083 | Source/job storage, inventory, auth and retry: same upload/retry does not duplicate facts; cancel/failure leaves published data unchanged; cross-org source/job/export/download access denied | Synthetic organizations plus authorized originals; IN-03/10 |
| B / 074–076, 081 | Parsers/proposals: original locators and formula/cache states retained; moved headers/merged cells, periods across columns and initials produce reviewable proposals without fixed coordinates | V18/property summary plus changed-layout fixture; IN-01/02/05/06 |
| C / 075–078, 084 | Review UI: field/period/scenario/grain conversions explicit; missing locator cannot be accepted; same-key conflicting values require source choice; 0 differs from missing/error | Reviewer-labeled examples; IN-04/05/06/09 |
| D / 079 | Export adapter: only valid reviewed input rows; no formula/registry changes; overflow visible; supported outputs reconcile to Section 8 tolerances | Blank and structured fixture as comparison, originals as source; IN-07 |
| E / 080, 082 | Update/version adapter: revised source produces changes, partial absence keeps prior fact, explicit removal reviewed, baseline unchanged and narrative invalidated | Revised-source and partial-update fixtures; IN-08 |
| F / 083, 086 | Second organization and failure tests: no data/ID/alias leakage; unavailable AI/OCR gives explicit state and manual route | Synthetic isolation plus separately authorized unfamiliar real documents; IN-02/10 |
| G / 085, 086 | Effort report: actual task/role hours, run costs, corrections and matched manual baseline; no unmeasured savings claim | Measured pilot; IN-11 |

Preserve existing Section 8 financial regressions: monetary tolerance $0.01, ratios 0.000001, exact states/labels. Source conflicts remain explained exceptions; do not force all organized-fixture figures to match by inventing inputs. Regression suite must run against master before implementation; historical 340-assertion results are not a new test run.

Current review status: IN-01–11 **not executed for new intake**; no implementation exists in this PR. Original-source acceptance is blocked on the missing originals; unfamiliar real-source acceptance awaits authorized second-organization documents. This environment currently has no `dotnet` executable, so no new .NET regression claim is made. Spreadsheet hashes and current source/API limitations were inspected directly.

## Measuring onboarding and later updates

Define a cycle by organization, asset count, document versions/volume, field families, years and deliverable. Freeze a reviewer-approved expected dataset. Perform a manual baseline on the same scope and comparable source quality, record operator experience, and include repeated-run familiarity/order effects when interpreting improvements. Report a second matched client cycle rather than treating a cached rerun as onboarding savings.

Record TaskEvent_ID, batch/assessment/source/run, task type, role, person, start/end, active minutes, waiting minutes, correction count, question count and notes. Task types: collection, extraction, mapping, reconciliation, review, correction, client clarification, explanation and engineering support. Roles: analyst, senior underwriter, founder and IT. Capture overlapping human time separately from elapsed wall time; avoid double-counting a timer. Record product engineering separately from client-specific support, with both visible.

Per field family report: supported reference facts, correctly proposed facts, incorrect proposals, missing reference facts, unresolved cases, accepted facts and analyst corrections. Completeness = correctly proposed supported facts / supported reference facts. Exactness = correctly proposed facts / all evaluable proposals. Also report raw counts and unsupported scope, not one blended confidence score. Test locator correctness and contextual normalization separately from numeric extraction.

Report onboarding and update active hours by role/task, elapsed time, machine cost, correction/review burden and residual critical errors. An update report also counts reused facts, changed facts, re-reviewed facts and explicit removals. Savings = 1 − assisted active hours / measured matched manual hours; leave unavailable when the baseline is unmeasured. Compare observed normalized cycles with the 1.0→0.455 onboarding and 0.20 update hypotheses only after measurement. Do not infer paid renewal or community outcomes from processing speed.

## Decisions needed before implementation

1. Confirm spreadsheet-first sequencing versus implementing all four format families before the first demo. Full Revision 6 acceptance still requires all agreed formats.
2. Provide authorized V18 and 2025 property summary, identify a real second-organization fixture and label expected supported facts with a senior underwriter.
3. Confirm hosting/worker/database/identity constraints, allowed processing location and service data-use terms; select provider only after a small authorized benchmark.
4. Approve the permitted-input cell policy, capacity expansion behavior and debt-component handling; do not modify financial methods to compensate for missing evidence.
5. Allocate engineering and underwriting time and capture a manual baseline before making onboarding-efficiency claims.
