using System.Globalization;

namespace IreiMvp.Web;

public sealed record V4Tile(string Label, string Value, string State, string Caption, string? MetricId, string Tone = "Teal");
public sealed record V4Section(string Id, string Title, string Component, string EmptyText, V4Row[] Records, string[] Notes);
public static class V4ViewService
{
    public static object Page(V4Assessment a, string page)
    {
        if (page is not ("P1" or "P2" or "P3" or "P4" or "P5" or "P6" or "P7")) throw new InvalidDataException("Unknown V4 page.");
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID"); var year = a.Setting("CURRENT_FISCAL_YEAR");
        var full = a.Setting("FULL_OPERATION_FISCAL_YEAR");
        V4Metric? M(string code, string? y = null) => a.Metric(scope, y ?? year, code);
        var properties = a.Rows("31_PROPERTY").Where(r => r.Text("Organization_ID") == a.OrganizationId && r.Text("Include_in_Assessment") == "Yes").DistinctBy(r => r.Id).ToArray();
        var exceptions = a.Rows("51_EXCEPTIONS").Where(r => r.Text("Assessment_ID") == a.AssessmentId).ToArray();
        var open = exceptions.Where(r => r.Text("Status") == "Open" && r.Text("Show_On_Evidence_Page") == "Yes").ToArray();
        var horizon = V4CalculationEngine.Years(a).Select(y => y.Year).ToArray();
        var peak = a.Metrics.Where(m => m.Scope == scope && m.Code == "DEBT_SERVICE" && m.Value.HasValue && horizon.Contains(m.Year)).OrderByDescending(m => m.Value).FirstOrDefault();
        V4Tile Tile(string code, string label, string caption, string? y = null)
        { var m = M(code, y); var tone = m?.Severity == "High" || (code is "CASH_SHORTFALL" && m?.Value > 0) ? "Red" :
            m?.Severity == "Medium" || code == "RESERVE_CLOSING" || m?.Value is null ? "Amber" : code == "DEBT_SERVICE" ? "Navy" : "Teal";
            return new(label, Format(m), m?.DisplayState ?? "No data available", caption, m?.Id, tone); }
        V4Tile Count(string label, int value, string caption) => new(label, value.ToString("N0", CultureInfo.InvariantCulture), "Register count", caption, null);
        var core = new[] { Tile("REVENUE", "Operating revenue", "Selected source control"), Tile("OPEX", "Running costs", "Society + building costs"),
            Tile("NOI", "Income after costs", "Before loan payments"), Tile("DEBT_SERVICE", "Loan payments", "Principal + interest"),
            Tile("DSCR", "Coverage", "Full precision controls risk"), Tile("CASH_SHORTFALL", "Cash shortfall", "Separate from target cushion"),
            Tile("RESERVE_CLOSING", "Closing reserve", "Permitted use unknown") };
        var existing = a.Metrics.Where(m => m.Scope == scope && m.Code == "HOMES_TOTAL" && m.Value.HasValue).OrderBy(m => m.Year).FirstOrDefault();
        var planned = M("HOMES_TOTAL", a.Setting("HORIZON_END_FISCAL_YEAR"));
        var tiles = page switch
        {
            "P1" => new[] { Count("Portfolio", properties.Length, "Included distinct buildings"), Tile("HOMES_TOTAL", "Homes projected", "Occupancy unconfirmed"), core[2], core[3], core[4], core[6], Tile("CASH_AFTER_DEBT", "Cash after loans", "Signed annual cash") },
            "P2" => new[] { Count("Properties", properties.Length, "Aliases retain stable IDs"), Tile("HOMES_TOTAL", "Homes projected", "Occupancy unconfirmed"), Tile("HOMES_TOTAL", "Homes planned", "Source plan", a.Setting("HORIZON_END_FISCAL_YEAR")),
                new V4Tile("Average actual rent", "No data available", "No data available", "Actual rent rolls required", null),
                new V4Tile("Homes protected", "No data available", "No data available", "Agreements required", null),
                new V4Tile("Good condition", "No data available", "No data available", "Current reports required", null),
                new V4Tile("People housed", "No data available", "No data available", "Resident records required", null) },
            "P4" => new[] { Count("Open questions", open.Length, "Eligible displayed Open records"), Count("Resolved", exceptions.Count(r => r.Text("Status") == "Resolved"), "Register status"), Count("Accepted exceptions", exceptions.Count(r => r.Text("Status") == "Exception accepted"), "Recorded rationale retained"), Count("Sources", a.Rows("10_SOURCE_REGISTER").Count(), "Retained immutable workbook"), Count("Import findings", a.ImportFindings.Count, "Field/schema review" ) },
            "P6" => new[] { Count("Actions", a.Rows("48_ACTIONS").Count(), "One shared register"), Count("Proposed", a.Rows("48_ACTIONS").Count(r => r.Text("Status") == "Proposed"), "Separate from decisions"), Count("Decisions", a.Rows("49_DECISIONS").Count(), "Append-only records"), Count("Updates", a.Events.Count, "Immutable version history"), Count("Outcomes measured", a.Outcomes.Count(o => o.CurrentValue.HasValue), "Evidence observations") },
            "P7" => new[] { Count("Report pathways", 4, "One portfolio record"), Count("Release authorizations", 0, "Pilot release remains blocked"), Count("Open questions", open.Length, "States travel with outputs"), new V4Tile("Current record", a.MethodologyVersion, "Working", "Not a financing approval", null) },
            _ => core
        };
        var sections = a.Rows("05_SECTION_REGISTRY").Where(r => r.Text("Page_ID") == page).OrderBy(r => r.Number("Display_Order")).Select(r =>
            new V4Section(r.Id, r.Text("Section_Title").Replace("{org}", a.Organization), r.Text("Component_Type"), r.Text("Empty_State_Text"),
                SectionRecords(a, r, page), [])).ToArray();
        var baseShortfall = M("CASH_SHORTFALL", full); var withoutShortfall = M("SHORTFALL_EX_PLACEHOLDER", full);
        var solutions = new List<string>();
        var placeholder = M("PLACEHOLDER_SUBSIDY", full)?.Value;
        string AnnualCash(V4Metric? m) => m?.Value is decimal v ? v == 0 ? "$0" : v < 1_000_000m ? "$" + (v / 1000m).ToString("0", CultureInfo.InvariantCulture) + "K" : Format(m) : "No data available";
        if (placeholder > 0)
            solutions.Add($"First option to test: confirm ${(placeholder.Value / 1_000_000m).ToString("0.000", CultureInfo.InvariantCulture)}M/year placeholder subsidy" +
                (baseShortfall?.Value > 0 ? $" plus {AnnualCash(baseShortfall)}/year residual cash relief in {full}." : $" in {full}. The source-plan cash requirement is {AnnualCash(baseShortfall)}."));
        if (withoutShortfall?.Value > 0)
        {
            solutions.Add($"Fallback: test {Format(withoutShortfall)}/year net income/support or annual payment relief. Terms and delivery require review.");
            solutions.Add($"A one-year {Format(withoutShortfall)} bridge could fund the cash gap; operating coverage remains {Format(M("DSCR_EX_PLACEHOLDER", full))} before added loan payments.");
        }
        if (solutions.Count == 0) solutions.Add(baseShortfall?.Value == 0 ?
            $"No cash shortfall is calculated for the selected full-operation source plan ({full}). Review evidence and permissions before sizing a financing pathway." :
            "No sized financial option is available until the selected case has usable income and annual payment evidence.");
        solutions.Add("Confirm current lender terms, reserve permissions, affordability and control limits before a financing decision.");
        var sensitivities = a.Rows("55_SENSITIVITY_INPUTS").Select(r => Sensitivity(a, r)).ToArray();
        var evidence = a.Rows("53_EVIDENCE_STATUS").Select(r => Evidence(a, r)).ToArray();
        var methods = a.Rows("40_CALC_REGISTRY").Select(r => new { Id = r.Id, Version = r.Text("Calc_Version"), Status = r.Text("Status"), Expression = r.Text("Formula_Expression") });
        return new {
            Context = new { a.Id, a.AssessmentId, a.OrganizationId, a.Organization, a.Currency, a.Version, a.Status, a.MethodologyVersion,
                CurrentYear = year, FullOperationYear = full, Scenario = V4CalculationEngine.Years(a).FirstOrDefault(y => y.Year == year).Scenario,
                Badge = "Pilot – management review", ConfigurationVersion = a.Version, a.Sha256,
                SourceVersion = a.Rows("10_SOURCE_REGISTER").FirstOrDefault(r => r.Text("Authoritative_Status") == "Primary")?.Text("Version"),
                PermittedActions = new[] { "Review", "Test option", "Record action", "Append decision" }, ExternalRelease = "Not authorized" },
            Pages = a.Rows("04_PAGE_REGISTRY"), Page = page, Tiles = tiles, Sections = sections,
            CurrentMetrics = a.Metrics.Where(m => m.Scope == scope && m.Year == year),
            FullOperationMetrics = a.Metrics.Where(m => m.Scope == scope && m.Year == full),
            FinancialOutlook = horizon.Select(y => new { Year = y, Metrics = a.Metrics.Where(m => m.Scope == scope && m.Year == y) }),
            CashChart = Chart(a, "CASH_AFTER_DEBT"), ReserveChart = Chart(a, "RESERVE_CLOSING"), HomesChart = Chart(a, "HOMES_TOTAL"),
            IncomeDebtChart = IncomeDebtChart(a),
            Properties = properties, BuildingMetrics = a.Metrics.Where(m => m.Scope != scope && m.Year == year),
            PortfolioSnapshot = new { Buildings = properties.Length, Operating = properties.Count(r => r.Text("Operating_Status").StartsWith("Operating")),
                PlannedBuildings = properties.Count(r => r.Text("Operating_Status").StartsWith("Planned")), ExistingHomes = Format(existing), PlannedHomes = Format(planned),
                ExistingMetricId = existing?.Id, PlannedMetricId = planned?.Id },
            Peak = peak, Solutions = solutions, Sensitivities = sensitivities, Evidence = evidence,
            OptionAnalysis = V4Analysis.Options(a), Capacity = V4Analysis.Capacity(a),
            ReserveContinuity = V4Analysis.ReserveContinuity(a), Maturities = V4Analysis.Maturities(a),
            Registers = a.Registers.Where(r => new[] { "10_SOURCE_REGISTER", "12_SOURCE_NOTES", "13_WATCH_DATES", "20_FIELD_MAPPING", "31_PROPERTY_VALUE", "33_DEBT", "35_AGREEMENTS", "36_CAPITAL_NEEDS", "37_UNIT_RENT", "38_ASSET_CONDITION", "39_MISSION_SERVICES", "39_GOVERNANCE", "45_NARRATIVE", "46_RISKS", "47_OPTIONS", "48_ACTIONS", "49_DECISIONS", "51_EXCEPTIONS", "54_INFO_REQUESTS", "57_OPTION_ANALYSIS", "58_CAPACITY_TESTS" }.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value),
            OpenQuestions = open, a.ImportFindings, a.Baseline, a.Outcomes, a.Events, Methods = methods,
            MaturityState = "Waiting on reviewed instrument dates, an explicit as-of date and balances. Payment aggregates are not distinct loans.",
            OutcomeState = a.Outcomes.Count == 0 ? "Not yet measured" : "Evidence observations; contribution only",
            Reports = new[] { new { Title = "Board summary", Audience = "Board / CEO / CFO", State = "Draft for review" },
                new { Title = "Management action list", Audience = "CEO and management", State = "Proposed; not approved" },
                new { Title = "Funder submission", Audience = "Authorized funder", State = "Incomplete evidence" },
                new { Title = "Lender package", Audience = "Authorized lender", State = "Incomplete evidence" } }
        };
    }

