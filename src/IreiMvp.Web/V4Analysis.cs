namespace IreiMvp.Web;

public static class V4Analysis
{
    // Terminal diagnostics never supply financial inputs or infer a permitted reserve.
    public static object[] ReserveContinuity(V4Assessment a) => a.Rows("11_RAW_STAGING")
        .Where(r => r.Text("Assessment_ID") == a.AssessmentId && r.Text("Raw_Label").StartsWith("Opening reserve", StringComparison.OrdinalIgnoreCase))
        .Select(r =>
        {
            var year = r.Text("Context_Text");
            var previous = V4CalculationEngine.Years(a).Select(y => y.Year).Order().TakeWhile(y => y != year).LastOrDefault();
            var close = previous is null ? null : a.Metric(a.Setting("PORTFOLIO_PROPERTY_ID"), previous, "RESERVE_CLOSING");
            var opening = r.Number("Raw_Value_Number");
            return (object)new { r.Id, Year = year, OpeningStaged = opening, PreviousClose = close?.Value,
                Difference = opening - close?.Value, SourceLocation = r.Text("Source_Location"), PriorMetricId = close?.Id,
                State = opening.HasValue && close?.Value.HasValue == true && Math.Abs(opening.Value - close.Value.Value) > .01m ? "Reconciliation required" : "Opening evidence requires review" };
        }).ToArray();

    public static object Maturities(V4Assessment a)
    {
        var asOf = WorkbookValue.Date(a.Setting("REPORTING_AS_OF_DATE"));
        var candidates = a.Rows("33_DEBT").Where(r => r.Text("Assessment_ID") == a.AssessmentId && r.Text("Maturity_Date").Length > 0)
            .GroupBy(r => r.Text("Debt_ID")).Select(g =>
            {
                var dates = g.Select(r => WorkbookValue.Date(r.Text("Maturity_Date"))).Where(d => d.HasValue).Distinct().ToArray();
                var conflict = dates.Length != 1;
                var date = conflict ? null : dates[0];
                var period = date is null ? "" : V4CalculationEngine.Years(a).FirstOrDefault(y =>
                    date >= new DateTimeOffset(int.Parse(y.Year[..4]), 4, 1, 0, 0, 0, TimeSpan.Zero) &&
                    date < new DateTimeOffset(int.Parse(y.Year[5..]), 4, 1, 0, 0, 0, TimeSpan.Zero)).Year;
                var reviewed = g.All(r => r.Text("Review_Status") is "Verified" or "Confirmed");
                return new { DebtId = g.Key, Dates = dates, Year = period, Conflict = conflict, Reviewed = reviewed,
                    DateAlreadyPassed = reviewed && !conflict && asOf.HasValue && date < asOf,
                    State = conflict ? "To confirm with the organization" : "Renewal outcome unconfirmed",
                    MaturityBalance = (decimal?)null, CurrentBalanceProxy = g.Count() == 1 ? g.First().Number("Outstanding_Principal") : null,
                    Context = string.Join("; ", g.Select(r => r.Text("Notes"))), Sources = g.Select(r => r.Text("Source_Location")).ToArray() };
            }).ToArray();
        return new { AsOf = asOf, Candidates = candidates,
            State = candidates.Length == 0 || !asOf.HasValue ? "Waiting on reviewed instrument dates and an explicit reporting as-of date" : "Historical terms; renewal outcomes require evidence" };
    }

    public static object[] Options(V4Assessment a) => a.Rows("57_OPTION_ANALYSIS").Select(r =>
    {
        var year = r.Text("Fiscal_Year"); if (year.Length == 0 && r.Formulas.ContainsKey("Fiscal_Year")) year = a.Setting("CURRENT_FISCAL_YEAR");
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID");
        var subsidy = r.Id == "OA-SUBSIDY";
        var amount = subsidy ? a.Metric(scope, year, "PLACEHOLDER_SUBSIDY")?.Value : r.Number("Cash_Available_or_Annual_Benefit");
        var cash = a.Metric(scope, year, subsidy ? "CASH_EX_PLACEHOLDER" : "CASH_AFTER_DEBT")?.Value;
        var need = cash.HasValue ? Math.Max(0m, -cash.Value) : (decimal?)null;
        return (object)new { r.Id, Name = r.Text("Option_Name"), Year = year, Amount = amount,
            CashShortfall = need, CashShortfallFunded = amount.HasValue && need.HasValue ? Math.Min(need.Value, Math.Max(0m, amount.Value)) : (decimal?)null,
            ResidualCashShortfall = amount.HasValue && need.HasValue ? Math.Max(0m, need.Value - Math.Max(0m, amount.Value)) : (decimal?)null,
            NeedsFirst = r.Text("Needs_First"), Risks = r.Text("What_It_Risks"), r.Fields, ReviewStatus = r.Text("Review_Status"), Method = "57 V4.2 cash-shortfall/1" };
    }).ToArray();

    public static object Capacity(V4Assessment a)
    {
        // Mixed-layout worksheet: assumptions have names; calculated rows are method output,
        // not assumptions. The method is implemented here, never loaded from formula caches.
        var assumptions = a.Rows("58_CAPACITY_TESTS");
        string Text(string name) => assumptions.FirstOrDefault(r => r.Text("Editable assumption") == name)?.Text("Value") ?? "";
        decimal? Number(string name) => WorkbookValue.Decimal(Text(name));
        var ltv = Number("Maximum LTV"); var constant = Number("Annual debt constant");
        var year = Text("Capacity fiscal year"); var floor = Number("Minimum reserve floor");
        var values = a.Rows("31_PROPERTY_VALUE").Where(r => r.Text("Assessment_ID") == a.AssessmentId && r.Text("Include_in_Capacity_Test") == "Yes").ToArray();
        decimal? security = values.Length > 0 && ltv.HasValue && values.All(r => r.Number("Value_Amount").HasValue && r.Number("Existing_Secured_Debt").HasValue)
            ? values.Sum(r => r.Number("Value_Amount") * ltv - r.Number("Existing_Secured_Debt")) : null;
        var scope = a.Setting("PORTFOLIO_PROPERTY_ID"); var noi = a.Metric(scope, year, "NOI")?.Value;
        var debt = a.Metric(scope, year, "DEBT_SERVICE")?.Value; var reserve = a.Metric(scope, year, "RESERVE_CLOSING")?.Value;
        var target = WorkbookValue.Decimal(a.Setting("TARGET_COVERAGE"));
        decimal? repay = constant > 0 && target > 0 && noi.HasValue && debt.HasValue ? Math.Max(0m, (noi.Value / target.Value - debt.Value) / constant.Value) : null;
        decimal? liquidity = floor.HasValue && reserve.HasValue ? Math.Max(0m, reserve.Value - floor.Value) : null;
        decimal? permission = Text("Permission and control") switch { "Permitted" => 1m, "Restricted" => 0m, _ => null };
        decimal? safe = security.HasValue && repay.HasValue && liquidity.HasValue && permission.HasValue
            ? permission == 0 ? 0m : Math.Min(security.Value, Math.Min(repay.Value, liquidity.Value)) : null;
        return new { Year = year, PropertySecurity = security, AbilityToRepay = repay, Permission = permission,
            Liquidity = liquidity, PreliminarySafeBorrowing = safe, ReviewStatus = "Draft screening assumptions",
            ValueState = safe.HasValue ? "Available" : "Missing", Method = "58 V4.2 capacity/1",
            Note = "All four tests are required. Closing reserves are not automatically permitted cash. A preliminary screen is not a financing approval." };
    }
}
