# Architecture

## Participant flow

`Browser -> POST /api/submissions -> SubmissionStore -> TemplateTransformer`

The participant receives dashboard metrics, property summaries and explicit review warnings. There is no participant endpoint for downloading the generated workbook.

## Runtime workbook analysis

The mapper:

1. Reads all source sheets.
2. Detects wide multi-year statement patterns from cell structure.
3. Finds year columns and line-item columns without fixed source addresses.
4. Identifies property-level and portfolio-level statements.
5. Extracts property, operating, unit and debt evidence.
6. Uses optional high-confidence AI field suggestions for unmatched table layouts.
7. Writes into target tables discovered from the blank template's own headers.
8. Preserves target formula cells.

## Private workbook

A copy of the organization-neutral blank template is written to:

`App_Data/Submissions/<submission-id>/admin-output.xlsx`

## Administration

The starter admin endpoints use `X-Admin-Key`. The configured development key is `kelly`.

Replace this with identity-based authentication and role authorization before production.
