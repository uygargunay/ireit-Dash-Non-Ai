namespace IreiMvp.Web;

public static class V4CalculationEngine
{
    public static void Calculate(V4Assessment a)
    {
        a.Metrics.Clear();
        var portfolio = a.Setting("PORTFOLIO_PROPERTY_ID");
        var target = WorkbookValue.Decimal(a.Setting("TARGET_COVERAGE"));
        foreach (var scope in a.Facts.Select(f => f.Scope).Distinct())
        foreach (var (year, scenario) in Years(a))
        {
            var facts = a.Facts.Where(f => f.Scope == scope && f.Year == year && f.Scenario == scenario &&
                f.Currency == a.Currency && (scope == portfolio ? f.Grain == "Portfolio control" : f.Grain is "Property detail" or "Project detail")).ToList();
            var inputs = new Dictionary<string, V4Fact[]>();
            decimal? Sum(string code, bool single = false)
            {
                var rows = facts.Where(f => f.Code == code).ToArray(); inputs[code] = rows;
                if (rows.Length == 0 || rows.Any(f => f.ValueState != "Available" || f.Value is null) ||
                    rows.Select(f => f.Id).Distinct().Count() != rows.Length ||
                    rows.GroupBy(f => (f.SourceId, f.SourceLocation)).Any(g => g.Count() > 1) || (single && rows.Length != 1)) return null;
                return rows.Sum(f => f.Value!.Value);
            }
            void Add(string code, decimal? value, string calc, string[] dependencies, string? state = null)
            {
                var basis = dependencies.SelectMany(d => inputs.GetValueOrDefault(d) ?? []).DistinctBy(f => f.Id).ToArray();
                inputs[code] = basis;
                var registry = a.Rows("40_CALC_REGISTRY").FirstOrDefault(r => r.Text("Calc_ID") == calc);
                var conflict = basis.Any(f => f.ValueState == "Conflict") ||
                    dependencies.Any(d => inputs.GetValueOrDefault(d)?.GroupBy(f => f.Id).Any(g => g.Count() > 1) == true) ||
                    dependencies.Any(d => inputs.GetValueOrDefault(d)?.GroupBy(f => (f.SourceId, f.SourceLocation)).Any(g => g.Count() > 1) == true);
                var valueState = value.HasValue ? "Available" : state ?? (conflict ? "Conflict" : "Missing");
                var reviewed = basis.Length > 0 && basis.All(f => f.ReviewStatus is "Verified" or "Confirmed" or "Exception accepted" && f.MappingStatus is "Confirmed" or "Unmapped");
                var display = value.HasValue ? basis.Any(f => f.Certainty == "Placeholder") ? "Placeholder" :
                    scenario == "Actual" && reviewed ? "Confirmed" : "Projected" : valueState == "Not Applicable" ? "Not applicable" : "No data available";
                var severity = code == "DSCR" && value.HasValue && target.HasValue ? Severity(value.Value, target.Value) : "None";
                a.Metrics.Add(new V4Metric($"MET-{scope}-{year}-{code}", scope, year, scenario, code, value, valueState,
                    display, reviewed ? "Reviewed" : "Under review", calc, registry?.Text("Calc_Version") ?? "1.0",
                    a.MethodologyVersion, basis.Select(f => f.Id).ToArray(), severity));
            }
            var revenue = Sum("REVENUE"); var opex = Sum("OPEX");
            Add("REVENUE", revenue, "CALC-REVENUE", ["REVENUE"]); Add("OPEX", opex, "CALC-OPEX", ["OPEX"]);
            var noi = revenue - opex; Add("NOI", noi, "CALC-NOI", ["REVENUE", "OPEX"]);
            var debt = Sum("DEBT_SERVICE", true);
            if (debt < 0) debt = null;
            Add("DEBT_SERVICE", debt, "CALC-DEBT-SVC", ["DEBT_SERVICE"], facts.Count(f => f.Code == "DEBT_SERVICE") > 1 ? "Conflict" : null);
            var dscr = debt > 0 ? noi / debt : null;
            Add("DSCR", dscr, "CALC-DSCR", ["NOI", "DEBT_SERVICE"], debt == 0 ? "Not Applicable" : null);
            var cash = noi - debt; Add("CASH_AFTER_DEBT", cash, "CALC-CASH-AFTER-DEBT", ["NOI", "DEBT_SERVICE"]);
            Add("CASH_SHORTFALL", cash.HasValue ? Math.Max(0m, -cash.Value) : null, "CALC-CASH-SHORTFALL/BR-V4-059", ["CASH_AFTER_DEBT"]);
            Add("REQUIRED_NOI", debt * target, "CALC-REQ-NOI", ["DEBT_SERVICE"]);
            Add("COVERAGE_GAP", debt * target - noi, "CALC-COVERAGE-GAP", ["NOI", "DEBT_SERVICE"]);
            var contribution = Sum("RESERVE_CONTRIBUTION");
            Add("RESERVE_CONTRIBUTION", contribution, "CALC-REPAIR", ["RESERVE_CONTRIBUTION"]);
            Add("CASH_AFTER_RESERVE", cash - contribution, "CALC-CASH-AFTER-RES", ["CASH_AFTER_DEBT", "RESERVE_CONTRIBUTION"]);
            Add("RESERVE_CLOSING", Sum("RESERVE_CLOSING", true), "CALC-RESERVE", ["RESERVE_CLOSING"]);
            var homes = Sum("HOMES_TOTAL", true);
            if (homes < 0 || (homes.HasValue && decimal.Truncate(homes.Value) != homes)) homes = null;
            Add("HOMES_TOTAL", homes, "CALC-HOMES", ["HOMES_TOTAL"]);
            foreach (var code in new[] { "REV_RENT_EXISTING", "REV_RENT_NEW", "REV_SUBSIDY", "REV_OTHER", "OPEX_HEAD_OFFICE_STAFF" })
                Add(code, Sum(code), "CALC-REV-COMPONENT", [code]);
            var subsidy = a.Metric(scope, year, "REV_SUBSIDY")?.Value;
            Add("SUBSIDY_SHARE", revenue > 0 ? subsidy / revenue : null, "CALC-SUBSIDY-SHARE", ["REV_SUBSIDY", "REVENUE"]);
            Add("NOI_MARGIN", revenue > 0 ? noi / revenue : null, "CALC-NOI-MARGIN", ["NOI", "REVENUE"], revenue == 0 ? "Not Applicable" : null);
            Add("DEBT_BURDEN", revenue > 0 ? debt / revenue : null, "CALC-DEBT-BURDEN", ["DEBT_SERVICE", "REVENUE"], revenue == 0 ? "Not Applicable" : null);
            Add("REPAIR_SHARE_RENT", a.Metric(scope, year, "REV_RENT_EXISTING")?.Value > 0 ? contribution / a.Metric(scope, year, "REV_RENT_EXISTING")?.Value : null,
                "CALC-REPAIR", ["RESERVE_CONTRIBUTION", "REV_RENT_EXISTING"]);
            if (scope != portfolio) continue;
            var flagged = a.Facts.Where(f => f.Scope != portfolio && f.Year == year && f.Scenario == scenario &&
                f.Currency == a.Currency && f.Code == "REV_SUBSIDY" && f.Certainty == "Placeholder").ToArray();
            inputs["PLACEHOLDER_SUBSIDY"] = flagged;
            var unique = flagged.DistinctBy(f => (f.Scope, f.SourceId, f.SourceLocation)).ToArray();
            decimal? placeholder = unique.All(f => f.Value.HasValue && f.ValueState == "Available") ? unique.Sum(f => f.Value!.Value) : null;
            Add("PLACEHOLDER_SUBSIDY", placeholder, "CALC-PLACEHOLDER-SUBSIDY", ["PLACEHOLDER_SUBSIDY"]);
            var noiEx = noi - placeholder;
            Add("NOI_EX_PLACEHOLDER", noiEx, "CALC-NOI-EX-PLACEHOLDER", ["NOI", "PLACEHOLDER_SUBSIDY"]);
            Add("DSCR_EX_PLACEHOLDER", debt > 0 ? noiEx / debt : null, "CALC-DSCR-EX-PLACEHOLDER", ["NOI_EX_PLACEHOLDER", "DEBT_SERVICE"]);
            Add("CASH_EX_PLACEHOLDER", noiEx - debt, "CALC-CASH-EX-PLACEHOLDER", ["NOI_EX_PLACEHOLDER", "DEBT_SERVICE"]);
            Add("SHORTFALL_EX_PLACEHOLDER", noiEx.HasValue && debt.HasValue ? Math.Max(0m, debt.Value - noiEx.Value) : null,
                "CALC-CASH-SHORTFALL/BR-V4-059", ["NOI_EX_PLACEHOLDER", "DEBT_SERVICE"]);
            Add("COVERAGE_GAP_EX_PLACEHOLDER", debt * target - noiEx, "CALC-COVERAGE-GAP", ["NOI_EX_PLACEHOLDER", "DEBT_SERVICE"]);
        }
    }

