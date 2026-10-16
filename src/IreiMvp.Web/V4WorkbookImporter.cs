using System.Globalization;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

// Deliberately not an Excel evaluator. Only source links and literal numeric settings are
// accepted as inputs. Financial and analysis formulas are executed by V4CalculationEngine.
public sealed class V4WorkbookImporter(XlsxWorkbookService xlsx)
{
    public V4Assessment Read(string path, string id, string fileName)
    {
        var workbook = xlsx.Read(path);
        if (!workbook.HasSheet("02_DISPLAY_CONFIG") || !workbook.HasSheet("57_OPTION_ANALYSIS"))
            throw new InvalidDataException("Use the V4.2 template. Other models require analyst mapping through the retained V3 intake.");
        var result = new V4Assessment { Id = id, FileName = Path.GetFileName(fileName), Sha256 = workbook.Sha256 };
        foreach (var sheet in workbook.Sheets.Values)
        {
            // Recognize named headers, not data row positions or the densest data row.
            var header = Enumerable.Range(1, Math.Min(20, sheet.MaxRow)).FirstOrDefault(r =>
                Enumerable.Range(1, sheet.MaxColumn).Any(c => WorkbookValue.Text(sheet.GetValue(r, c)).EndsWith(" *")));
            if (header == 0) header = Enumerable.Range(1, Math.Min(20, sheet.MaxRow)).FirstOrDefault(r =>
                WorkbookValue.Text(sheet.GetValue(r, 1)) is "Editable assumption" or "Capacity_Test_ID");
            if (header == 0) continue;
            var headers = Enumerable.Range(1, sheet.MaxColumn).ToDictionary(c => c,
                c => WorkbookValue.Text(sheet.GetValue(header, c)).Replace(" *", ""));
            var rows = new List<V4Row>();
            for (var r = header + 1; r <= sheet.MaxRow; r++)
            {
                var first = WorkbookValue.Text(sheet.GetValue(r, 1));
                if (first.Length == 0 && sheet.Cells.GetValueOrDefault(XlsxAddress.ToCellReference(r, 1))?.Formula is null) continue;
                if (first == headers[1]) continue; // repeated header
                var fields = new Dictionary<string, string>(); var formulas = new Dictionary<string, string>();
                foreach (var (c, key) in headers.Where(h => h.Value.Length > 0))
                {
                    var cell = XlsxAddress.ToCellReference(r, c);
                    var snapshot = sheet.Cells.GetValueOrDefault(cell);
                    var formula = snapshot?.Formula;
                    if (formula is not null) formulas[key] = formula;
                    var resolved = Resolve(workbook, sheet.Name, cell, [], 0);
                    // Derived/output sheet caches are never inputs. Keep formula text for review.
                    fields[key] = WorkbookValue.Text(resolved);
                }
                rows.Add(new V4Row(fields.GetValueOrDefault(headers[1], first), sheet.Name, r, fields, formulas));
            }
            result.Registers[sheet.Name] = rows;
        }
        var assessment = result.Rows("01_ASSESSMENT").SingleOrDefault()
            ?? throw new InvalidDataException("Complete one assessment record before upload.");
        result.AssessmentId = assessment.Text("Assessment_ID"); result.OrganizationId = assessment.Text("Organization_ID");
        result.Currency = assessment.Text("Default_Currency");
        var org = result.Rows("30_ORGANIZATION").SingleOrDefault(o => o.Text("Organization_ID") == result.OrganizationId);
        result.Organization = org?.Text("Legal_Name") ?? "";
        if (result.AssessmentId.Length == 0 || result.OrganizationId.Length == 0 || result.Organization.Length == 0 || result.Currency.Length == 0)
            throw new InvalidDataException("Assessment, organization, legal name and currency are required.");
        result.Config = result.Rows("02_DISPLAY_CONFIG").ToDictionary(r => r.Text("Config_Key"), r => r.Text("Config_Value"));
        if (result.Setting("PORTFOLIO_PROPERTY_ID").Length == 0 || result.Setting("CURRENT_FISCAL_YEAR").Length == 0)
            throw new InvalidDataException("Current fiscal year and portfolio scope are required.");
        var mappings = result.Rows("20_FIELD_MAPPING").GroupBy(r => r.Id).ToDictionary(g => g.Key, g =>
        {
            if (g.Count() == 1) return g.Single();
            result.ImportFindings.Add($"Mapping {g.Key}: duplicate candidates retained; affected facts are Conflict.");
            var first = g.First(); var fields = new Dictionary<string, string>(first.Fields) { ["Mapping_Status"] = "Conflict" };
            return first with { Fields = fields };
        });
        var sourceIds = result.Rows("10_SOURCE_REGISTER").Select(r => r.Id).ToHashSet();
        var propertyIds = result.Rows("31_PROPERTY").Where(r => r.Text("Organization_ID") == result.OrganizationId)
            .Select(r => r.Id).Append(result.Setting("PORTFOLIO_PROPERTY_ID")).ToHashSet();
        var schemes = new[] { ("32_FINANCIAL", "Amount", "Metric_Code", "Currency"),
            ("33_DEBT_SCHEDULE", "Principal_Payment", "", "Currency"),
            ("34_CASH_RESERVES", "Amount", "", "Currency"),
            ("31_PORTFOLIO_UNITS", "Total_Units", "", "") };
        foreach (var (sheet, amount, codeField, currencyField) in schemes)
        foreach (var row in result.Rows(sheet))
        {
            if (row.Text("Assessment_ID") != result.AssessmentId ||
                (row.Text("Organization_ID").Length > 0 && row.Text("Organization_ID") != result.OrganizationId))
            { result.ImportFindings.Add($"{sheet}!{row.Row}: assessment/organization mismatch; excluded."); continue; }
            var scope = row.Text("Property_ID"); var year = row.Text("Fiscal_Year");
            if (row.Text("Input_Status") == "Excluded") continue;
            if (!propertyIds.Contains(scope) || !sourceIds.Contains(row.Text("Source_ID")) ||
                (currencyField.Length > 0 && row.Text(currencyField) != result.Currency))
            { result.ImportFindings.Add($"{sheet}!{row.Row}: unknown scope/source or currency mismatch; excluded."); continue; }
            if (!Regex.IsMatch(year, @"^\d{4}-\d{4}$") || int.Parse(year[5..], CultureInfo.InvariantCulture) != int.Parse(year[..4], CultureInfo.InvariantCulture) + 1)
            { result.ImportFindings.Add($"{sheet}!{row.Row}: invalid fiscal year {year}; analyst normalization required."); continue; }
            var mapping = mappings.GetValueOrDefault(row.Text("Mapping_ID"));
            var value = row.Number(amount); var code = row.Text(codeField);
            var amountField = amount;
            if (sheet == "33_DEBT_SCHEDULE")
            {
                var principal = row.Number("Principal_Payment"); var interest = row.Number("Interest_Payment");
                value = principal + interest; code = "DEBT_SERVICE";
                // A reviewed source total is supported without inventing a principal/interest split.
                if (principal is null && interest is null && row.Text("Review_Status") is "Verified" or "Confirmed")
                { value = row.Number("Total_Debt_Service"); amountField = "Total_Debt_Service"; }
            }
            if (sheet == "34_CASH_RESERVES") code = "RESERVE_CLOSING";
            if (sheet == "31_PORTFOLIO_UNITS") code = "HOMES_TOTAL";
            var grain = row.Text("Record_Grain");
            if (grain.Length == 0 && sheet != "32_FINANCIAL") grain = scope == result.Setting("PORTFOLIO_PROPERTY_ID") ? "Portfolio control" : "Property detail";
            var state = row.Text("Review_Status") == "Rejected" || mapping?.Text("Mapping_Status") == "Rejected" ? "Missing" :
                row.Text("Review_Status") == "Conflict" || mapping?.Text("Mapping_Status") == "Conflict" ? "Conflict" : value.HasValue ? "Available" : "Missing";
            if (state != "Available") value = null;
            var header = workbook.Sheets[sheet];
            var headerRow = Enumerable.Range(1, Math.Min(20, header.MaxRow)).First(r => Enumerable.Range(1, header.MaxColumn)
                .Any(c => WorkbookValue.Text(header.GetValue(r, c)).Replace(" *", "") == amountField));
            var amountColumn = Enumerable.Range(1, header.MaxColumn).First(c => WorkbookValue.Text(header.GetValue(headerRow, c)).Replace(" *", "") == amountField);
            var cell = XlsxAddress.ToCellReference(row.Row, amountColumn); var chain = new List<string>();
            Resolve(workbook, sheet, cell, chain, 0);
            if (sheet == "33_DEBT_SCHEDULE" && amountField == "Principal_Payment") Resolve(workbook, sheet, XlsxAddress.ToCellReference(row.Row, amountColumn + 1), chain, 0);
            result.Facts.Add(new V4Fact(row.Id, scope, year, row.Text("Scenario"), code, value, state,
                grain, row.Text("Review_Status"), mapping?.Text("Mapping_Status") ?? "Unmapped", row.Text("Certainty_Flag"),
                result.Currency, row.Text("Source_ID"), row.Text("Source_Location"), $"{sheet}!{cell}",
                row.Formulas.GetValueOrDefault(amountField, ""), chain.Distinct().ToArray()));
        }
        ValidateEnums(result);
        V4CalculationEngine.Calculate(result);
        result.Baseline = new V4Baseline("BASE-" + id, result.AssessmentId, result.OrganizationId, result.CreatedUtc,
            result.Setting("CURRENT_FISCAL_YEAR"), result.Setting("PORTFOLIO_PROPERTY_ID"), result.MethodologyVersion,
            result.Metrics.ToArray());
        return result;
    }