    private static V4Row[] SectionRecords(V4Assessment a, V4Row section, string page)
    {
        var source = section.Text("Data_Source");
        var sheet = source.Split(' ', ';')[0];
        var rows = a.Rows(sheet);
        if (sheet == "45_NARRATIVE") rows = rows.Where(r => r.Text("Section_ID") == section.Id);
        if (sheet is "45_NARRATIVE" or "46_RISKS" or "47_OPTIONS") rows = rows.Where(r =>
            r.Text("Review_Status") == "Approved" || (a.Setting("SHOW_DRAFT_NARRATIVE") == "Yes" && r.Text("Review_Status") == "Draft"));
        if (sheet is "46_RISKS" or "47_OPTIONS") rows = rows.Where(r => r.Text("Show_On_Pages").Split(';', ',', ' ').Contains(page));
        if (sheet == "51_EXCEPTIONS" && section.Id == "P4_QUESTIONS") rows = rows.Where(r => r.Text("Status") == "Open" && r.Text("Show_On_Evidence_Page") == "Yes");
        return rows.ToArray();
    }
    private static object Evidence(V4Assessment a, V4Row row)
    {
        var codes = row.Id switch { "EVID-FIN" => new[] { "REVENUE", "OPEX" }, "EVID-DEBT" => ["DEBT_SERVICE"], "EVID-RESERVE" => ["RESERVE_CLOSING"], _ => [] };
        if (codes.Length == 0) return new { Id = row.Id, Name = row.Text("Area_Name"), State = row.Text("Evidence_Status"), Summary = row.Text("Summary_Text"), Needs = row.Text("Needs_Text") };
        var years = V4CalculationEngine.Years(a).ToArray(); var scope = a.Setting("PORTFOLIO_PROPERTY_ID");
        var complete = years.Count(y => codes.All(c => a.Metric(scope, y.Year, c)?.Value.HasValue == true));
        var any = years.Any(y => codes.Any(c => a.Metric(scope, y.Year, c)?.Value.HasValue == true));
        return new { Id = row.Id, Name = row.Text("Area_Name"), State = complete == years.Length && complete > 0 ? "Confirmed" : any ? "Partly confirmed" : "No data available",
            Summary = $"Usable figures in {complete} of {years.Length} configured periods. Completeness is separate from review and release.", Needs = row.Text("Needs_Text") };
    }
    public static object Sensitivity(V4Assessment a, V4Row row)
    {
        var year = row.Text("Apply_to_Fiscal_Year"); if (year.Length == 0 && row.Formulas.ContainsKey("Apply_to_Fiscal_Year")) year = a.Setting("FULL_OPERATION_FISCAL_YEAR");
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID"); decimal? V(string c) => a.Metric(scope, year, c)?.Value;
        var revenue = V("REVENUE") * (1 + row.Number("Revenue_Change")) - V("REV_SUBSIDY") * row.Number("Subsidy_Loss");
        var opex = V("OPEX") * (1 + row.Number("OPEX_Change")); var debt = V("DEBT_SERVICE") * (1 + row.Number("Debt_Service_Change"));
        var noi = revenue - opex; var target = row.Number("Target_DSCR") ?? WorkbookValue.Decimal(a.Setting("TARGET_COVERAGE"));
        var cash = noi - debt - row.Number("Additional_Cash_Need");
        return new { row.Id, Name = row.Text("Scenario_Name"), Year = year, Revenue = revenue, Opex = opex, DebtService = debt, Noi = noi,
            Dscr = debt > 0 ? noi / debt : null, RequiredNoi = debt * target, CoverageGap = debt * target - noi,
            CashAfterDebt = cash, CashShortfall = cash.HasValue ? Math.Max(0m, -cash.Value) : (decimal?)null, ReviewStatus = row.Text("Review_Status"), Method = "55/56 V4.2 registered stress/1" };
    }
    public static string Format(V4Metric? m)
    {
        if (m?.Value is not decimal value) return m?.ValueState == "Not Applicable" ? "Not applicable" : "No data available";
        if (m.Code.Contains("DSCR")) return value.ToString(value is >= 0.99m and < 1m ? "0.000" : "0.00", CultureInfo.InvariantCulture) + "x";
        if (m.Code == "HOMES_TOTAL") return value.ToString("N0", CultureInfo.InvariantCulture);
        var signedCash = m.Code is "CASH_AFTER_DEBT" or "CASH_AFTER_RESERVE" or "CASH_EX_PLACEHOLDER";
        return value == 0 ? "$0" : (value < 0 ? "-" : signedCash ? "+" : "") + "$" + (Math.Abs(value) / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "M";
    }
    private static object Chart(V4Assessment a, string code)
    {
        var points = V4CalculationEngine.Years(a).Select(y => a.Metric(a.Setting("PORTFOLIO_PROPERTY_ID"), y.Year, code)).Where(m => m is not null).Cast<V4Metric>().ToArray();
        var max = Math.Max(1m, points.Select(m => Math.Abs(m.Value ?? 0m)).DefaultIfEmpty(0m).Max());
        var scale = code == "CASH_AFTER_DEBT" ? 110m : 130m;
        return points.Select(m => {
            var height = Math.Abs(m.Value ?? 0m) / max * scale;
            var negative = m.Value < 0;
            return new { m.Id, m.Year, m.Value, m.DisplayState,
                Label = m.Value is not decimal value ? "No data" : code == "HOMES_TOTAL" ? value.ToString("N0", CultureInfo.InvariantCulture) :
                    value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture).Replace("+", code == "CASH_AFTER_DEBT" ? "+" : ""),
                Height = height, Top = negative ? 130m : 130m - height,
                LabelTop = negative ? 130m + height + 3m : Math.Max(0m, 130m - height - 20m), Negative = negative };
        }).ToArray();
    }
    private static object IncomeDebtChart(V4Assessment a)
    {
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID");
        var periods = V4CalculationEngine.Years(a).Select(y => y.Year).ToArray();
        var max = Math.Max(1m, a.Metrics.Where(m => m.Scope == scope && periods.Contains(m.Year) && m.Code is "NOI" or "DEBT_SERVICE")
            .Select(m => Math.Abs(m.Value ?? 0m)).DefaultIfEmpty(0m).Max());
        object Point(V4Metric? m) { var height = Math.Abs(m?.Value ?? 0m) / max * 110m;
            return new { Id = m?.Id, m?.Value, Height = height, Top = 130m - height,
                Label = m?.Value is decimal v ? (v / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) : "—" }; }
        return periods.Select(year => new { Year = year, Noi = Point(a.Metric(scope, year, "NOI")), Debt = Point(a.Metric(scope, year, "DEBT_SERVICE")) }).ToArray();
    }
}
