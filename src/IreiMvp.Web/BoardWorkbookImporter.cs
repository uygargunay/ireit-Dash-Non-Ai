using System.Globalization;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

// Original spreadsheet values are source observations, never Excel-evaluated results.
// Portfolio controls and property detail are kept at separate grains.
public sealed class BoardWorkbookImporter(XlsxWorkbookService xlsx, V4WorkbookImporter canonical)
{
    internal static string Text(SheetSnapshot s, int r, int c) => WorkbookValue.Text(s.GetValue(r, c)).Trim();
    internal static string Norm(string s) => Regex.Replace(s.ToUpperInvariant(), @"[^A-Z0-9]", "");
    internal static int Find(SheetSnapshot s, Func<string, bool> predicate) => Enumerable.Range(1, s.MaxRow)
        .FirstOrDefault(r => predicate(Norm(Text(s, r, 2))));
    internal static string Location(string sheet, int row, int col) => $"'{sheet.Replace("'", "''")}'!{XlsxAddress.ToCellReference(row, col)}";
    public static V4Row Row(string sheet, string id, params (string Key, string Value)[] fields) =>
        new(id, sheet, 0, fields.ToDictionary(f => f.Key, f => f.Value), []);

    public V4Assessment Read(string path, string id, string name, string organization, string currentYear)
    {
        var w = xlsx.Read(path);
        if (w.HasSheet("02_DISPLAY_CONFIG") && w.HasSheet("57_OPTION_ANALYSIS"))
            return canonical.Read(path, id, name);
        var candidates = w.Sheets.Values.Where(s => Find(s, l => l == "TOTALREVENUE") > 0 &&
            Find(s, l => l.Contains("MORTGAGEPRINCIPAL")) > 0 && Find(s, l => l.Contains("MORTGAGEINTEREST")) > 0).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("No unique portfolio cash-flow control was found. Upload a populated V4.2 workbook, or a source model with named total revenue, society/building costs and principal/interest payment rows. No source was guessed.");
        var s = candidates[0]; var years = Years(s).ToArray();
        if (string.IsNullOrWhiteSpace(currentYear))
        {
            var now = DateTimeOffset.UtcNow; var start = now.Month >= 4 ? now.Year : now.Year - 1;
            currentYear = $"{start}-{start + 1}";
        }
        if (years.Length < 2) throw new InvalidDataException("At least two consecutive fiscal-year columns are required.");
        var title = string.Join(" ", s.Cells.Where(c => XlsxAddress.Parse(c.Key).Row <= 2).Select(c => WorkbookValue.Text(c.Value.Value)));
        var end = Regex.Match(title, @"(?:19|20)\d{2}\s*[-–]\s*((?:19|20)\d{2})");
        if (end.Success) years = years.Where(y => int.Parse(y.Year[5..], CultureInfo.InvariantCulture) <= int.Parse(end.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        if (years.Length < 2) throw new InvalidDataException("The source title and fiscal-year columns do not define a usable horizon.");
        var a = new V4Assessment { Id = id, AssessmentId = "ASM-" + id, OrganizationId = "ORG-" + id,
            Organization = string.IsNullOrWhiteSpace(organization) ? Path.GetFileNameWithoutExtension(name) : organization.Trim(),
            Currency = "CAD", Sha256 = w.Sha256, FileName = name, MethodologyVersion = "V2-BOARD/1" };
        const string scope = "PORTFOLIO";
        a.Config = new() { ["PORTFOLIO_PROPERTY_ID"] = scope, ["CURRENT_FISCAL_YEAR"] = years.Any(y => y.Year == currentYear) ? currentYear : years[0].Year,
            ["HORIZON_END_FISCAL_YEAR"] = years[^1].Year, ["TARGET_COVERAGE"] = "1.2", ["SHOW_DRAFT_NARRATIVE"] = "Yes",
            ["EVIDENCE_SOURCE_VERSION"] = "Original source model", ["REPORTING_AS_OF_DATE"] = DateTimeOffset.UtcNow.ToString("O") };
        a.ImportFindings.Add("Original source model: saved numeric values are retained as unverified source observations; Excel formulas are not executed. CAD and the 1.20x screening target are visible provisional intake assumptions.");
        void AddRow(V4Row row) { if (!a.Registers.TryGetValue(row.Sheet, out var list)) a.Registers[row.Sheet] = list = []; list.Add(row); }
        void Issue(string code, string titleText, string detail, string source = "") => AddRow(Row("51_EXCEPTIONS", code,
            ("Exception_ID", code), ("Assessment_ID", a.AssessmentId), ("Issue_Type", titleText), ("Description", detail),
            ("Question_For_Client", detail), ("Why_It_Matters", "May change the interpretation or decision."), ("Status", "Open"),
            ("Materiality", "Material"), ("Show_On_Evidence_Page", "Yes"), ("Source_Location", source), ("Suggested_Respondent", "CFO / management")));
        AddRow(Row("10_SOURCE_REGISTER", "SOURCE", ("File_Name", name), ("Source_Type", "Cash-flow model"),
            ("Authoritative_Status", "Primary"), ("Review_Status", "Under review"), ("Checksum_or_File_Key", w.Sha256)));
        for (var i = 0; i < years.Length; i++)
        {
            a.Config[$"HORIZON_FY_{i + 1:D2}"] = years[i].Year;
            a.Config[$"HORIZON_SCENARIO_{i + 1:D2}"] = "Projection";
            if (Text(s, years[i].Row, years[i].Col) != years[i].Year)
                Issue("YEAR-" + i, "Malformed fiscal-year header", $"Source header '{Text(s, years[i].Row, years[i].Col)}' normalized to {years[i].Year} from consecutive adjacent years; confirm this interpretation.", Location(s.Name, years[i].Row, years[i].Col));
        }
        void Fact(SheetSnapshot sheet, int row, int col, string property, string year, string code, string certainty = "", bool conflict = false)
        {
            if (row == 0) return;
            var address = XlsxAddress.ToCellReference(row, col); var cell = sheet.Cells.GetValueOrDefault(address);
            var value = WorkbookValue.Decimal(cell?.Value);
            var location = Location(sheet.Name, row, col);
            a.Facts.Add(new V4Fact("INPUT-" + a.Facts.Count, property, year, "Projection", code,
                conflict ? null : value, conflict ? "Conflict" : value.HasValue ? "Available" : "Missing",
                property == scope ? "Portfolio control" : "Property detail", "Under review", "Proposed", certainty,
                a.Currency, "SOURCE", location, location, cell?.Formula ?? "", [location]));
        }
        var revenue = Find(s, l => l == "TOTALREVENUE");
        var society = Find(s, l => l == "TOTALSOCIETYOPERATINGEXPENSES");
        var building = Find(s, l => l.StartsWith("TOTALBUILDINGOPERATINGEXPENSES"));
        if (society == 0 || building == 0) throw new InvalidDataException("Both named society and building operating expense controls are required. Costs were not inferred from loan payments or net income.");
        var principal = Find(s, l => l.Contains("MORTGAGEPRINCIPAL")); var interest = Find(s, l => l.Contains("MORTGAGEINTEREST"));
        var closing = Find(s, l => l.EndsWith("NETRESERVEFUNDS"));
        var opening = Find(s, l => l.EndsWith("RESERVEFUNDS") && !l.Contains("NET"));
        foreach (var y in years)
        {
            Fact(s, revenue, y.Col, scope, y.Year, "REVENUE"); Fact(s, society, y.Col, scope, y.Year, "OPEX"); Fact(s, building, y.Col, scope, y.Year, "OPEX");
            var p = WorkbookValue.Decimal(s.GetValue(principal, y.Col)); var n = WorkbookValue.Decimal(s.GetValue(interest, y.Col));
            var location = Location(s.Name, principal, y.Col) + "; " + Location(s.Name, interest, y.Col);
            a.Facts.Add(new("DEBT-" + y.Year, scope, y.Year, "Projection", "DEBT_SERVICE", p + n, p.HasValue && n.HasValue ? "Available" : "Missing",
                "Portfolio control", "Under review", "Proposed", "", a.Currency, "SOURCE", location, location, "Principal + interest", [Location(s.Name, principal, y.Col), Location(s.Name, interest, y.Col)]));
            Fact(s, closing, y.Col, scope, y.Year, "RESERVE_CLOSING");
            foreach (var (code, row) in new[] { ("REV_RENT_EXISTING", Find(s, l => l.StartsWith("RENTALREVENUE") && l.Contains("NONEW"))),
                ("REV_RENT_NEW", Find(s, l => l.StartsWith("RENTALREVENUE") && l.Contains("NEWPROJECTS") && !l.Contains("NONEW"))),
                ("REV_SUBSIDY", Find(s, l => l.StartsWith("BCHOUSINGSUBSIDY"))), ("REV_OTHER", Find(s, l => l == "TOTALOTHERINCOME")),
                ("RESERVE_CONTRIBUTION", Find(s, l => l.StartsWith("CAPITALRESERVEFUNDS"))) }) Fact(s, row, y.Col, scope, y.Year, code);
            if (opening > 0) AddRow(Row("11_RAW_STAGING", "OPEN-" + y.Year, ("Assessment_ID", a.AssessmentId), ("Raw_Label", "Opening reserve"),
                ("Raw_Value_Number", Text(s, opening, y.Col)), ("Context_Text", y.Year), ("Source_Location", Location(s.Name, opening, y.Col))));
        }
        var propertySheets = w.Sheets.Values.Where(p => p != s && Find(p, l => l == "TOTALREVENUE") > 0 && Find(p, l => l is "TOTALOPERATINGEXPENSE" or "TOTALOPERATINGEXPENSES") > 0 && !Regex.IsMatch(p.Name, "summary|bond|cash.flow", RegexOptions.IgnoreCase)).ToArray();
        foreach (var p in propertySheets)
        {
            var py = Years(p).ToDictionary(y => y.Year); var pr = Find(p, l => l == "TOTALREVENUE");
            var op = Find(p, l => l is "TOTALOPERATINGEXPENSE" or "TOTALOPERATINGEXPENSES");
            var pay = Enumerable.Range(1, p.MaxRow).LastOrDefault(r => Norm(Text(p, r, 2)) is "MORTGAGE" or "MORTGAGEPAYMENTS");
            var property = "PROPERTY-" + Norm(p.Name); var nameText = Text(p, 1, 1); if (nameText.Length == 0) nameText = p.Name;
            var available = years.Where(y => py.ContainsKey(y.Year)).ToArray();
            var planned = available.Take(2).All(y => WorkbookValue.Decimal(p.GetValue(pr, py[y.Year].Col)) == 0) && available.Any(y => WorkbookValue.Decimal(p.GetValue(pr, py[y.Year].Col)) > 0);
            AddRow(Row("31_PROPERTY", property, ("Organization_ID", a.OrganizationId), ("Property_Name", nameText), ("Include_in_Assessment", "Yes"),
                ("Operating_Status", planned ? "Planned - development" : "Operating"), ("Source_Location", Location(p.Name, 1, 1)), ("Review_Status", "Under review")));
            var signed = available.Any(y => pay > 0 && WorkbookValue.Decimal(p.GetValue(pay, py[y.Year].Col)) < 0);
            if (signed) Issue("SIGN-" + property, "Building payment sign requires review", $"Reconcile signed mortgage payments for {nameText}. Building coverage is withheld; portfolio payments remain separate.", Location(p.Name, pay, 2));
            var subsidy = Find(p, l => l == "SUBSIDY");
            var placeholder = subsidy > 0 && Text(p, subsidy, 1).Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
            foreach (var y in available)
            {
                var col = py[y.Year].Col;
                Fact(p, pr, col, property, y.Year, "REVENUE"); Fact(p, op, col, property, y.Year, "OPEX");
                Fact(p, pay, col, property, y.Year, "DEBT_SERVICE", conflict: signed);
                if (placeholder) Fact(p, subsidy, col, property, y.Year, "REV_SUBSIDY", "Placeholder");
            }
            if (placeholder) AddRow(Row("12_SOURCE_NOTES", "NOTE-" + property, ("Source_ID", "SOURCE"), ("Property_ID", property),
                ("Raw_Note_Text", Text(p, subsidy, 1)), ("Source_Location", Location(p.Name, subsidy, 1)), ("Note_Type", "Placeholder")));
        }
        var homes = w.Sheets.Values.Where(p => Find(p, l => l is "UNITS" or "TOTALUNITS" or "HOMES") > 0).OrderByDescending(p => p.Name.Contains("ALL", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (homes.Length > 0)
        {
            var h = homes[0]; var hr = Find(h, l => l is "UNITS" or "TOTALUNITS" or "HOMES"); var hy = Years(h).ToDictionary(y => y.Year);
            foreach (var y in years.Where(y => hy.ContainsKey(y.Year))) Fact(h, hr, hy[y.Year].Col, scope, y.Year, "HOMES_TOTAL");
            if (homes.Length > 1) Issue("HOMES-SOURCE", "Competing home-count summaries", $"Working source: {h.Name}. Other summary counts remain unverified. Confirm the controlling register and actual occupancy.", h.Name);
        }
        // A plateau is only a proposed planning milestone; it is never a verified delivery date.
        var homeFacts = a.Facts.Where(f => f.Code == "HOMES_TOTAL").OrderBy(f => f.Year).ToArray();
        var full = homeFacts.Skip(1).FirstOrDefault(f => f.Value == homeFacts.Max(x => x.Value) && homeFacts.Any(x => x.Year.CompareTo(f.Year) < 0 && x.Value == f.Value));
        a.Config["FULL_OPERATION_FISCAL_YEAR"] = full?.Year ?? years[^1].Year;
        Issue("MILESTONES", "Planning milestone requires confirmation", $"Full-operation screen: {a.Config["FULL_OPERATION_FISCAL_YEAR"]}, inferred from the selected homes plateau or horizon end. Obtain project dates and lender terms.");
        foreach (var (code, question) in new[] { ("TERMS", "Supply current lender statements and renewal outcomes."), ("RESERVE", "Confirm permitted reserve use, minimum floor and bank reconciliation."),
            ("SUBSIDY", "Confirm written commitments for source-labeled placeholder subsidy."), ("RENT", "Supply actual rent rolls and occupied homes; projections are not current occupancy."),
            ("CONDITION", "Supply condition reports, capital plans and affordability/governance limits.") }) Issue(code, question, question);
        V4CalculationEngine.Calculate(a);
        a.Baseline = new("BASE-" + id, a.AssessmentId, a.OrganizationId, a.CreatedUtc, a.Setting("CURRENT_FISCAL_YEAR"), scope, a.MethodologyVersion, a.Metrics.ToArray());
        return a;
    }

    internal static IEnumerable<(string Year, int Col, int Row)> Years(SheetSnapshot s)
    {
        var row = Enumerable.Range(1, Math.Min(20, s.MaxRow)).FirstOrDefault(r => Enumerable.Range(1, Math.Min(5, s.MaxColumn)).Any(c => Norm(Text(s, r, c)) == "FISCALYEAR"));
        if (row == 0) yield break;
        string? previous = null;
        for (var col = 3; col <= Math.Min(s.MaxColumn, 80); col++)
        {
            var value = Text(s, row, col); var match = Regex.Match(value, @"^(\d{4})-(\d{4})$");
            string? normalized = match.Success && int.Parse(match.Groups[2].Value) == int.Parse(match.Groups[1].Value) + 1 ? value : null;
            if (normalized is null && previous is not null && Regex.IsMatch(Text(s, row, col + 1), @"^\d{4}-\d{4}$"))
            {
                var next = Text(s, row, col + 1); var expected = int.Parse(previous[..4]) + 1;
                if (int.Parse(next[..4]) == expected + 1) normalized = $"{expected}-{expected + 1}";
            }
            if (normalized is null || previous is not null && int.Parse(normalized[..4]) != int.Parse(previous[..4]) + 1) yield break;
            yield return (normalized, col, row); previous = normalized;
        }
    }
}
