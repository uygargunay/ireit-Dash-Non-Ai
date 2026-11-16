# V2 board dashboard

The root workspace `/` now presents seven views based on the supplied visual structure: Executive Summary, Portfolio & Mission, Financial & Debt, Evidence, Risk & Options, Updates & Actions, and Reports. The original eight-page application remains at `/legacy-v2.html`, with its prior APIs and stored records unchanged.

## Upload and present

1. Open `/` directly. V2 requires no sign-in, access key or session confirmation.
2. Open `/`, select **Upload / Update**, and choose a populated V4.2 assessment, one original cash-flow model with optional supporting tables, or a mapped V4.2 assessment together with its source workbook(s) and supporting property table.
3. Leave the reporting year blank to retain the mapped workbook's selected year. Original-model intake selects the current April–March fiscal year when it falls within the source horizon. The year control can change the view after upload.
4. Select **Present** to hide the toolbar. Escape restores it. Every metric and View details control remains usable.
5. On Reports, use **Print all pages / Save PDF** to print the complete seven-view internal draft, or download the complete analysis record as JSON.

V2 does not contact `/api/evidence/session` and its requests omit portal cookies. A request failure clears the loading state and enables retry without discarding selected workbooks. V2 uploads, assessment links, workflow mutations, workbook lists and downloads are accessible without authentication. The separate evidence portal remains authenticated and continues to enforce roles and CSRF protection. Its protections do not extend to the open V2 APIs.

## Source selection and calculations

A mapped V4.2 assessment is the controlling input if supplied. Its numeric input-link chain is resolved through canonical mapping/staging rows. Output formula caches are not inputs. The existing server calculation engine computes revenue, operating expenses, NOI, annual principal plus interest, cash after debt, coverage, closing reserves, homes, target cushion and no-placeholder sensitivity.

When the original source is also uploaded, exact original cells are compared with the mapped observations. Their values are not added to the canonical totals. Mismatches appear in the checks; they do not silently replace the chosen source. Upload a corrected mapped workbook after resolving source authority.

Original-model intake finds a unique cash-flow sheet with named revenue, society/building expense and principal/interest controls. It recognizes fiscal-year columns, property schedules, unit-summary candidates and explicit subsidy-placeholder labels. It excludes aggregate sheets from property counts, preserves signed payment concerns and keeps property detail separate from portfolio totals. A malformed fiscal year is normalized only when consecutive adjacent years uniquely establish it. Source-title horizon limits are respected. Unsupported or competing source structures produce a clear error rather than invented results.

The original workbook's saved numeric values are retained as **unverified source observations**, with formulas preserved as provenance. The server does not execute arbitrary Excel formulas. Recalculate and save the source in Excel before upload when its formulas change. Raw-source CAD currency and a 1.20x coverage screening target are disclosed provisional assumptions. The inferred full-operation period remains a planning assumption requiring confirmation.

## Expected results from the supplied mapped assessment

| Measure | Expected source result |
| --- | ---: |
| Current fiscal year | 2026–2027 |
| Distinct property records | 16 |
| Operating / planned | 12 / 4 |
| Projected homes | 715 |
| Horizon planned homes | 1,011 |
| Income after costs | $4,082,375.58 |
| Annual loan payments | $2,591,047.00 |
| Coverage | 1.5755698693× |
| Cash after loans | $1,491,328.58 |
| Closing reserve | $2,312,308.76 |
| Full-operation coverage | 0.9958406777× |
| Full-operation cash shortfall | $57,113.97 |
| Full-operation placeholder subsidy | $3,318,983.00/year |
| Cash shortfall without placeholder subsidy | $3,376,096.97/year |
| Coverage without placeholder subsidy | 0.7541358950× |
| Open / resolved / accepted exceptions | 16 / 2 / 2 |
| Forward periods below 1.0× | 2 of 9 |

Ratios just below 1.0× display three decimal places so 0.99584× does not falsely appear to meet break-even. Cash shortfall, recurring NOI relief, a one-year bridge and the separate target cushion are distinct. A bridge can fund cash without improving operating coverage.

## What the uploaded files cannot establish

The reference images combine V18/V4.2 financial controls with an older property summary. Upload that supporting summary in the same batch as the controlling financial assessment. Labeled tabular supporting sources can supply historical listed units, cities, addresses, loan balances and maturity dates. Recognized column labels include Property name/ID, Units, City, Address, Maturity date, Loan balance and Source date. Exact unique property names, IDs and explicit alias fields match existing property IDs. Unmatched names, duplicate aliases or ambiguous headers produce import findings and never create new buildings. Unrecognized workbook layouts are retained with an explicit mapping finding.

Historical observations retain their file SHA, cell address and supplied date, separately from current financial facts. Conflicting historical values appear as conflicts, not a chosen value. Historical dates are not current renewal outcomes, listed units are not occupied homes, and historical loan balances are not annual debt service. A source date is never invented from the upload date. Previously saved records require a new upload to incorporate an additional source.

Actual rent rolls, household affordability, current occupied homes, resident outcomes, current condition reports and reserve-use permissions are also unconfirmed. A historical supporting source or a modeled rent assumption does not establish them. Property-level payment reconciliation and sign concerns remain visible even when portfolio coverage can be calculated.

## Evidence, actions and persistence

Automatic checks cover revenue components, original-to-mapped cell agreement, reserve opening continuity, property-versus-portfolio payments, and source workbook error values. States are **Pass**, **Review required**, or **Not testable**. Checks are terminal diagnostics and do not feed financial calculations.

Each metric opens its input values, source cells, formula text, review status and calculation version. Property rows open the underlying property record and current financial detail. Suggested actions remain proposals until deliberately recorded. The action dialog persists owner, date, status and originating finding. Recording an action does not invent a board approval or measured outcome.

Original files, source SHA-256 checksums, the dated metric baseline and versioned records are stored under the configured `Irei:DataRoot/BoardAssessments/`. New uploads create new assessment IDs and preserve previous records. Reporting-year changes and recorded actions append version history. Presentation and PDF output carry source identity, unresolved questions and draft status.

## Deployment and verification

Publish the updated project from source and upload the publish output to the existing SmarterASP site. Preserve the live `App_Data`, configured data root and existing portal configuration. Do not deploy the stale checked-in `smarterasp-publish` snapshot. V2 needs no account or key. Preserve the existing portal configuration for the separate protected portal.

```bash
dotnet build IreiMvp.sln -c Release
python tests/verify_v2.py
node --check src/IreiMvp.Web/wwwroot/board.js
python tests/smoke_board.py
```

`BOARD_RAW_WORKBOOK` optionally points to the supplied original V18 file for paired-source and original-only tests. `BOARD_BROWSER_TEST=1` enables the end-to-end browser regression; `PLAYWRIGHT_MODULE` and `EVIDENCE_CHROME` can specify local installed test dependencies. Browser checks cover real uploads, anonymous upload without session requests and file retention, all seven layouts, evidence/property drill-through, action persistence, option testing, print output and mobile widths.


## Reference-layout correction

The financial page uses the compact three-column options summary, with the full five-column comparison on Risks & Options. The executive summary retains visible stewardship captions and prioritizes current cash, renewals, full-operation coverage and subsidy dependency. Updates only displays a separate management-action table when records exist. Evidence questions prefer the actual client question over a category label. Browser assets have a new cache version.

Validation limits for this revision: JavaScript syntax and isolated renderer checks can be run without a browser. The .NET tests include mapped-reference summary counts and synthetic historical-source failure cases. The actual property-summary layout and live pixel comparison still need runtime validation; these changes do not establish an exact visual match.
