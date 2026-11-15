# Upload evidence and preview data

1. Sign in. If no organization exists, an administrator creates one under **Administration → Create an organization**. Keep the scope `organization` unless separate departments are needed.
2. Open **Documents → Upload documents**. Choose the appropriate category and reporting period. Successful files are retained independently. Category is an organizational label, not a restriction on which data the file can provide.
3. Staff uploads can **Extract supported data after upload**. For previously uploaded documents, choose **Extract all ready documents**, or **Extract data / refresh preview** beside a file. Extraction does not verify the contents.
4. Click **View data preview**, or open **Data preview and review**. Filter by source, proposed property/sheet and period. You can view unreviewed values immediately. Different forecasts, portfolio totals and property components are not added together.
5. Expand extraction results for sheets/pages and warnings. **Read extracted text** opens PDF page text. Image-only scans need OCR; this release cannot read their images. Unknown layouts may need manual mapping.
6. Confirm context once for a group: choose one **Source file** and **Proposed property / sheet**. Leave **Reporting period** empty to preserve all its fiscal years. Click **Confirm context for all matching records**, choose the correct inventory property or organization-wide control, basis, currency and monetary scale. Preview the count, then confirm. This covers every matching page and retains the original records. Filter to a period when actuals, budgets or projections have different bases. For single-year labels, explicitly choose whether FY2026 means the starting or ending year; malformed ranges remain blocked.
7. Click **Review all eligible matching records**. The preview shows the complete filtered count, eligible count and blocked reasons. After checking the original sources and mappings, enter your rationale and confirm once. Eligible records become verified together; incomplete context, competing records and affected material issues stay unchanged. Automated checks do not certify the underlying source figures. The existing selected-row workflow remains available for corrections and other review decisions.
8. Click **Create assessment**. Select the authoritative source versions for one case, reporting basis, currency and current fiscal year. Enter target coverage and the full-operation fiscal year only if confirmed. Preview eligible inputs, exclusions and conflicts, then create the assessment. It opens the existing seven V4 dashboard views using your portal sign-in; no additional staff key or export/re-upload is required. Reopen it from the assessment links in the data workbench.
9. CSV exports remain optional evidence extracts. They are **not** assessment upload files.

## How calculations work

Only verified, mapped, supported financial/homes records for the selected basis and currency feed the existing V4 server calculation engine. Supported inputs include revenue, total costs or complete society/building cost pairs, debt service or complete principal/interest pairs, reserves, homes and revenue components. NOI, coverage, cash after debt and shortfalls are calculated, not copied from arbitrary Excel caches. Missing inputs remain unavailable.

Portfolio controls and property detail remain separate. No automatic portfolio sum is inferred from a potentially incomplete sample. Multiple totals for the same property/period/metric become conflicts even if their values match. A total that disagrees with its complete component pair is withheld with an evidence question. Negative expense/payment source signs require an explicit source-linked correction; they are never silently reversed. Different financial scenarios must be assessed separately. Each assessment uses one reporting basis; create separate actual/budget/projection cases when needed.

The bridge creates a dated working snapshot, not an approved report. Changes to the original records do not silently rewrite it: correct the evidence and create a new assessment. Financial year selection and option testing work in portal dashboards; legacy workbook record-editing is not offered on these snapshots. Formal narrative approval, client sign-off and external release remain separate. Borrowing capacity, affordability, asset condition and other outputs remain unavailable unless their required evidence and assumptions exist; this bridge does not invent them.

Client evidence and generated assessments stay encrypted in the existing portal store and require staff roles plus all originating organization/department permissions. A withdrawn or quarantined source blocks access to its existing assessment. Preserve the existing evidence folder and encryption key when publishing. No new web.config setting is required. Publish the complete application, including its existing blank V4.2 template.

## When something fails

- **Quarantined/rejected:** resolve screening or provide an appropriate clean original before extraction.
- **Extraction failed:** open the warning; the successful upload remains saved. Split, convert or correct the source, then retry extraction.
- **OCR required:** provide a reviewed, searchable PDF or the original spreadsheet.
- **No supported fields:** inspect page text or use manual source-linked entry; missing data is not zero.
- **Conflicting values:** check scope, scenario, source version, period and units before choosing or correcting a value.
