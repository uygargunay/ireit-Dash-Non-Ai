# IREI MVP — Updated First Version

This is the original ASP.NET Core 8 upload-and-dashboard project, updated to process organization workbooks whose sheet names, header rows, column order and property-tab names vary.

## Current intake approach

The application inspects every uploaded sheet at runtime. It does not depend on a fixed list of client sheet names or source columns.

It supports two intake paths:

1. **Pattern mapping for wide financial models**
   - Detects multi-year headers such as `2025-2026`.
   - Identifies the line-item column dynamically.
   - Distinguishes portfolio summaries from property-level statements.
   - Creates property/project records from repeated property statement tabs.
   - Extracts one reporting-period operating view without summing every forecast year.
   - Detects repeated loan-summary matrices and annual mortgage/debt-service lines.
   - Retains formula errors, unit conflicts and missing evidence as review flags.

2. **Optional OpenAI field suggestions**
   - When `OPENAI_API_KEY` is configured, compact runtime sheet profiles are sent to the OpenAI Responses API.
   - AI suggestions are used only above the configured confidence threshold.
   - A 429 response or unavailable AI service does not stop processing; local pattern mapping continues.
   - Flat tables can also be mapped by runtime header matching.

The generated workbook remains private to the administrator. Participants see the dashboard only.

## Main changes in this package

- Removed organization-specific seed logic, source files and wording.
- Removed the built-in example-processing button and endpoint.
- Replaced the blank workbook with an organization-neutral IREI template.
- Added dynamic handling for wide multi-year property and portfolio financial models.
- Added debt-summary and unit-conflict detection.
- Set the development admin key to `kelly`.
- Kept local file storage for the first version.

## Run locally

```powershell
dotnet clean .\src\IreiMvp.Web\IreiMvp.Web.csproj
dotnet run --project .\src\IreiMvp.Web\IreiMvp.Web.csproj --urls http://localhost:5078
```

Open:

- Participant UI: `http://localhost:5078`
- Admin UI: `http://localhost:5078/admin.html`
- Development admin key: `kelly`

## Optional OpenAI configuration

PowerShell:

```powershell
$env:OPENAI_API_KEY="your-api-key"
```

The application remains usable without the key for supported financial-model patterns.

## Storage

```text
App_Data/
  Submissions/
    <submission-id>/
      input.xlsx
      admin-output.xlsx
      submission.json
```

Before a public production launch, replace the simple admin key with authenticated user accounts and role-based authorization, and move files to encrypted object storage.
