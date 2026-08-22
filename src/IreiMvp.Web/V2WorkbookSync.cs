namespace IreiMvp.Web;

public sealed class V2WorkbookSync
{
    private readonly XlsxWorkbookService _xlsx;

    public V2WorkbookSync(XlsxWorkbookService xlsx)
    {
        _xlsx = xlsx;
    }

    public void Sync(
        SubmissionRecord record,
        OrganizationPortfolioState state)
    {
        if (!File.Exists(record.OutputPath))
        {
            return;
        }

        var workbook = _xlsx.Read(record.OutputPath);
        var patches = new List<CellPatch>();
        PatchControl(workbook, patches, "Organization / Demonstration Name", state.OrganizationName);
        PatchControl(workbook, patches, "Current Assessment Version", record.VersionId);
        PatchControl(workbook, patches, "Comparison Version", record.ParentVersionId ?? "Initial baseline");
        AppendSourceDocuments(workbook, patches, state.SourceDocuments);

        WriteTable(
            workbook,
            patches,
            "ASSESSMENT_VERSION",
            state.Assessments.OrderBy(item => item.VersionNumber).Select(item => Row(
                ("Version_ID", item.VersionId),
                ("Organization_ID", state.OrganizationId),
                ("Organization_Name", state.OrganizationName),
                ("Reporting_Period", item.ReportingPeriod),
                ("Version_Status", item.VersionStatus),
                ("Readiness_Status", item.ReadinessStatus),
                ("Source_Cutoff_Date", Date(item.SourceCutoffUtc)),
                ("Created_Date", Date(item.CreatedUtc)),
                ("Verified_Date", Date(item.LastVerifiedUtc)),
                ("Approved_Date", Date(item.ApprovedUtc)),
                ("Approved_By", item.ApprovedBy),
                ("Based_On_Version_ID", item.ParentVersionId),
                ("Notes", string.Join(" ", item.Warnings.Take(3))))));

        WriteTable(
            workbook,
            patches,
            "FIELD_HISTORY",
            state.FieldHistory.OrderBy(item => item.RecordedUtc).Select(item => Row(
                ("History_ID", item.HistoryId),
                ("Version_ID", item.VersionId),
                ("Entity_Type", item.EntityType),
                ("Entity_ID", item.EntityId),
                ("Field_Name", item.FieldName),
                ("Previous_Value", (object?)item.PreviousNumeric ?? item.PreviousValue),
                ("Current_Value", (object?)item.CurrentNumeric ?? item.CurrentValue),
                ("Change_Type", item.ChangeStatus),
                ("Change_Amount", item.ChangeAmount),
                ("Change_%", item.ChangePercent),
                ("Material_Flag", item.IsMaterial ? "Yes" : "No"),
                ("Source_ID", item.SourceReference),
                ("Review_Status", item.ReviewStatus),
                ("Notes", item.MaterialityRule))));

        WriteTable(
            workbook,
            patches,
            "CHANGE_EVENT",
            state.ChangeEvents.OrderBy(item => item.DetectedUtc).Select(item => Row(
                ("Change_ID", item.ChangeId),
                ("Version_ID", item.VersionId),
                ("Entity_Type", item.EntityType),
                ("Entity_ID", item.EntityId),
                ("Change_Category", item.Category),
                ("Field_or_Metric", item.FieldOrMetric),
                ("Previous_Value", item.PriorValue),
                ("Current_Value", item.CurrentValue),
                ("Direction", item.Direction),
                ("Materiality", item.IsMaterial ? "Material" : "Recorded"),
                ("Impact", item.Impact),
                ("Source_ID", ChangeSource(state, item)),
                ("Status", item.ReviewStatus),
                ("Notes", JoinNotes(
                    item.MaterialityBasis,
                    string.IsNullOrWhiteSpace(item.LinkedActionId) ? null : $"Linked action: {item.LinkedActionId}")))));

        WriteTable(
            workbook,
            patches,
            "ACTION_DECISION",
            state.Actions.OrderBy(item => item.CreatedUtc).Select(item => Row(
                ("Action_ID", item.ActionId),
                ("Version_ID", item.VersionId),
                ("Organization_ID", item.OrganizationId),
                ("Property_ID", item.EntityId),
                ("Issue_ID", item.LinkedChangeId),
                ("Action_Type", item.DecisionBody.Contains("Board", StringComparison.OrdinalIgnoreCase) ? "Decision" : "Action"),
                ("Description", item.Issue),
                ("Owner", item.Owner),
                ("Decision_Body", item.DecisionBody),
                ("Date_Identified", Date(item.CreatedUtc)),
                ("Due_Date", Date(item.DueDate)),
                ("Status", item.Status),
                ("Priority", item.RiskLevel),
                ("Decision_Date", Date(item.ClosedUtc)),
                ("Outcome", item.Outcome),
                ("Evidence_ID", item.EvidenceReference),
                ("Closed_Date", Date(item.ClosedUtc)),
                ("Notes", JoinNotes(item.Cause, item.Impact, item.Response)))));

        WriteTable(
            workbook,
            patches,
            "DATA_OBLIGATIONS",
            state.Obligations.OrderBy(item => item.CreatedUtc).Select(item => Row(
                ("Obligation_ID", item.ObligationId),
                ("Organization_ID", item.OrganizationId),
                ("Property_ID", item.EntityId),
                ("Obligation_Type", item.EntityType),
                ("Obligation", item.Obligation),
                ("Source_or_Agreement", item.Source),
                ("Owner", item.Owner),
                ("Decision_Body", "Management"),
                ("Frequency", item.Recurrence),
                ("Due_Date", Date(item.DueDate)),
                ("Status", item.Status),
                ("Due_Status", DueStatus(item)),
                ("Evidence_ID", item.EvidenceReference),
                ("Notes", item.Notes))));

        WriteTable(
            workbook,
            patches,
            "REPORT_SNAPSHOT",
            state.Reports.OrderBy(item => item.CreatedUtc).Select(item => Row(
                ("Report_ID", item.ReportId),
                ("Version_ID", item.VersionId),
                ("Report_Type", item.ReportName),
                ("Reporting_Period", item.Period),
                ("Generated_Date", Date(item.CreatedUtc)),
                ("Report_Status", item.Status),
                ("Approved_By", item.ApprovedBy),
                ("Approved_Date", Date(item.ApprovedUtc)),
                ("Intended_Recipient", item.Recipient),
                ("Scope", item.Scope),
                ("Supersedes_Report_ID", item.SupersedesReportId),
                ("Purpose", item.Purpose),
                ("File_or_Link", string.IsNullOrWhiteSpace(item.ArtifactPath) ? null : Path.GetFileName(item.ArtifactPath)),
                ("Notes", item.SharedUtc.HasValue ? $"Shared {Date(item.SharedUtc)} by {item.SharedBy}" : null))));

        _xlsx.Patch(record.OutputPath, patches);
    }

