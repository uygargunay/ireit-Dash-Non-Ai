namespace IreiMvp.Web;

public sealed class DashboardMetricsFactory
{
    public DashboardMetrics Build(
        WorkbookSnapshot template,
        IReadOnlyCollection<CellPatch> patches,
        string organizationName,
        string profileName,
        IReadOnlyCollection<string> mappingWarnings,
        string? reportingPeriod = null)
    {
        var view = new WorkbookDataView(template, patches);
        var period = FirstNonBlank(reportingPeriod, view.Control("Reporting Fiscal Year"));
        var dashboard = new DashboardMetrics
        {
            OrganizationName = FirstNonBlank(
                organizationName,
                view.Control("Organization / Demonstration Name"),
                "Unnamed organization"),
            ReportingPeriod = period,
            ProfileName = profileName,
            Warnings = mappingWarnings
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        ReadProperties(view, dashboard);
        ReadUnitMix(view, dashboard);
        ReadFinancials(view, dashboard);
        ReadDebt(view, dashboard);
        ReadCashAndCapital(view, dashboard);
        ReadAgreements(view, dashboard);
        ReadFlags(view, dashboard);
        BuildAssessmentSummaries(view, dashboard);

        dashboard.HouseholdsServed = dashboard.ModeledUnits;
        dashboard.Affo = dashboard.Noi.HasValue && dashboard.DebtService.HasValue
            ? dashboard.Noi.Value - dashboard.DebtService.Value
            : null;
        dashboard.Dscr = dashboard.DebtService > 0 && dashboard.Noi.HasValue
            ? dashboard.Noi.Value / dashboard.DebtService.Value
            : null;

        dashboard.OverallStatus = DetermineOverallStatus(dashboard);
        dashboard.Warnings = dashboard.Warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return dashboard;
    }

    private static void ReadProperties(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        foreach (var row in view.Rows("DATA_PROPERTY_MASTER"))
        {
            var id = row.Text("Property_ID");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var modeledUnits = row.Int("Modeled Unit Count", "Total Units") ?? 0;
            var alternateUnits = row.Int("Alternate Unit Count");
            var formulaConflict = row.Bool("Unit Conflict?") == true;
            var unitConflict = formulaConflict ||
                               alternateUnits is > 0 && alternateUnits != modeledUnits;
            var included = row.Bool("Included?") ?? false;
            var status = row.Text("Status");
            var classification = row.Text("Final Classification");
            var confidence = FirstNonBlank(row.Text("Source Confidence"), "Not assessed");
            var dataStatus = row.Text("Data Status");
            if (unitConflict)
            {
                dataStatus = "Needs review";
            }
            else if (string.IsNullOrWhiteSpace(dataStatus))
            {
                dataStatus = !string.IsNullOrWhiteSpace(row.Text("Property_Name")) &&
                             !string.IsNullOrWhiteSpace(row.Text("City")) &&
                             !string.IsNullOrWhiteSpace(status) &&
                             !string.IsNullOrWhiteSpace(classification) &&
                             modeledUnits > 0
                    ? "Ready"
                    : "Partial";
            }

            var property = new PropertySummary
            {
                PropertyId = id,
                Name = FirstNonBlank(row.Text("Property_Name"), id),
                City = row.Text("City"),
                Status = FirstNonBlank(status, "Not assessed"),
                Included = included,
                Units = modeledUnits,
                AlternateUnits = alternateUnits,
                UnitConflict = unitConflict,
                Classification = FirstNonBlank(classification, "Not assessed"),
                SourceConfidence = confidence,
                DataStatus = dataStatus
            };

            property.DataGaps = CountPropertyGaps(property);
            property.RiskLevel = DeterminePropertyRisk(property);
            dashboard.Properties.Add(property);
        }

        dashboard.TotalProperties = dashboard.Properties.Count;
        dashboard.IncludedProperties = dashboard.Properties.Count(item => item.Included);
        dashboard.ExistingAssets = dashboard.Properties.Count(item =>
            item.Included && IsExisting(item.Status));
        dashboard.NewProjects = dashboard.Properties.Count(item =>
            item.Included && IsFuture(item.Status));
        dashboard.ModeledUnits = dashboard.Properties
            .Where(item => item.Included)
            .Sum(item => item.Units);

        if (dashboard.TotalProperties == 0)
        {
            dashboard.Warnings.Add("No property records were mapped from the uploaded workbook.");
        }
    }

    private static void ReadUnitMix(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        var includedIds = dashboard.Properties
            .Where(item => item.Included)
            .Select(item => item.PropertyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        decimal affordabilityWeighted = 0;
        var affordabilityWeight = 0;

        foreach (var row in view.Rows("DATA_UNIT_MIX"))
        {
            var propertyId = row.Text("Property_ID");
            if (!includedIds.Contains(propertyId))
            {
                continue;
            }

            var units = row.Int("Unit_Count") ?? 0;
            var affordable = row.Int("Affordable_Units") ?? 0;
            var market = row.Int("Market_Units") ?? 0;
            var supportive = row.Int("Supportive_Units") ?? 0;
            var unclassified = row.Int("Unclassified_Units") ??
                               Math.Max(0, units - affordable - market - supportive);

            dashboard.AffordableUnits += affordable;
            dashboard.MarketUnits += market;
            dashboard.SupportiveUnits += supportive;
            dashboard.UnclassifiedUnits += unclassified;

            var property = dashboard.Properties.FirstOrDefault(item =>
                string.Equals(item.PropertyId, propertyId, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
            {
                property.AffordableUnits += affordable;
                property.MarketUnits += market;
                property.SupportiveUnits += supportive;
            }

            var currentRent = row.Decimal("Avg_Current_Rent");
            var marketRent = row.Decimal("Avg_Market_Rent");
            if (units > 0 && currentRent is >= 0 && marketRent > 0)
            {
                affordabilityWeighted +=
                    Math.Max(0, (marketRent.Value - currentRent.Value) / marketRent.Value) * units;
                affordabilityWeight += units;
            }
        }

        dashboard.AverageAffordabilityDepth = affordabilityWeight > 0
            ? affordabilityWeighted / affordabilityWeight
            : null;

        if (dashboard.ModeledUnits > 0 &&
            dashboard.AffordableUnits + dashboard.MarketUnits +
            dashboard.SupportiveUnits + dashboard.UnclassifiedUnits == 0)
        {
            dashboard.UnclassifiedUnits = dashboard.ModeledUnits;
        }
    }

    private static void ReadFinancials(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        var rows = view.Rows("DATA_OPERATING_ACTUALS")
            .Where(row => !string.IsNullOrWhiteSpace(row.Text("Record_ID")))
            .ToList();
        var periods = rows
            .Select(row => row.Text("Fiscal_Year"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(FiscalSortKey)
            .ToList();
        var schedule = ReadDebtSchedule(view);

        foreach (var period in periods)
        {
            var periodRows = rows
                .Where(row => string.Equals(
                    row.Text("Fiscal_Year"),
                    period,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var portfolioRows = periodRows
                .Where(row => string.Equals(
                    row.Text("Property_ID"),
                    "PORTFOLIO",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var sourceRows = portfolioRows.Count > 0
                ? portfolioRows
                : periodRows.Where(row => IsIncludedProperty(dashboard, row.Text("Property_ID"))).ToList();
            var point = SummarizeFinancialRows(sourceRows, period);

            if (schedule.TryGetValue(period, out var debtService))
            {
                point.DebtService = debtService.Total;
            }

            point.Dscr = point.DebtService > 0
                ? point.Noi / point.DebtService
                : null;
            dashboard.FinancialTrend.Add(point);
        }

        var current = dashboard.FinancialTrend.FirstOrDefault(point =>
            string.Equals(
                point.Period,
                dashboard.ReportingPeriod,
                StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            current = dashboard.FinancialTrend.LastOrDefault();
            if (current is not null)
            {
                dashboard.ReportingPeriod = current.Period;
            }
        }

        if (current is not null)
        {
            dashboard.Revenue = current.Revenue;
            dashboard.OperatingExpenses = current.OperatingExpenses;
            dashboard.Noi = current.Noi;
            dashboard.DebtService = current.DebtService;
        }

        var currentPeriodIndex = dashboard.FinancialTrend.FindIndex(point =>
            string.Equals(point.Period, dashboard.ReportingPeriod, StringComparison.OrdinalIgnoreCase));
        var priorPeriod = currentPeriodIndex > 0
            ? dashboard.FinancialTrend[currentPeriodIndex - 1].Period
            : null;

        foreach (var property in dashboard.Properties)
        {
            var currentRows = rows.Where(row =>
                    string.Equals(row.Text("Property_ID"), property.PropertyId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Text("Fiscal_Year"), dashboard.ReportingPeriod, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var priorRows = priorPeriod is null
                ? new List<WorkbookRow>()
                : rows.Where(row =>
                        string.Equals(row.Text("Property_ID"), property.PropertyId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(row.Text("Fiscal_Year"), priorPeriod, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            property.Noi = currentRows.Count > 0
                ? SummarizeFinancialRows(currentRows, dashboard.ReportingPeriod).Noi
                : null;
            property.PreviousNoi = priorRows.Count > 0
                ? SummarizeFinancialRows(priorRows, priorPeriod ?? "").Noi
                : null;
            property.YoyNoiChange = property.Noi.HasValue && property.PreviousNoi is not null and not 0
                ? (property.Noi.Value - property.PreviousNoi.Value) / Math.Abs(property.PreviousNoi.Value)
                : null;
        }

        if (dashboard.FinancialTrend.Count == 0)
        {
            dashboard.Warnings.Add("No reporting-period operating data was mapped.");
        }
    }

    private static void ReadDebt(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        var currentSchedule = ReadDebtSchedule(view).GetValueOrDefault(dashboard.ReportingPeriod);
        if (dashboard.DebtService is null or 0 && currentSchedule is not null)
        {
            dashboard.DebtService = currentSchedule.Total;
        }

        var yearAmounts = new Dictionary<int, decimal>();
        decimal interest = currentSchedule?.Interest ?? 0;
        decimal masterDebtService = 0;
        decimal totalDebt = 0;
        var hasDebtAmount = false;

        foreach (var row in view.Rows("DATA_DEBT_MASTER"))
        {
            if (string.IsNullOrWhiteSpace(row.Text("Debt_ID")) || row.Bool("Included?") == false)
            {
                continue;
            }

            var principal = row.Decimal("Principal_Amount") ?? 0;
            if (row.Decimal("Principal_Amount").HasValue)
            {
                hasDebtAmount = true;
                totalDebt += principal;
            }
            masterDebtService += row.Decimal(
                "Source_Annual_Debt_Service",
                "Reporting_Year_Debt_Service") ?? 0;
            if (currentSchedule is null)
            {
                interest += row.Decimal("Reporting_Year_Interest") ?? 0;
            }

            var term = row.Int("Term_Years");
            var firstYear = FiscalSortKey(row.Text("First_Full_Service_Year"));
            if (term is > 0)
            {
                var maturityYear = (firstYear > 0 ? firstYear : FiscalSortKey(dashboard.ReportingPeriod)) + term.Value;
                if (maturityYear > 0 && principal > 0)
                {
                    yearAmounts[maturityYear] = yearAmounts.GetValueOrDefault(maturityYear) + principal;
                }
            }

            var propertyId = row.Text("Property_ID");
            var property = dashboard.Properties.FirstOrDefault(item =>
                string.Equals(item.PropertyId, propertyId, StringComparison.OrdinalIgnoreCase));
            var service = row.Decimal("Source_Annual_Debt_Service", "Reporting_Year_Debt_Service") ?? 0;
            if (property is not null && property.Noi.HasValue && service > 0)
            {
                property.Dscr = property.Noi.Value / service;
                if (property.Dscr < 1)
                {
                    property.RiskLevel = "High";
                }
                else if (property.Dscr < 1.2m && !string.Equals(property.RiskLevel, "High", StringComparison.OrdinalIgnoreCase))
                {
                    property.RiskLevel = "Watch";
                }
            }
        }

        dashboard.TotalDebt = hasDebtAmount ? totalDebt : null;
        if (dashboard.DebtService is null or 0 && masterDebtService > 0)
        {
            dashboard.DebtService = masterDebtService;
        }
        if (!dashboard.TotalDebt.HasValue)
        {
            dashboard.Warnings.Add("Instrument-level debt principal was not supplied; total debt is not assessed.");
        }

        dashboard.InterestCoverage = interest > 0 && dashboard.Noi.HasValue
            ? dashboard.Noi.Value / interest
            : null;
        dashboard.DebtHorizon = yearAmounts
            .OrderBy(pair => pair.Key)
            .Select(pair => new DebtHorizonPoint
            {
                Label = $"Estimated maturity {pair.Key}",
                Year = pair.Key,
                Amount = pair.Value
            })
            .ToList();
    }

    private static void ReadCashAndCapital(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        dashboard.UnrestrictedLiquidity = view.Rows("DATA_CASH_RESERVES")
            .Where(row => string.Equals(
                row.Text("Property_ID"),
                "PORTFOLIO",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.Int("Year") ?? 0)
            .Select(row => row.Decimal("Unrestricted_Cash"))
            .FirstOrDefault(value => value.HasValue);
        if (!dashboard.UnrestrictedLiquidity.HasValue)
        {
            dashboard.Warnings.Add("Unrestricted liquidity was not explicitly classified; liquidity is not assessed.");
        }

        decimal unfunded = 0;
        var hasCapitalValue = false;
        foreach (var row in view.Rows("DATA_CAPITAL_NEEDS"))
        {
            if (string.IsNullOrWhiteSpace(row.Text("Capital_Item_ID")))
            {
                continue;
            }

            var explicitUnfunded = row.Decimal("Unfunded_Amount");
            var estimated = row.Decimal("Estimated_Cost");
            var funded = row.Decimal("Funded_Amount");
            if (explicitUnfunded.HasValue)
            {
                hasCapitalValue = true;
                unfunded += Math.Max(0, explicitUnfunded.Value);
            }
            else if (estimated.HasValue)
            {
                hasCapitalValue = true;
                unfunded += Math.Max(0, estimated.Value - (funded ?? 0));
            }
        }

        dashboard.UnfundedCapitalNeeds = hasCapitalValue ? unfunded : null;
        if (!dashboard.UnfundedCapitalNeeds.HasValue)
        {
            dashboard.Warnings.Add("A quantified funded/unfunded capital-needs plan was not supplied.");
        }
    }

    private static void ReadAgreements(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        foreach (var row in view.Rows("DATA_AGREEMENTS"))
        {
            var id = row.Text("Agreement_ID");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var restrictions = new[]
                {
                    row.Text("Affordability_Requirement"),
                    row.Text("Rent_Restriction"),
                    row.Text("Use_Restriction"),
                    row.Text("Refinance_Restriction"),
                    row.Text("Disposition_Restriction")
                }
                .Count(item => !string.IsNullOrWhiteSpace(item) &&
                               !string.Equals(item, "No", StringComparison.OrdinalIgnoreCase));

            dashboard.Agreements.Add(new AgreementSummary
            {
                AgreementId = id,
                PropertyId = FirstNonBlank(row.Text("Property_ID"), "PORTFOLIO"),
                AgreementType = FirstNonBlank(row.Text("Agreement_Type"), "Not assessed"),
                ExpiryDate = row.Date("End_Date"),
                Impact = restrictions > 0 ? "Restriction recorded" : "Not assessed",
                Source = row.Text("Source_File_ID"),
                VerificationStatus = FirstNonBlank(row.Text("Review_Status"), "Needs review")
            });
        }
        if (dashboard.Agreements.Count == 0)
        {
            dashboard.Warnings.Add("Agreement and restriction evidence was not supplied; mission obligations are not assessed.");
        }
    }

    private static void ReadFlags(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        foreach (var row in view.Rows("ASSUMPTIONS_FLAGS"))
        {
            var id = row.Text("Flag_ID");
            var status = row.Text("Status");
            if (string.IsNullOrWhiteSpace(id) || IsClosed(status))
            {
                continue;
            }

            var issue = row.Text("Issue / Assumption");
            var level = FirstNonBlank(row.Text("Severity"), "Medium");
            dashboard.RiskItems.Add(new RiskItem
            {
                Name = FirstNonBlank(issue, id),
                Level = level,
                Note = FirstNonBlank(row.Text("Current Treatment"), row.Text("Reviewer Note"))
            });
        }
    }

    private static void BuildAssessmentSummaries(
        WorkbookDataView view,
        DashboardMetrics dashboard)
    {
        var financialStatus = dashboard.FinancialTrend.Count == 0
            ? "Not assessed"
            : dashboard.Dscr is < 1.2m || !dashboard.UnrestrictedLiquidity.HasValue
                ? "Conditional"
                : "Evidence available";
        var governanceStatus = view.Rows("DATA_OBLIGATIONS")
            .Any(row => !string.IsNullOrWhiteSpace(row.Text("Obligation_ID")))
            ? "Evidence available"
            : "Not assessed";
        var assetStatus = dashboard.Properties.Count == 0
            ? "Not assessed"
            : dashboard.Properties.Any(item => item.DataGaps > 0)
                ? "Conditional"
                : "Evidence available";
        var missionStatus = dashboard.ModeledUnits == 0
            ? "Not assessed"
            : dashboard.UnclassifiedUnits > 0 || dashboard.Agreements.Count == 0
                ? "Conditional"
                : "Evidence available";
        var evidenceStatus = dashboard.Warnings.Count > 0
            ? "Needs review"
            : "Evidence available";

        dashboard.AssessmentAreas =
        [
            Area("Financial resilience", financialStatus, FinancialNote(dashboard)),
            Area("Governance & organization", governanceStatus,
                governanceStatus == "Not assessed" ? "No governance obligation records were supplied." : "Governance obligations are recorded."),
            Area("Asset management", assetStatus,
                $"{dashboard.Properties.Count(item => item.DataGaps > 0)} properties have review items."),
            Area("Mission & public value", missionStatus,
                dashboard.UnclassifiedUnits > 0 ? $"{dashboard.UnclassifiedUnits:N0} units remain unclassified." : "Unit classifications are recorded."),
            Area("Data quality & evidence", evidenceStatus,
                $"{dashboard.Warnings.Count} intake warnings require review.")
        ];

        dashboard.GovernanceIndicators =
        [
            new() { Indicator = "Board capacity", Status = "Not assessed" },
            new() { Indicator = "Board independence", Status = "Not assessed" },
            new() { Indicator = "Policies & controls", Status = governanceStatus },
            new() { Indicator = "Strategic oversight", Status = "Not assessed" }
        ];

        dashboard.CapacityIndicators =
        [
            new() { Area = "Financial management", Status = financialStatus },
            new() { Area = "Asset management", Status = assetStatus },
            new() { Area = "Development capacity", Status = dashboard.NewProjects > 0 ? "Needs review" : "Not assessed" },
            new() { Area = "Staffing & skills", Status = "Not assessed" },
            new() { Area = "Data & reporting", Status = evidenceStatus }
        ];

        dashboard.Outcomes =
        [
            new()
            {
                Outcome = "Housing stability",
                CurrentEvidence = dashboard.ModeledUnits > 0 ? $"{dashboard.ModeledUnits:N0} modeled units" : "Not assessed",
                PrimarySource = "Property and unit records",
                Status = dashboard.ModeledUnits > 0 ? "Needs review" : "Not assessed"
            },
            new()
            {
                Outcome = "Affordability",
                CurrentEvidence = dashboard.AffordableUnits > 0 ? $"{dashboard.AffordableUnits:N0} classified affordable units" : "Not assessed",
                PrimarySource = "Unit mix / agreements",
                Status = dashboard.AffordableUnits > 0 ? "Needs review" : "Not assessed"
            },
            new()
            {
                Outcome = "Supportive housing",
                CurrentEvidence = dashboard.SupportiveUnits > 0 ? $"{dashboard.SupportiveUnits:N0} supportive units" : "Not assessed",
                PrimarySource = "Unit mix",
                Status = dashboard.SupportiveUnits > 0 ? "Needs review" : "Not assessed"
            }
        ];
    }

    private static Dictionary<string, DebtScheduleValue> ReadDebtSchedule(WorkbookDataView view)
    {
        var result = new Dictionary<string, DebtScheduleValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in view.Rows("DATA_DEBT_SCHEDULE"))
        {
            var period = row.Text("Fiscal_Year");
            if (string.IsNullOrWhiteSpace(period))
            {
                continue;
            }

            var principal = row.Decimal("Principal_Payment") ?? 0;
            var interest = row.Decimal("Interest_Payment") ?? 0;
            var total = row.Decimal("Total_Debt_Service") ?? principal + interest;
            result[period] = new DebtScheduleValue(total, principal, interest);
        }

        return result;
    }

    private static FinancialTrendPoint SummarizeFinancialRows(
        IReadOnlyCollection<WorkbookRow> rows,
        string period)
    {
        decimal revenue = 0;
        decimal expense = 0;
        decimal debt = 0;
        decimal? explicitNoi = null;

        foreach (var row in rows)
        {
            var category = WorkbookValue.Normalize(row.Text("IREI_Category"));
            var amount = row.Decimal("Amount") ?? 0;
            if (category.Contains("revenue", StringComparison.Ordinal) ||
                category is "income" or "operatingincome")
            {
                revenue += amount;
            }
            else if (category.Contains("operatingexpense", StringComparison.Ordinal) ||
                     category is "expense" or "expenses")
            {
                expense += Math.Abs(amount);
            }
            else if (category.Contains("debtprincipal", StringComparison.Ordinal) ||
                     category.Contains("debtinterest", StringComparison.Ordinal) ||
                     category.Contains("debtservice", StringComparison.Ordinal))
            {
                debt += Math.Abs(amount);
            }
            else if (category is "noi" or "netoperatingincome" or "cashbeforedebt")
            {
                explicitNoi = (explicitNoi ?? 0) + amount;
            }
        }

        var noi = explicitNoi ?? revenue - expense;
        return new FinancialTrendPoint
        {
            Period = period,
            Revenue = revenue,
            OperatingExpenses = expense,
            Noi = noi,
            DebtService = debt,
            Dscr = debt > 0 ? noi / debt : null
        };
    }

    private static int CountPropertyGaps(PropertySummary property)
    {
        var gaps = 0;
        if (string.IsNullOrWhiteSpace(property.City)) gaps++;
        if (property.Status == "Not assessed") gaps++;
        if (property.Classification == "Not assessed") gaps++;
        if (property.Units <= 0) gaps++;
        if (property.SourceConfidence == "Not assessed") gaps++;
        if (property.DataStatus == "Not assessed") gaps++;
        if (property.UnitConflict) gaps++;
        return gaps;
    }

    private static string DeterminePropertyRisk(PropertySummary property)
    {
        if (property.UnitConflict || property.DataGaps >= 3)
        {
            return "High";
        }

        var classification = WorkbookValue.Normalize(property.Classification);
        if (classification.Contains("watch", StringComparison.Ordinal) || property.DataGaps > 0)
        {
            return "Watch";
        }

        return property.DataStatus == "Not assessed" ? "Not assessed" : "Stable";
    }

    private static string DetermineOverallStatus(DashboardMetrics dashboard)
    {
        if (dashboard.Properties.Count == 0 || dashboard.FinancialTrend.Count == 0)
        {
            return "Not assessed";
        }

        if (dashboard.RiskItems.Any(item =>
                string.Equals(item.Level, "High", StringComparison.OrdinalIgnoreCase)) ||
            dashboard.Properties.Any(item => item.UnitConflict) ||
            dashboard.Dscr is < 1.2m ||
            dashboard.UnrestrictedLiquidity is null ||
            dashboard.UnfundedCapitalNeeds is null)
        {
            return "Conditional";
        }

        return "Ready for review";
    }

    private static AssessmentAreaSummary Area(string area, string status, string note) => new()
    {
        Area = area,
        Status = status,
        Note = note
    };

    private static string FinancialNote(DashboardMetrics dashboard)
    {
        if (dashboard.FinancialTrend.Count == 0)
        {
            return "No reporting-period operating data was supplied.";
        }

        if (dashboard.Dscr.HasValue)
        {
            return $"Portfolio DSCR is {dashboard.Dscr:0.00}x; liquidity and capital evidence are shown separately.";
        }

        return "Operating data is present; debt coverage is not assessed.";
    }

    private static bool IsIncludedProperty(DashboardMetrics dashboard, string propertyId) =>
        dashboard.Properties.Any(item =>
            item.Included &&
            string.Equals(item.PropertyId, propertyId, StringComparison.OrdinalIgnoreCase));

    private static bool IsExisting(string value)
    {
        var normalized = WorkbookValue.Normalize(value);
        return normalized.Contains("active", StringComparison.Ordinal) ||
               normalized.Contains("existing", StringComparison.Ordinal) ||
               normalized.Contains("operating", StringComparison.Ordinal) ||
               normalized.Contains("stable", StringComparison.Ordinal);
    }

    private static bool IsFuture(string value)
    {
        var normalized = WorkbookValue.Normalize(value);
        return normalized.Contains("newdev", StringComparison.Ordinal) ||
               normalized.Contains("future", StringComparison.Ordinal) ||
               normalized.Contains("development", StringComparison.Ordinal) ||
               normalized.Contains("project", StringComparison.Ordinal);
    }

    private static bool IsClosed(string value)
    {
        var normalized = WorkbookValue.Normalize(value);
        return normalized is "resolved" or "closed" or "complete" or "completed";
    }

    private static int FiscalSortKey(string period)
    {
        var digits = new string(period.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4 && int.TryParse(digits[..4], out var year))
        {
            return year;
        }

        return int.TryParse(period, out year) ? year : 0;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private sealed record DebtScheduleValue(decimal Total, decimal Principal, decimal Interest);
}
