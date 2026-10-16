# Spreadsheet-first intake demonstration

This is a runnable, explicitly enabled **trusted-staff demonstration**, implementing the first financial path of Revision 6 Section 12. It is not the full Revision 6 release or a multi-tenant customer portal. Source proposals never approve themselves. The existing V4 seven-page dashboard and registered calculations remain the destination.

## Run

Use .NET 8 and the existing solution. Configure the access key in the same shell as the server; the committed default is blank. Intake is disabled by default.

```bash
export Irei__AdminKey="set-a-local-secret"
export Irei__IntakeEnabled=true
dotnet run --project src/IreiMvp.Web --urls http://localhost:5078
```

PowerShell equivalents: `$env:Irei__AdminKey="set-a-local-secret"` and `$env:Irei__IntakeEnabled="true"`.

Open `/intake.html`, enter that key and create a batch with reviewed organization/assessment IDs, currency (CAD or USD), fiscal calendar and current-year label. Save the batch ID or URL to reopen it. Do not enable this endpoint for customer accounts: the existing pilot key grants trusted staff access to all batches, and organization membership/role enforcement is not implemented.

## Supported workflow

1. Upload one or more original XLSX or comma-delimited UTF-8 CSV files (maximum ten per request, 20 MB each, subject to the host's aggregate request limit). XLSX extraction refuses macro content and expanded archives above 100 MB. Sources remain local in the application data directory. Unsupported formats need conversion/manual review.
2. Inspect the inventory of every worksheet, its dimensions, nonempty/formula cells and missing caches. Every original workbook sheet is read; the extractor does not require an IREI template. Inspect property-table observations and date/note evidence as well as financial proposals.
3. Filter proposals by source/property hints, year/label or review state. Open an observation for its exact source location, raw value, formula and surrounding cells. Property initials and worksheet names are hints, not confirmed identity. Use the same reviewed stable property ID for aliases; this demo has no persistent alias dictionary.
4. Explicitly accept/reject each selected fact with a reviewer and rationale. Acceptance requires a supported financial code, scope/grain, fiscal year, scenario, numeric value and certainty selection. Record confirmation of currency, units, monthly/annual basis, conversion/sign and cache freshness in the rationale. No automated annualization or sign reversal is performed. Date/note evidence, missing caches and errors cannot become numeric facts. Malformed year labels remain raw until explicitly corrected by the analyst.
5. Commit accepted inputs. The exporter copies the reusable blank, populates allowed input cells and produces a candidate/source-to-target-row manifest. Existing template formulas remain unchanged. It then calls the existing importer and exposes the new V4 assessment link. Unreviewed proposals are excluded, so partial datasets intentionally produce unavailable results. This does not authorize external reports.
6. Upload a partial revision to the same batch. Earlier accepted facts remain active until an explicit replacement is reviewed. A same-key replacement must name the superseded candidate; a conflicting upload cannot win automatically. Recommit to create a new runtime assessment linked to the previous one, preserving the original baseline, prior snapshots, decisions, actions and observations. Copied narrative is marked for re-review. There is no automatic deletion from absence.
7. Record task minutes by role and machine cost/currency. Parser milliseconds are recorded per file separately from analyst effort. These observations are not a measured business-model productivity improvement until compared with a matched manual baseline.

Financial codes: revenue, total operating costs or the society/building operating-cost pair, total debt service or principal/interest pair, closing reserve, homes, subsidy and existing/new/other revenue components. Select total or components, never both. Component pairs must be complete. Principal/interest are never invented from a total. The importer now supports a **reviewed** total-only debt schedule row, preserving its total cell as evidence. An incomplete principal/interest pair remains missing.

The demo handles one selected scenario per fiscal year and one currency per batch. There is no legal-entity/instrument-level consolidation or guarantee/transferability inference. Reserve permissions stay unknown. Property names in the canonical register use reviewed scope IDs until richer identity review is added. Full-operation and renewal years are not guessed; related dashboard conclusions remain unavailable without configuration/evidence.

## Implementation and persistence

`IntakeExtractor` preserves spreadsheet observations and proposes supported meanings from labels and period headers, without NGOB-specific addresses, cell positions or fixtures. CSV quoting, escaped quotes and quoted newlines are handled; ambiguous delimiters/encodings require conversion. Source macros, formulas and external links are not executed. Cached source values have unverified freshness until reviewed. Original date serials are retained as raw evidence, not silently converted into exact financial dates.

`IntakeStore` retains immutable source files, a batch inventory, candidates, review history, supersession links, elapsed parser time and effort records under `App_Data/Intake/<batch-id>`. Source uploads deduplicate identical bytes within the batch; upload dates are not document dates. Each failed parse is listed with an error and contributes no partial candidates. Latest batch JSON is replaced atomically, with a process-local lock and optimistic review versions. Accepted records cannot be edited in place through the API.

`IntakeExporter` writes only input/configuration records to a copy of the existing blank. It checks headers, refuses formula overwrite, limits the existing eleven-year horizon and fails on template capacity overflow rather than truncating. Automatic input-capacity expansion is deferred. Workbook and manifest downloads require the same pilot key and a current committed selection. Manifest rows identify target sheet and row, linked to source/candidate IDs; richer cell-level browser traversal remains future work.

Changes to `V4Store` preserve baseline and working-record continuity across reviewed intake imports; the original structured-import route remains available. An intake update creates a new runtime folder, linked by `PriorRuntimeAssessmentId`; runtime version numbers still begin at one in each folder. Do not interpret those local counters as a single longitudinal version number.

## Verification

Run existing regression checks and the new dependency-free API test:

```bash
dotnet run --project tests/IreiMvp.V4.Tests -- .
python tests/verify_v2.py
node --check src/IreiMvp.Web/wwwroot/intake.js
python tests/smoke_intake.py
# Optional original-source checks; files stay outside the repository:
python tests/smoke_intake.py /path/to/original-financial.xlsx /path/to/property-summary.xlsx
```

`IREI_TEST_SERVER` may specify a JSON command array for an already compiled server. Test sources are created by the integration test; no original customer workbook is added to this PR. The organized V4 workbook is only an existing regression/reference fixture, never the extractor's hidden input.

Observed in this development session:

- Existing V4 suite: 340 assertions passed after the intake changes; .NET compilation used the official SDK compiler directly because the container's process metadata prevents the normal CLI/MSBuild launcher from running. This is not a completed `dotnet build` invocation; CI runs the standard build.
- New integration checks: synthetic CSV → review → template export → existing engine; missing-key rejection; duplicate upload; commit retry; exact preservation of all template formulas; explicit conflict supersession; retained partial-update facts; baseline/prior-version preservation; independent second-organization initialization; failed-file state; effort recording.
- Authorized original financial file: all 20 worksheets inventoried, 1,712 proposals; original property summary: its one worksheet inventoried, 392 observations/proposals. Proposals include unsupported/context-only observations; these counts are not precision/recall or approved fact counts.
- Both Jessica source locations (`JP!A32` and summary `X26`) remain separate evidence, with no automatic mortgage-date choice. The malformed fiscal header is retained without silent normalization. Evidence extraction does not close the historical mortgage acceptance tests or establish a current loan outcome.
- Six explicitly reviewed original FY2026–27 portfolio controls reproduce revenue, operating costs and DSCR within existing tolerances. This is a bounded financial demonstration, not full eleven-year/all-property extraction parity.
- JavaScript syntax and V2 structural verification pass. A pre-existing hardcoded pilot key on current master was cleared to satisfy the structural safeguard; configure the key at runtime.
- Rendered browser interaction/visual QA has not been completed in this environment: the Chromium download failed. API checks and JavaScript syntax do not establish browser behavior.

## Still required before a broader pilot

Full Revision 6 IN-01–11 are **not all passed**. Remaining work includes durable asynchronous jobs/retry/cancel, crash recovery across workbook/assessment/batch commit, authenticated customer organization/role separation, comprehensive aliases and instrument/entity keys, structured normalization records, multi-year scenario review, automatic change-set/gap summaries, group review, a complete expected dataset and full extraction quality evaluation, formula-cache freshness controls, automatic capacity expansion, source download/deep metric-to-batch traversal, DOCX/PDF/OCR, replaceable approved AI proposals and an unfamiliar real second-organization pilot. No external processing service is invoked by this build.

This staff demo is synchronous and single-instance. A crash after assessment creation but before batch save can leave an orphan assessment requiring reconciliation; do not claim transactional exactly-once publication. Back up the entire application data directory. There is no automated source deletion/retention policy. Raw originals and review information must not be exposed through static file hosting.

To roll back, set `Irei__IntakeEnabled=false` and deploy the prior application if needed, retaining intake source/audit files and V4 assessment snapshots. Existing structured import remains usable. Inspect measured review burden and remaining errors before expanding formats or updating delivery-economics assumptions.