    private static void AppendSourceDocuments(
        WorkbookSnapshot workbook,
        List<CellPatch> patches,
        IReadOnlyCollection<SourceDocumentRecord> documents)
    {
        if (!workbook.Sheets.TryGetValue("STG_SOURCE_INTAKE", out var sheet))
        {
            return;
        }

        var headerRow = FindHeaderRow(sheet);
        if (headerRow == 0)
        {
            return;
        }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= sheet.MaxColumn; column++)
        {
            var header = WorkbookValue.Normalize(WorkbookValue.Text(sheet.GetValue(headerRow, column)));
            if (!string.IsNullOrWhiteSpace(header)) columns.TryAdd(header, column);
        }

        if (!columns.TryGetValue(WorkbookValue.Normalize("Source_ID"), out var idColumn))
        {
            return;
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextRow = headerRow + 1;
        for (var row = headerRow + 1; row <= Math.Max(sheet.MaxRow, headerRow + 1); row++)
        {
            var id = WorkbookValue.Text(sheet.GetValue(row, idColumn));
            if (!string.IsNullOrWhiteSpace(id))
            {
                existing.Add(id);
                nextRow = row + 1;
            }
        }

        foreach (var document in documents.Where(item => !existing.Contains(item.SourceId)))
        {
            var values = Row(
                ("Source_ID", document.SourceId),
                ("File / Source Name", document.FileName),
                ("Source Type", document.SourceType),
                ("Version / Date", Date(document.UploadedUtc)),
                ("Priority", "Assessment evidence"),
                ("Used For", "Assessment source / supporting evidence"),
                ("Review Status", document.ReviewStatus),
                ("Owner", "Organization"),
                ("Notes", string.IsNullOrWhiteSpace(document.Sha256) ? null : $"SHA-256 {document.Sha256}"));
            foreach (var pair in values)
            {
                if (!columns.TryGetValue(WorkbookValue.Normalize(pair.Key), out var column) || pair.Value is null)
                {
                    continue;
                }
                patches.Add(new CellPatch
                {
                    Sheet = sheet.Name,
                    Cell = XlsxAddress.ToCellReference(nextRow, column),
                    Value = pair.Value
                });
            }
            existing.Add(document.SourceId);
            nextRow++;
        }
    }

