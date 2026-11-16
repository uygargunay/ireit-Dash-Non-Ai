# IREI Stage 1 — Non-AI

> V2 board dashboard: `/` provides seven visual portfolio, financial, evidence and decision views from uploaded mapped assessments and original cash-flow models. See [upload, presentation, calculations and deployment](docs/V2-BOARD-DASHBOARD.md). The original eight-page V2 workspace remains at `/legacy-v2.html`.

> Phase 2 evidence portal: `/evidence.html` provides invitation-based client uploads, XLSX/CSV and text-PDF extraction, a draft data preview, guided preparation from zero verified records, bulk context/review, printable internal draft reports and direct working V4 assessments and frozen client-signoff snapshots. See the [upload and preview guide](docs/EVIDENCE-QUICK-START.md). Disabled by default; see [setup, security gates and boundaries](docs/PHASE2-EVIDENCE-PORTAL.md).

> V4.2 underwriting pilot: open `/v4.html`. See [implementation and deployment](docs/V4-IMPLEMENTATION.md) and [calculation validation](docs/V4-VALIDATION.md). The seven-page workspace uses the finalized V4.2 template and server-side calculations, with source evidence, shared review records and baseline/outcome tracking. Enter the configured pilot access key before importing a workbook. Production approval and external report release remain blocked.

> V3.1 financial pilot: see [the V3 implementation and handoff](docs/V3-IMPLEMENTATION.md). Open `/v3.html` to import the current customer or internal template. The original V2 workflow remains available at `/legacy-v2.html`.

This ASP.NET Core 8 application turns an uploaded organization workbook into a portfolio stewardship dashboard and a standardized IREI workbook.

The non-AI edition is deterministic. It does not call an AI service, infer investor recommendations, create a 100-point readiness score, or silently fill missing evidence. Unsupported or absent fields are shown as **Not assessed** and retained for human review.

## V2 capabilities

- Eight distinct workflow pages matching the Stage 1 V2 navigation:
  1. Portfolio Overview
  2. Organization & Governance
  3. Properties / Portfolio
  4. Financial Resilience
  5. Mission & Public Value
  6. Decisions, Risks & Actions
  7. Evidence & Data Quality
  8. Reports & Authorized Use
- Canonical import for the 40-sheet Stage 1 V2 workbook, plus normalized-header and existing wide-statement fallbacks.
- Working assessment versions that compare to the latest approved/verified baseline.
- Immutable approved assessment artifacts.
- Deterministic field history and material-change events.
- Persistent actions, decisions, obligations and forward events.
- Metric-to-source evidence lineage and supporting-evidence uploads.
- Frozen report artifacts with approval and sharing gates.
- Property-row drill-through without inventing missing values.
- Searchable/filterable property register with portfolio, geography, risk and exception views.
- Four controlled condensed-story presets: Executive Summary, Portfolio & Financial, Mission & Risk, and Evidence & Reporting.

## Included workbooks

- `Data/Templates/IREI_MVP_Stage1_V2_Blank_Template.xlsx` — production blank template.
- `Data/Examples/IREI_MVP_Stage1_V2_NGOB_Demo.xlsx` — supplied V2 demonstration workbook for local verification only.

No organization-specific demo values are embedded in the application code or UI.

## Run locally

```bash
export Irei__AdminKey="choose-a-long-local-secret"
dotnet restore ./src/IreiMvp.Web/IreiMvp.Web.csproj
dotnet run --project ./src/IreiMvp.Web/IreiMvp.Web.csproj --urls http://localhost:5078
```

Open:

- Dashboard: `http://localhost:5078`
- Admin workbook list: `http://localhost:5078/admin.html`
- Health: `http://localhost:5078/health`

V2 uploads, assessment links, workbook lists and generated workbook/report downloads require no sign-in, key or session confirmation. `Irei__AdminKey` remains relevant to the separate V3/V4/intake workspaces. No key is committed to source control.

### Open V2 access

Open `/` or `/legacy-v2.html` and upload the Excel workbooks directly. V2 does not contact the portal session endpoint and sends no portal cookies or confirmation tokens. The V2 workbook list and download screen at `/admin.html` also require no key.

V2 assessment URLs and its submission register are accessible without signing in. The separate evidence portal retains authentication, roles and CSRF protection; V3, V4 and intake keep their existing key requirements.

## Docker

```bash
export IREI_ADMIN_KEY="choose-a-long-local-secret"
docker compose up --build
```

The named volume keeps submissions and organization workflow state between container restarts.

## Storage model

```text
App_Data/
  Submissions/
    <submission-id>/
      input.xlsx
      admin-output.xlsx
      submission.json
  Organizations/
    <organization-id>/
      organization.json
      Evidence/<source-id>/<file>
      Reports/<report-id>.xlsx
```

Only safe public view models are returned by participant APIs. Server filesystem paths are not included. V2 assessment and report downloads are open without authentication, including when the evidence portal is enabled.

## Verification

```bash
node --check src/IreiMvp.Web/wwwroot/app.js
python tests/verify_v2.py
dotnet build ./IreiMvp.sln
bash tests/smoke_api.sh
```

The CI workflow runs these checks on pushes and pull requests.

## Deployment note

`smarterasp-publish/` is a legacy compiled snapshot and is not updated by source edits. Regenerate it with `dotnet publish` before deploying to that hosting target; do not deploy the stale checked-in binaries.

Before public production use, replace the starter admin-key control with identity-based authentication and role authorization, and move artifacts to encrypted managed storage with retention and backup policies.
