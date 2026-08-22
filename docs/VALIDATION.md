# Validation

## Requirement inputs reviewed

- V1 → V2 engineering change specification (four rendered pages checked).
- Stage 1 V2 blank workbook and demonstration workbook.
- Condensed platform visual summary.
- Eight full-page dashboard screenshots.

## Workbook contract checks

- Both supplied V2 workbooks contain 40 worksheets.
- Required workflow tables are present: `ASSESSMENT_VERSION`, `FIELD_HISTORY`, `CHANGE_EVENT`, `ACTION_DECISION`, `DATA_OBLIGATIONS`, and `REPORT_SNAPSHOT`.
- All eight `DASH_0x_*` sheets are present.
- The blank workbook retains more than 3,000 formulas; the demonstration workbook retains more than 2,800 formulas.
- Required property, operating, debt, cash, agreements and capital-needs tables and headers are present.
- Formula-dependent server metrics have direct-data fallbacks; target formulas remain untouched and are marked for full recalculation on open.

## Application checks run in this workspace

```text
PASS  python tests/verify_v2.py
PASS  node --check src/IreiMvp.Web/wwwroot/app.js
PASS  node --check src/IreiMvp.Web/wwwroot/admin.js
PASS  bash -n tests/smoke_api.sh
PASS  git diff --check
```

The structural test also confirms:

- exactly eight primary navigation pages;
- the V2 template is configured;
- no active AI advisor/configuration remains;
- no readiness `/100` UI or score properties remain;
- required V2 API routes exist;
- private artifact/source paths are separated from public view models; and
- both `.xlsx` packages are valid OOXML archives with the required V2 sheets and headers.

## Full build/runtime gate

The current artifact workspace does not contain the .NET SDK, so a local `dotnet build` and API execution cannot be honestly reported as completed here. `.github/workflows/ci.yml` installs .NET 8, builds the solution, starts the Release application, uploads the supplied V2 demo workbook, approves the assessment, persists an action, creates/approves/downloads a frozen report, and checks that private filesystem paths are absent from the public API.

Run locally before deployment:

```bash
dotnet restore IreiMvp.sln
dotnet build IreiMvp.sln --configuration Release --no-restore
bash tests/smoke_api.sh
```

## Deployment caution

The checked-in `smarterasp-publish/` folder is a legacy compiled snapshot. It must be regenerated from this source after the build/runtime gate passes.
