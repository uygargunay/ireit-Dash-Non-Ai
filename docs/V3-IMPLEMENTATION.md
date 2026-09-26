# V3.1 financial pilot patch (BRDOC-V3-003)

This patch adds a separate V3.1 workspace at `/v3.html` while preserving the existing V2 application. It accepts the supplied Version 4 customer and internal templates. Every upload creates a new working assessment with a retained source workbook, checksum, staged financial controls, available calculated metrics, evidence pointers, and import exceptions under `App_Data/V3Assessments/<id>/`.

The V3.1 workspace uses the V2 dashboard's sidebar, palette, typography, header, upload dialog and metric-card styles. Its financial page now follows V2's dashboard hierarchy: a page summary, five headline metrics, two detailed panels, review status and evidence disclosures. Its navigation has seven V3 pages; the V2 workspace stays available through a sidebar link.

## Calculation boundaries

- Financial records are grouped by property, fiscal year, and scenario. Revenue and operating expense *control* rows are summed; detail, subtotal, source-reported NOI and source-reported DSCR rows are excluded to prevent double counting.
- Internal annual debt service is calculated from the principal and interest fields in `33_DEBT_SCHEDULE`. Customer annual `DEBT_SERVICE` records on `Financials` are used for their explicit fiscal year and scenario. Internal `DEBT_SERVICE_SOURCE` is comparison evidence and cannot silently substitute for a missing confirmed schedule. The undated `Debt` register is retained as a warning, never assigned to a year.
- Property debt service with an unresolved negative sign in any year is excluded for the whole property. In the reference case this guards MH, NT, and JP. Missing debt service stays missing, including FY2024–25. Zero revenue has a distinct `Not Applicable` ratio state; an explicit zero revenue or expense remains a real input.
- NOI, DSCR, cash after debt, cash after reserve, debt burden, required NOI and coverage gap are calculated on the server. The 1.20 coverage trigger and 1.00 severity boundary are labelled as pilot rule version 1.1. The unapproved stabilized comparator is suppressed.
- The other six pages explicitly display `Not Assessed`. Production approval is disabled pending methodology and evidence approval. The V2 approval path does not approve V3 versions.
- A customer template marked `Existing documents`, including the supplied NGOB customer file, is held at `Mapping Required`; its staged controls do not generate Page 3 numbers until the original source headers and mapping are reviewed. This prevents the 15 unsupported FY2030–31 property periods from appearing as approved calculations. The internal controlled template generates the reference Page 3 numbers.

## What is still required before the V3.1 release contract is complete

This is a working financial pilot implementation, **not** the complete product described in the business rules. It does not implement source-file extraction and analyst-assisted mapping for arbitrary existing Excel models, a database with migrations, analyst mapping decisions and conflict resolution, registry editing/version snapshots, user-level role permissions, or formal approval. Critical source controls in a customer template require analyst confirmation before any externally authorized output. The 12,221 raw financial detail lines are not stored as a full immutable staging table; the original workbook preserves them. Do not call this production ready.

## Local verification

The financial page formats monetary figures with the assessment currency, ratios as percentages, and DSCR as a multiple. It shows the selected property, year, and scenario, and hides the controls until an assessment loads. JavaScript syntax was checked with `node --check`. The current workspace has no .NET SDK, so a build and workbook import must be run in an environment with .NET 8 before deployment.

```bash
dotnet build IreiMvp.sln
node --check src/IreiMvp.Web/wwwroot/v3.js
dotnet run --project src/IreiMvp.Web/IreiMvp.Web.csproj --urls http://localhost:5078
```

Open `http://localhost:5078/v3.html` and upload the supplied internal Version 4 workbook. Inspect the 2025–2026 portfolio period, switch property/year, and open the evidence details. Upload the supplied customer workbook separately and confirm that it displays `Mapping Required` and `Not Assessed`. Reopen an assessment using the URL carrying its ID. Do not upload financial source files for which the V3 template has not been prepared and reviewed.

## Deployment and rollback

Apply `irei-v3-financial-pilot.patch` to a clean checkout of the repository commit identified in the handoff. Build and deploy using the existing deployment pipeline. Keep the persistent `App_Data` volume. To roll back code, redeploy the prior commit; retain `App_Data/V3Assessments` for audit and future import. No database migration is included or required by this pilot patch.