    public static IEnumerable<(string Year, string Scenario)> Years(V4Assessment a) => a.Config
        .Where(p => p.Key.StartsWith("HORIZON_FY_", StringComparison.Ordinal) && p.Value.Length > 0).OrderBy(p => p.Key)
        .Select(p => (p.Value, a.Setting("HORIZON_SCENARIO_" + p.Key[11..])));
    public static string Severity(decimal ratio, decimal target) => ratio < 1m ? "High" : ratio < target ? "Medium" : "None";

    public static object TestOption(V4Assessment a, V4OptionRequest request)
    {
        if (!Years(a).Any(y => y.Year == request.Year) || request.Case is not ("Base" or "Without placeholder") ||
            request.Kind is not ("Bridge" or "Recurring income" or "Payment relief" or "Confirm subsidy") ||
            request.Amount < 0 || request.AdditionalDebtService < 0 || request.Amount > 1_000_000_000m || request.AdditionalDebtService > 1_000_000_000m)
            throw new InvalidDataException("Select a configured year/case, supported option, and bounded nonnegative amounts.");
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID");
        var without = request.Case == "Without placeholder";
        var noi = a.Metric(scope, request.Year, without ? "NOI_EX_PLACEHOLDER" : "NOI")?.Value;
        var debt = a.Metric(scope, request.Year, "DEBT_SERVICE")?.Value;
        var placeholder = a.Metric(scope, request.Year, "PLACEHOLDER_SUBSIDY")?.Value;
        var shortfall = noi.HasValue && debt.HasValue ? Math.Max(0m, debt.Value - noi.Value) : (decimal?)null;
        var benefit = request.Kind == "Confirm subsidy" ? (without && request.SubsidyConfirmed ? placeholder : 0m) : request.Amount;
        var afterNoi = request.Kind is "Recurring income" or "Confirm subsidy" ? noi + benefit : noi;
        var afterDebt = request.Kind == "Payment relief" ? debt - benefit + request.AdditionalDebtService : debt + request.AdditionalDebtService;
        if (afterDebt < 0) throw new InvalidDataException("Payment relief exceeds annual payments.");
        var cash = afterNoi - afterDebt + (request.Kind == "Bridge" ? request.Amount : 0m);
        var coverage = afterDebt > 0 ? afterNoi / afterDebt : null;
        return new { request.Year, request.Case, request.Kind, Amount = benefit, CashShortfall = shortfall,
            CashShortfallFunded = shortfall.HasValue && cash.HasValue ? Math.Min(shortfall.Value, Math.Max(0m, cash.Value + shortfall.Value)) : (decimal?)null,
            ResidualCashShortfall = cash.HasValue ? Math.Max(0m, -cash.Value) : (decimal?)null,
            CoverageAfter = coverage, NoiAfter = afterNoi, DebtServiceAfter = afterDebt,
            ValueState = noi.HasValue && debt.HasValue ? "Available" : "Missing", ReviewStatus = "Draft",
            Note = request.Kind == "Bridge" ? "One-year cash only; operating coverage is unchanged except for added debt service. Permitted use and repayment require review." :
                "Annual net benefit screening. Confirm subsidy restores excluded support once; it does not add support to the base plan." };
    }
}
