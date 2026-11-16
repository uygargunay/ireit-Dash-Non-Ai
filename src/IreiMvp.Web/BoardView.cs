using System.Globalization;

namespace IreiMvp.Web;

public sealed record BoardFinding(string Id, string Title, string Meaning, string Tone, string MetricId, string EvidenceNeeded);
public sealed record BoardProposal(string Id, string Action, string Size, string Owner, string Timing, string Basis);
public static class BoardView
{
    public static object Build(BoardRecord record)
    {
        var a = record.Assessment; var scope = a.Setting("PORTFOLIO_PROPERTY_ID"); var year = a.Setting("CURRENT_FISCAL_YEAR");
        var full = a.Setting("FULL_OPERATION_FISCAL_YEAR"); var allYears = V4CalculationEngine.Years(a).Select(y => y.Year).Order().ToArray();
        var forward = allYears.Where(y => y.CompareTo(year) >= 0).ToArray();
        V4Metric? M(string c, string? y = null) => a.Metric(scope, y ?? year, c);
        string F(string c, string? y = null) => V4ViewService.Format(M(c, y));
        var properties = a.Rows("31_PROPERTY").Where(r => r.Text("Organization_ID") == a.OrganizationId && r.Text("Include_in_Assessment") == "Yes").DistinctBy(r => r.Id).ToArray();
        var exceptions = a.Rows("51_EXCEPTIONS").Where(r => r.Text("Assessment_ID") == a.AssessmentId).ToArray();
        var open = exceptions.Where(r => r.Text("Status") == "Open" && r.Text("Show_On_Evidence_Page") == "Yes").ToArray();
        var peak = allYears.Select(y => M("DEBT_SERVICE", y)).Where(m => m?.Value.HasValue == true).OrderByDescending(m => m!.Value).FirstOrDefault();
        var shortfallYears = forward.Where(y => M("DSCR", y)?.Value < 1).ToArray();
        var firstHomes = allYears.Select(y => M("HOMES_TOTAL", y)).FirstOrDefault(m => m?.Value.HasValue == true);
        var plateau = allYears.Where(y => M("HOMES_TOTAL", y)?.Value == M("HOMES_TOTAL", allYears.LastOrDefault() ?? "")?.Value && M("HOMES_TOTAL", y)?.Value.HasValue == true).FirstOrDefault();
        var plannedIds = properties.Where(p => p.Text("Operating_Status").StartsWith("Planned", StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToHashSet();
        var newPayments = a.Facts.Where(f => plannedIds.Contains(f.Scope) && f.Year == full && f.Code == "DEBT_SERVICE").ToArray();
        decimal? newPaymentTotal = newPayments.Length > 0 && newPayments.All(f => f.Value.HasValue) ? newPayments.Sum(f => f.Value) : null;
        // Unreviewed literal source payment aggregates can be displayed as observations,
        // without admitting them as verified debt facts or calculating building coverage.
        var sourcePayments = a.Rows("33_DEBT_SCHEDULE").Where(r => plannedIds.Contains(r.Text("Property_ID")) &&
            r.Text("Fiscal_Year") == full && r.Text("Currency") == a.Currency &&
            r.Text("Scenario") == V4CalculationEngine.Years(a).FirstOrDefault(y => y.Year == full).Scenario).ToArray();
        if (newPaymentTotal is null && plannedIds.Count > 0 && sourcePayments.Length == plannedIds.Count &&
            sourcePayments.Select(r => r.Text("Property_ID")).Distinct().Count() == plannedIds.Count &&
            sourcePayments.All(r => !r.Formulas.ContainsKey("Total_Debt_Service") && r.Number("Total_Debt_Service") >= 0))
            newPaymentTotal = sourcePayments.Sum(r => r.Number("Total_Debt_Service"));
        var findings = new List<BoardFinding>();
        void Finding(string id, string title, string meaning, string tone, string metric, string needed) => findings.Add(new(id, title, meaning, tone, metric, needed));
        if (M("CASH_AFTER_DEBT")?.Value is decimal cash) Finding("CURRENT-CASH", cash >= 0 ? "Current income exceeds loan payments" : "Current income does not cover loan payments",
            $"{year}: {F("CASH_AFTER_DEBT")} cash after loans; {F("DSCR")} coverage. These are source-plan figures, not unrestricted cash or confirmed actual results.", cash >= 0 ? "green" : "red", M("CASH_AFTER_DEBT")!.Id, "Current actual financial statements and bank reconciliation");
        foreach (var y in shortfallYears) Finding("COVERAGE-" + y, "Future coverage falls below break-even", $"{y}: {F("DSCR", y)} coverage and {F("CASH_SHORTFALL", y)} annual cash shortfall.", "red", M("DSCR", y)!.Id, "Updated income, subsidy commitments and lender payment schedules");
        if (M("PLACEHOLDER_SUBSIDY", full)?.Value > 0) Finding("SUBSIDY", "The plan relies on placeholder subsidy",
            $"{full}: {F("PLACEHOLDER_SUBSIDY", full)}/year is labeled placeholder in the source. Removing it produces {F("SHORTFALL_EX_PLACEHOLDER", full)} shortfall and {F("DSCR_EX_PLACEHOLDER", full)} coverage.", "red", M("SHORTFALL_EX_PLACEHOLDER", full)!.Id, "Written subsidy commitments: amount, duration and conditions");
        Finding("RENEWAL", "Current mortgage terms are not established", "Payment aggregates and planning selectors do not establish actual loan maturities. Obtain current lender statements before relying on renewal timing.", "amber", "", "Current balances, terms and renewal outcomes");
        var reserveBreaks = record.Checks.Where(c => c.Area == "Reserve opening continuity" && c.State == "Review required").ToArray();
        if (reserveBreaks.Length > 0) Finding("RESERVE-RESET", "Opening cash does not carry forward", $"{reserveBreaks[0].Period}: unexplained reserve reset of {Money(reserveBreaks[0].Difference)}. This is separate from annual operating cash flow.", "amber", "", "Bank reconciliation and transfers explaining the reset");
        var proposals = new List<BoardProposal> { new("P-TERMS", "Confirm near-term mortgages", "Terms not confirmed", "CFO / lender", "First evidence request", "RENEWAL") };
        if (M("PLACEHOLDER_SUBSIDY", full)?.Value > 0) proposals.Add(new("P-SUBSIDY", "Secure project subsidy", F("PLACEHOLDER_SUBSIDY", full) + "/year", "CEO", "Before relying on FY" + full[^2..], "SUBSIDY"));
        if (M("CASH_SHORTFALL", full)?.Value > 0) proposals.Add(new("P-RESIDUAL", "Close residual annual cash gap", Annual(M("CASH_SHORTFALL", full)?.Value), "CFO / management", "FY" + full[^2..] + " source plan", "COVERAGE-" + full));
        proposals.Add(new("P-RULES", "Set mission and reserve rules", "Limits unconfirmed", "Board", "Before financing approval", "RESERVE-PERMISSION"));
        if (M("SHORTFALL_EX_PLACEHOLDER", full)?.Value > 0) proposals.Add(new("P-FALLBACK", "Test no-subsidy fallback", F("SHORTFALL_EX_PLACEHOLDER", full) + " cash need", "CFO / board", "Annual FY" + full[^2..] + " sensitivity", "SUBSIDY"));
        var additional = open.Select(r => new BoardProposal(r.Id,
            r.Text("Exception_Type").Length > 0 ? "Review: " + r.Text("Exception_Type") : r.Text("Issue_Type").Length > 0 ? r.Text("Issue_Type") : r.Text("Plain_Question"),
            "Evidence required", r.Text("Who_Answers").Length > 0 ? r.Text("Who_Answers") : r.Text("Suggested_Respondent").Length > 0 ? r.Text("Suggested_Respondent") : "CFO / management", "Before related commitments", r.Id)).ToArray();
        var missing = new[] {
            new { Area = "Affordability", Sheet = "37_UNIT_RENT", Required = "Actual rent rolls and household affordability evidence", Unlocks = "Rent levels, affordability and protected homes" },
            new { Area = "Loan terms", Sheet = "33_DEBT", Required = "Current lender statements and renewal outcomes", Unlocks = "Current balances, rates, maturities and refinancing terms" },
            new { Area = "Resident outcomes", Sheet = "39_MISSION_SERVICES", Required = "Resident/service records and occupied homes", Unlocks = "People housed and demonstrated mission outcomes" },
            new { Area = "Governance", Sheet = "39_GOVERNANCE", Required = "Reserve policy, bylaws and approval authorities", Unlocks = "Permitted use, minimum cash floor and decision authority" },
            new { Area = "Current condition", Sheet = "38_ASSET_CONDITION", Required = "Current condition reports and capital plan", Unlocks = "Repair priorities and investment needs" }
        }.Select(x => new { x.Area, x.Required, x.Unlocks, Count = a.Rows(x.Sheet).Count(), State =
            x.Sheet == "33_DEBT" ? a.Rows(x.Sheet).Any(r => r.Text("Maturity_Date").Length > 0) ? "Terms require review" : "Not established" : a.Rows(x.Sheet).Any() ? "Records require review" : "No data available" }).ToArray();
        var options = new List<object>();
        if (M("PLACEHOLDER_SUBSIDY", full)?.Value > 0) options.Add(new { Name = "Confirm subsidy + residual relief", Amount = F("PLACEHOLDER_SUBSIDY", full) + "/year + " + Annual(M("CASH_SHORTFALL", full)?.Value),
            Needs = "Written commitments and viable residual measures", Protects = "Closes source-plan cash shortfall if delivered", Risks = "Funding not secured; mission effects require review" });
        if (M("SHORTFALL_EX_PLACEHOLDER", full)?.Value.HasValue == true)
        {
            options.Add(new { Name = "Recurring replacement / NOI improvement", Amount = F("SHORTFALL_EX_PLACEHOLDER", full) + "/year net", Needs = "Verified recurring income/support or lower costs", Protects = "Closes no-subsidy cash shortfall", Risks = "Rents, services or maintenance may be affected" });
            options.Add(new { Name = "One-year cash bridge", Amount = F("SHORTFALL_EX_PLACEHOLDER", full) + " for " + full, Needs = "Permitted reserves/grant/capital and minimum cash floor", Protects = "Funds one year; coverage stays " + F("DSCR_EX_PLACEHOLDER", full), Risks = "Depletes liquidity; the gap returns" });
            options.Add(new { Name = "Renew / restructure payments", Amount = F("SHORTFALL_EX_PLACEHOLDER", full) + "/year relief to test", Needs = "Current lender terms and priced scenarios", Protects = "Could close the annual gap if feasible", Risks = "Longer term, interest, covenants and security" });
        }
        options.Add(new { Name = "Target coverage cushion", Amount = F("COVERAGE_GAP", full) + " base / " + F("COVERAGE_GAP_EX_PLACEHOLDER", full) + " no subsidy", Needs = "Separate recurring-NOI test", Protects = "Raises coverage if delivered", Risks = "Larger change; mission and feasibility review" });
        options.Add(new { Name = "Phase / delay development", Amount = "Not calculated", Needs = "Dated delay and cash-flow scenario", Protects = "May reduce timing exposure", Risks = "Fewer/later homes, lost income or higher costs" });
        options.Add(new { Name = "Do nothing", Amount = "$0 mitigation", Needs = "No prerequisite", Protects = "No current intervention", Risks = "Future shortfalls and unresolved terms remain" });
        return new {
            View = V4ViewService.Page(a, "P3"), Findings = findings,
            ExecutiveFindings = findings.Select(f => f with { Meaning = f.Id switch {
                "CURRENT-CASH" => $"FY{year[^2..]}: {F("CASH_AFTER_DEBT")} cash after loans; {F("DSCR")} coverage.",
                "SUBSIDY" => $"Without placeholder subsidy: FY{full[^2..]} cash gap {F("SHORTFALL_EX_PLACEHOLDER", full)}; coverage {F("DSCR_EX_PLACEHOLDER", full)}.",
                "RENEWAL" => "Current mortgage terms and renewal outcomes are unconfirmed. Obtain lender statements first.",
                _ => f.Id.StartsWith("COVERAGE-") ? f.Meaning.Replace(f.Id[9..], "FY" + f.Id[^2..]) : f.Meaning } }),
            Proposals = proposals, ProposedTracker = proposals.Concat(additional),
            Missing = missing, Options = options, Checks = record.Checks,
            Sources = record.Sources.Select(s => new { s.Name, s.Sha256, s.Kind }),
            Summary = new { CurrentYear = year, FullYear = full, Buildings = properties.Length,
                Operating = properties.Count(p => !p.Text("Operating_Status").StartsWith("Planned", StringComparison.OrdinalIgnoreCase)), Planned = plannedIds.Count,
                ExistingHomes = firstHomes?.Value, PlannedHomes = M("HOMES_TOTAL", allYears.LastOrDefault())?.Value, PlannedBy = plateau,
                Open = open.Length, Resolved = exceptions.Count(r => r.Text("Status") == "Resolved"), Accepted = exceptions.Count(r => r.Text("Status") == "Exception accepted"),
                ExceptionTotal = exceptions.Length, ForwardPeriods = forward.Length, BelowOne = shortfallYears.Length, Peak = peak,
                NewPayments = newPaymentTotal, NewPaymentsState = "Source annual payment aggregates; not verified instruments", RentRollState = a.Rows("37_UNIT_RENT").Any() ? "Review records" : "Not received",
                SourceDocuments = a.Rows("10_SOURCE_REGISTER").Count(), UploadedFiles = record.Sources.Count,
                ReserveBreaks = reserveBreaks.Length, CheckPass = record.Checks.Count(c => c.State == "Pass"), CheckReview = record.Checks.Count(c => c.State == "Review required"), CheckMissing = record.Checks.Count(c => c.State == "Not testable"),
                Cities = properties.Select(p => p.Text("City")).Where(s => s.Length > 0).Distinct(),
                HistoricalDetailState = "Only values present in the uploaded records are shown. Historical values in reference images are not input data.",
                DisplayYears = allYears.Where(y => M("DEBT_SERVICE", y)?.Value.HasValue == true).ToArray() },
            Formatted = new { ResidualAnnual = Annual(M("CASH_SHORTFALL", full)?.Value), PlaceholderAnnual = F("PLACEHOLDER_SUBSIDY", full),
                FallbackAnnual = F("SHORTFALL_EX_PLACEHOLDER", full), NewPayments = Money(newPaymentTotal), Target = WorkbookValue.Decimal(a.Setting("TARGET_COVERAGE"))?.ToString("0.00", CultureInfo.InvariantCulture) + "x" }
        };
    }
    public static string Money(decimal? value) => value is null ? "No data available" : value == 0 ? "$0" : (value < 0 ? "-" : "") + "$" + (Math.Abs(value.Value) / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "M";
    private static string Annual(decimal? value) => value is > 0 and < 1_000_000m ? "$" + (value.Value / 1000m).ToString("0", CultureInfo.InvariantCulture) + "K/year" : Money(value) + (value.HasValue ? "/year" : "");
}
