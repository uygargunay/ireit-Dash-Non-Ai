# IREI Stage 1 V2 — Non-AI

This ASP.NET Core 8 application turns an uploaded organization workbook into a private, eight-page portfolio stewardship dashboard and a standardized IREI workbook.

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

No admin key is committed to source control. Set `Irei__AdminKey` in the process environment before using private workbook or approved-report downloads. Keep the value out of URLs, logs and committed configuration.

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

Only safe public view models are returned by participant APIs. Server filesystem paths are not included. Generated assessment and approved-report downloads require the configured authorization key in the `X-Admin-Key` request header in this MVP.

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
