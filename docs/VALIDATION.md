# Validation performed

The updated first version was checked against the newly supplied wide multi-year financial workbook.

## Runtime pattern results

- 20 worksheets were inspected without a fixed source-tab list.
- 4 portfolio / aggregate financial sheets were identified structurally.
- 16 property or project statement sheets were identified.
- `2025-2026` was selected as the latest completed/current reporting period represented in the workbook.
- The highest-quality portfolio operating statement was selected separately from the portfolio unit source.
- A 470-unit portfolio total was identified from the strongest unit-summary candidate.
- 4 future-development property sheets were distinguished from active properties by the first positive rental-revenue year.
- The 245-versus-328 unit conflict on one project sheet was retained as a review flag rather than silently resolved.
- Annual mortgage principal and interest lines produced a partial portfolio debt-service record when a complete debt schedule was not present.
- Cached Excel error values in the source workbook are counted and surfaced as a review warning.

## Package checks

- The organization-neutral blank template contains no organization-specific seed rows or legacy example wording.
- The project contains no built-in example endpoint or example-processing button.
- Runtime mapping uses structural patterns and runtime headers, not fixed source sheet names or fixed source columns.
- OpenAI is invoked only for workbook layouts that do not match the supported structural pattern.
- OpenAI unavailability or HTTP 429 does not prevent supported local-pattern processing.
- JavaScript syntax, JSON configuration, project XML, workbook ZIP integrity and source-code delimiter balance were checked during packaging.
- A full .NET compilation could not be performed in the artifact environment because the .NET SDK is unavailable there.