    private static void PatchControl(
        WorkbookSnapshot workbook,
        List<CellPatch> patches,
        string control,
        object? value)
    {
        if (!workbook.Sheets.TryGetValue("MVP_CONTROL", out var sheet))
        {
            return;
        }

        for (var row = 1; row <= sheet.MaxRow; row++)
        {
            if (!string.Equals(
                    WorkbookValue.Text(sheet.GetValue(row, 1)),
                    control,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            patches.Add(new CellPatch
            {
                Sheet = sheet.Name,
                Cell = XlsxAddress.ToCellReference(row, 2),
                Value = value
            });
            return;
        }
    }

    private static void WriteTable(
        WorkbookSnapshot workbook,
        List<CellPatch> patches,
        string sheetName,
        IEnumerable<IReadOnlyDictionary<string, object?>> sourceRows)
    {
        if (!workbook.Sheets.TryGetValue(sheetName, out var sheet))
        {
            return;
        }

        var headerRow = FindHeaderRow(sheet);
        if (headerRow == 0)
        {
            return;
        }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= sheet.MaxColumn; column++)
        {
            var header = WorkbookValue.Normalize(WorkbookValue.Text(sheet.GetValue(headerRow, column)));
            if (!string.IsNullOrWhiteSpace(header))
            {
                columns.TryAdd(header, column);
            }
        }

        var rows = sourceRows.ToList();
        var clearThrough = Math.Max(headerRow + rows.Count, Math.Min(sheet.MaxRow, headerRow + 200));
        for (var row = headerRow + 1; row <= clearThrough; row++)
        {
            foreach (var column in columns.Values)
            {
                if (!string.IsNullOrWhiteSpace(sheet.GetCell(row, column)?.Formula))
                {
                    continue;
                }

                patches.Add(new CellPatch
                {
                    Sheet = sheetName,
                    Cell = XlsxAddress.ToCellReference(row, column),
                    Clear = true
                });
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = headerRow + index + 1;
            foreach (var pair in rows[index])
            {
                if (!columns.TryGetValue(WorkbookValue.Normalize(pair.Key), out var column) ||
                    pair.Value is null ||
                    !string.IsNullOrWhiteSpace(sheet.GetCell(rowNumber, column)?.Formula))
                {
                    continue;
                }

                patches.Add(new CellPatch
                {
                    Sheet = sheetName,
                    Cell = XlsxAddress.ToCellReference(rowNumber, column),
                    Value = pair.Value
                });
            }
        }
    }

    private static int FindHeaderRow(SheetSnapshot sheet)
    {
        for (var row = 1; row <= Math.Min(sheet.MaxRow, 20); row++)
        {
            var count = Enumerable.Range(1, sheet.MaxColumn)
                .Count(column => !string.IsNullOrWhiteSpace(
                    WorkbookValue.Text(sheet.GetValue(row, column))));
            if (count >= 5)
            {
                return row;
            }
        }

        return 0;
    }

    private static IReadOnlyDictionary<string, object?> Row(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static string? Date(DateTimeOffset? value) => value?.UtcDateTime.ToString("yyyy-MM-dd");

    private static string DueStatus(ObligationRecord item)
    {
        if (string.Equals(item.Status, "Complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return "Complete";
        }

        if (!item.DueDate.HasValue) return "Not dated";
        if (item.DueDate.Value < DateTimeOffset.UtcNow) return "Overdue";
        if (item.DueDate.Value <= DateTimeOffset.UtcNow.AddDays(60)) return "Due <=60 days";
        return "Future";
    }

    private static string JoinNotes(params string?[] values) => string.Join(
        " | ",
        values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string? ChangeSource(
        OrganizationPortfolioState state,
        ChangeEventRecord change) => state.FieldHistory
        .LastOrDefault(history =>
            string.Equals(history.VersionId, change.VersionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(history.EntityType, change.EntityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(history.EntityId, change.EntityId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(history.FieldName, change.FieldOrMetric, StringComparison.OrdinalIgnoreCase))
        ?.SourceReference;
}