    private static object? Resolve(WorkbookSnapshot workbook, string sheet, string cell, List<string> chain, int depth)
    {
        if (depth > 16 || !workbook.Sheets.TryGetValue(sheet, out var s)) return null;
        chain.Add($"{sheet}!{cell}");
        var item = s.Cells.GetValueOrDefault(cell); if (item is null) return null;
        if (string.IsNullOrEmpty(item.Formula)) return item.Value;
        var formula = item.Formula.TrimStart('=');
        var reference = Regex.Match(formula, @"^'([^']+)'!\$?([A-Z]+)\$?(\d+)$");
        if (reference.Success) return Resolve(workbook, reference.Groups[1].Value, reference.Groups[2].Value + reference.Groups[3].Value, chain, depth + 1);
        if (decimal.TryParse(formula, NumberStyles.Number, CultureInfo.InvariantCulture, out var literal)) return literal;
        return null; // no fallback to an arbitrary calculation cache
    }

    private static void ValidateEnums(V4Assessment a)
    {
        foreach (var guide in a.Rows("00_FIELD_GUIDE"))
        {
            var allowed = guide.Text("Allowed_Values");
            if (!allowed.Contains(';') || allowed.Contains("Formula")) continue;
            var values = allowed.Split(';', StringSplitOptions.TrimEntries).ToHashSet();
            foreach (var row in a.Rows(guide.Text("Sheet")))
            {
                var value = row.Text(guide.Text("Field"));
                if (value.Length > 0 && !values.Contains(value) && !row.Formulas.ContainsKey(guide.Text("Field")))
                    a.ImportFindings.Add($"{row.Sheet}!{row.Row} {guide.Text("Field")}: '{value}' is outside this field's allowed values; retained for review.");
            }
        }
    }
}
