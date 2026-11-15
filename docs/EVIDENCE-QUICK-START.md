# Upload evidence and preview data

1. Sign in. If no organization exists, an administrator creates one under **Administration → Create an organization**. Keep the scope `organization` unless separate departments are needed.
2. Open **Documents → Upload documents**. Choose the appropriate category and reporting period. Successful files are retained independently. Category is an organizational label, not a restriction on which data the file can provide.
3. Staff uploads can **Extract supported data after upload**. For previously uploaded documents, choose **Extract all ready documents**, or **Extract data / refresh preview** beside a file. Extraction does not verify the contents.
4. Click **View data preview**, or open **Data preview and review**. Filter by source, proposed property/sheet and period. You can view unreviewed values immediately. Different forecasts, portfolio totals and property components are not added together.
5. Expand extraction results for sheets/pages and warnings. **Read extracted text** opens PDF page text. Image-only scans need OCR; this release cannot read their images. Unknown layouts may need manual mapping.
6. For review, filter to one source, property/sheet and period, then expand **Review a selected group together**. Select relevant rows (up to 200). **Confirm selected context** records the correct property or organization scope, period, basis, currency and scale as traceable corrections. Original proposals remain in history.
7. **Review selected facts** applies a documented decision. Verification still checks every selected fact and blocks incomplete context, material conflicts and duplicate verified facts. Inspect exceptions rather than approving every imported row indiscriminately.
8. **Export draft data CSV** includes unreviewed source observations and status labels. **Export verified facts CSV** includes eligible verified facts only and explains if none are available.

## Where V4 fits

The new preview shows extracted source data. It is not a complete underwriting report. The CSV exports are not V4.2 workbooks. An analyst still prepares the relevant data in the V4.2 assessment workbook and uploads that workbook at `/v4.html` for the existing financial dashboards. The portal does not automatically build or refresh a V4 assessment.

## When something fails

- **Quarantined/rejected:** resolve screening or provide an appropriate clean original before extraction.
- **Extraction failed:** open the warning; the successful upload remains saved. Split, convert or correct the source, then retry extraction.
- **OCR required:** provide a reviewed, searchable PDF or the original spreadsheet.
- **No supported fields:** inspect page text or use manual source-linked entry; missing data is not zero.
- **Conflicting values:** check scope, scenario, source version, period and units before choosing or correcting a value.
