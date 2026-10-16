using System.Globalization;
using System.Text.Json;

namespace IreiMvp.Web;

public sealed class IntakeExporter(XlsxWorkbookService xlsx, IWebHostEnvironment env)
{
    private const string TemplateName = "IREI_Stage1_MVP_V4.2_Live_Underwriting_Template_BLANK.xlsx";
    public void Export(IntakeBatch b, string path)
    {
        var template = Path.Combine(env.ContentRootPath, "Data", "Templates", TemplateName);
        var w = xlsx.Read(template); var patches = new List<CellPatch>(); var manifest = new List<object>();
        var next = new Dictionary<string, int>();
        var accepted = b.Candidates.Where(c => c.State == "Accepted").ToArray();
        if (accepted.Length == 0) throw new InvalidDataException("Accept source-linked numeric inputs before export.");
        foreach (var g in accepted.GroupBy(c => (c.Scope, c.Year)))
        {
            if (g.Select(c => c.Scenario).Distinct().Count() > 1) throw new InvalidDataException("Choose one scenario per fiscal year; comparison cases remain candidates.");
            if (g.Any(c => c.Code == "OPEX") && g.Any(c => c.Code.StartsWith("OPEX_"))) throw new InvalidDataException("Select either a total operating-cost control or reviewed society/building components, never both.");
            if (g.Any(c => c.Code.StartsWith("OPEX_")) && !(g.Any(c => c.Code == "OPEX_SOCIETY") && g.Any(c => c.Code == "OPEX_BUILDINGS")))
                throw new InvalidDataException("Both society and building cost components are required; no absent component is assumed zero.");
            if (g.Any(c => c.Code == "DEBT_SERVICE") && g.Any(c => c.Code is "DEBT_PRINCIPAL" or "DEBT_INTEREST")) throw new InvalidDataException("Select debt-service total or components, never both.");
            if (g.Any(c => c.Code is "DEBT_PRINCIPAL" or "DEBT_INTEREST") && !(g.Any(c => c.Code == "DEBT_PRINCIPAL") && g.Any(c => c.Code == "DEBT_INTEREST")))
                throw new InvalidDataException("Both principal and interest are required; do not invent a zero component.");
        }
        // One scenario selector per year is a constraint of the existing engine.
        if (accepted.GroupBy(c => c.Year).Any(g => g.Select(c => c.Scenario).Distinct().Count() > 1)) throw new InvalidDataException("Current V4 supports one selected scenario per fiscal year.");
        int Write(string sheetName, Dictionary<string, object?> values)
        {
            var s = w.Sheets[sheetName]; var header = Enumerable.Range(1, Math.Min(20, s.MaxRow)).First(r => Enumerable.Range(1, s.MaxColumn).Any(c => WorkbookValue.Text(s.GetValue(r, c)).EndsWith(" *")));
            var headers = Enumerable.Range(1, s.MaxColumn).Where(c => WorkbookValue.Text(s.GetValue(header, c)).Length > 0)
                .ToDictionary(c => WorkbookValue.Text(s.GetValue(header, c)).Replace(" *", ""), c => c);
            var r = next.GetValueOrDefault(sheetName, header + 1);
            // Capacity expansion is deliberately not guessed; no silent truncation.
            if (r > s.MaxRow) throw new InvalidDataException($"{sheetName} exceeds the approved template capacity; expansion needs a tested adapter.");
            foreach (var (key, value) in values)
            {
                if (!headers.TryGetValue(key, out var col)) throw new InvalidDataException($"Unsupported target {sheetName}.{key}.");
                var cell = XlsxAddress.ToCellReference(r, col);
                if (s.GetCell(r, col)?.Formula is not null) throw new InvalidDataException($"Refusing to overwrite template formula {sheetName}!{cell}.");
                patches.Add(new() { Sheet = sheetName, Cell = cell, Value = value });
            }
            next[sheetName] = r + 1; return r;
        }
        var years = accepted.Select(c => c.Year).Append(b.CurrentYear).Distinct().Order().ToArray();
        if (years.Length > 11) throw new InvalidDataException("More than eleven fiscal years requires an approved horizon extension.");
        var currentStart = Start(b, b.CurrentYear); var currentEnd = currentStart.AddYears(1).AddDays(-1);
        Write("01_ASSESSMENT", new() { ["Assessment_ID"] = b.AssessmentId, ["Organization_ID"] = b.OrganizationId, ["Assessment_Version"] = b.Version,
            ["Status"] = "Working", ["Reporting_Period_Start"] = currentStart.ToString("yyyy-MM-dd"), ["Reporting_Period_End"] = currentEnd.ToString("yyyy-MM-dd"),
            ["Default_Currency"] = b.Currency, ["Methodology_Version"] = "V4.2-PILOT/1", ["Created_Date"] = DateTimeOffset.UtcNow.ToString("O"), ["Created_By"] = "Staff intake review" });
        var config = w.Sheets["02_DISPLAY_CONFIG"]; var settings = new Dictionary<string, object?> { ["CURRENT_FISCAL_YEAR"] = b.CurrentYear, ["CURRENT_PERIOD_START"] = currentStart.ToString("yyyy-MM-dd"),
            ["PORTFOLIO_PROPERTY_ID"] = "PORTFOLIO", ["HORIZON_END_FISCAL_YEAR"] = years.Last() };
        for (var i = 0; i < years.Length; i++) { settings[$"HORIZON_FY_{i + 1:00}"] = years[i]; settings[$"HORIZON_SCENARIO_{i + 1:00}"] = accepted.FirstOrDefault(c => c.Year == years[i])?.Scenario ?? "Projection"; }
        foreach (var (key, value) in settings)
        {
            var row = Enumerable.Range(7, config.MaxRow - 6).FirstOrDefault(r => WorkbookValue.Text(config.GetValue(r, 1)) == key);
            if (row == 0) throw new InvalidDataException("Configuration key missing: " + key);
            patches.Add(new() { Sheet = config.Name, Cell = XlsxAddress.ToCellReference(row, 2), Value = value });
        }
        Write("30_ORGANIZATION", new() { ["Organization_ID"] = b.OrganizationId, ["Legal_Name"] = b.OrganizationName, ["Entity_Type"] = "Other", ["Fiscal_Year_End"] = currentEnd.ToString("MM-dd"),
            ["Default_Currency"] = b.Currency, ["Source_ID"] = "INTAKE-CONTEXT", ["Source_Location"] = "Staff batch context", ["Review_Status"] = "Under review", ["Version_ID"] = b.Version });
        foreach (var scope in accepted.Where(c => c.Scope != "PORTFOLIO").Select(c => c.Scope).Distinct())
            Write("31_PROPERTY", new() { ["Property_ID"] = scope, ["Organization_ID"] = b.OrganizationId, ["Property_Name"] = scope, ["Ownership_Tenure"] = "Unknown", ["Operating_Status"] = "Other", ["Include_in_Assessment"] = "Yes", ["Review_Status"] = "Under review", ["Version_ID"] = b.Version });
        foreach (var s in b.Sources)
            Write("10_SOURCE_REGISTER", new() { ["Source_ID"] = s.Id, ["Assessment_ID"] = b.AssessmentId, ["File_Name"] = s.Name, ["Source_Type"] = "Other", ["Version"] = s.Id,
                ["Checksum_or_File_Key"] = s.Sha256, ["Received_Date"] = s.UploadedUtc.ToString("O"), ["Authoritative_Status"] = "Supporting", ["Review_Status"] = "Under review", ["Storage_Reference"] = $"intake/{b.Id}/{s.Id}" });
        foreach (var c in accepted)
        {
            var locator = $"'{c.Sheet}'!{c.Cell}";
            Write("11_RAW_STAGING", new() { ["Raw_Record_ID"] = c.Id, ["Assessment_ID"] = b.AssessmentId, ["Source_ID"] = c.SourceId, ["Source_Location"] = locator, ["Raw_Label"] = c.Label,
                ["Raw_Value_Text"] = c.RawText, ["Context_Text"] = c.Formula + " | " + c.Rationale, ["Capture_Method"] = "System-Extracted", ["Extraction_Tool"] = "spreadsheet/1" });
            Write("20_FIELD_MAPPING", new() { ["Mapping_ID"] = "MAP-" + c.Id, ["Assessment_ID"] = b.AssessmentId, ["Raw_Record_ID"] = c.Id, ["Source_ID"] = c.SourceId,
                ["Canonical_Entity"] = c.Code.StartsWith("DEBT_") ? "Debt schedule" : c.Code == "RESERVE_CLOSING" ? "Cash reserves" : c.Code == "HOMES_TOTAL" ? "Portfolio units" : "Financial", ["Canonical_Field"] = c.Code, ["Canonical_Value_Number"] = c.Value, ["Mapping_Status"] = "Confirmed",
                ["Reviewer"] = b.Reviews.Last(r => r.CandidateId == c.Id).Reviewer, ["Review_Date"] = b.Reviews.Last(r => r.CandidateId == c.Id).Date.ToString("O"), ["Notes"] = c.Rationale });
            if (c.Code is "DEBT_PRINCIPAL" or "DEBT_INTEREST") continue;
            var start = Start(b, c.Year); var end = start.AddYears(1).AddDays(-1);
            var common = new Dictionary<string, object?> { ["Assessment_ID"] = b.AssessmentId, ["Organization_ID"] = b.OrganizationId, ["Property_ID"] = c.Scope, ["Fiscal_Year"] = c.Year,
                ["Scenario"] = c.Scenario, ["Source_ID"] = c.SourceId, ["Source_Location"] = locator, ["Review_Status"] = "Verified", ["Version_ID"] = b.Version };
            string target; int row;
            if (c.Code == "HOMES_TOTAL")
            { target = "31_PORTFOLIO_UNITS"; common["Unit_Period_ID"] = c.Id; common["Total_Units"] = c.Value; common["Period_Start"] = start.ToString("yyyy-MM-dd"); common["Period_End"] = end.ToString("yyyy-MM-dd"); common["Mapping_ID"] = "MAP-" + c.Id; row = Write(target, common); }
            else if (c.Code == "RESERVE_CLOSING")
            { target = "34_CASH_RESERVES"; common["Cash_Record_ID"] = c.Id; common["Amount"] = c.Value; common["Currency"] = b.Currency; common["As_Of_Date"] = end.ToString("yyyy-MM-dd"); common["Cash_Type"] = "Other"; common["Restriction_Status"] = "Unknown"; row = Write(target, common); }
            else if (c.Code == "DEBT_SERVICE")
            { target = "33_DEBT_SCHEDULE"; common.Remove("Organization_ID"); common["Debt_Schedule_ID"] = c.Id; common["Debt_ID"] = "AGG-" + c.Scope; common["Total_Debt_Service"] = c.Value; common["Currency"] = b.Currency; common["Period_Start"] = start.ToString("yyyy-MM-dd"); common["Period_End"] = end.ToString("yyyy-MM-dd"); row = Write(target, common); }
            else
            { target = "32_FINANCIAL"; common["Financial_Record_ID"] = c.Id; common["Period_Start"] = start.ToString("yyyy-MM-dd"); common["Period_End"] = end.ToString("yyyy-MM-dd");
                common["Metric_Code"] = c.Code.StartsWith("OPEX_") ? "OPEX" : c.Code; common["Amount"] = c.Value; common["Currency"] = b.Currency; common["Mapping_ID"] = "MAP-" + c.Id; common["Record_Grain"] = c.Grain; common["Input_Status"] = "Ready"; common["Certainty_Flag"] = c.Certainty == "Placeholder" ? "Placeholder" : ""; row = Write(target, common); }
            manifest.Add(new { CandidateId = c.Id, c.SourceId, Locator = locator, Target = target, Row = row });
        }
        foreach (var group in accepted.Where(c => c.Code is "DEBT_PRINCIPAL" or "DEBT_INTEREST").GroupBy(c => (c.Scope, c.Year, c.Scenario)))
        {
            var principal = group.Single(c => c.Code == "DEBT_PRINCIPAL"); var interest = group.Single(c => c.Code == "DEBT_INTEREST");
            // Two evidence sources are kept in the manifest; schedule locator includes both, never a fabricated split.
            var locator = $"'{principal.Sheet}'!{principal.Cell}; '{interest.Sheet}'!{interest.Cell}";
            var start = Start(b, group.Key.Year);
            var row = Write("33_DEBT_SCHEDULE", new() { ["Debt_Schedule_ID"] = "DS-" + principal.Id, ["Assessment_ID"] = b.AssessmentId, ["Debt_ID"] = "AGG-" + group.Key.Scope,
                ["Property_ID"] = group.Key.Scope, ["Period_Start"] = start.ToString("yyyy-MM-dd"), ["Period_End"] = start.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd"), ["Fiscal_Year"] = group.Key.Year,
                ["Scenario"] = group.Key.Scenario, ["Principal_Payment"] = principal.Value, ["Interest_Payment"] = interest.Value, ["Total_Debt_Service"] = principal.Value + interest.Value,
                ["Currency"] = b.Currency, ["Source_ID"] = principal.SourceId, ["Source_Location"] = locator, ["Review_Status"] = "Verified", ["Version_ID"] = b.Version });
            manifest.Add(new { CandidateIds = group.Select(c => c.Id).ToArray(), SourceIds = group.Select(c => c.SourceId).ToArray(), Locator = locator, Target = "33_DEBT_SCHEDULE", Row = row });
        }
        // Context provenance is explicit and is not presented as an uploaded document.
        Write("10_SOURCE_REGISTER", new() { ["Source_ID"] = "INTAKE-CONTEXT", ["Assessment_ID"] = b.AssessmentId, ["File_Name"] = "Staff batch context", ["Source_Type"] = "Other", ["Received_Date"] = DateTimeOffset.UtcNow.ToString("O"), ["Authoritative_Status"] = "Supporting", ["Review_Status"] = "Under review" });
        xlsx.CopyAndPatch(template, path, patches);
        File.WriteAllText(path + ".manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static DateTime Start(IntakeBatch b, string year) => new(int.Parse(year[..4], CultureInfo.InvariantCulture), b.FiscalStartMonth, 1);
}
