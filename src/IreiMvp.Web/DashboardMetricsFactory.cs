using System.Globalization;

namespace IreiMvp.Web;

public sealed class DashboardMetricsFactory
{
    public DashboardMetrics Build(
        WorkbookSnapshot template,
        IReadOnlyCollection<CellPatch> patches,
        string organizationName,
        string profileName,
        IReadOnlyCollection<string> warnings)
    {
        var overlay = new PatchOverlay(template, patches);
        var properties = ReadProperties(overlay);

        var revenue = SumRows(
            overlay,
            "DATA_OPERATING_ACTUALS",
            categoryColumn: 9,
            amountColumn: 10,
            acceptedCategories: ["Revenue"]);

        var expenses = SumRows(
            overlay,
            "DATA_OPERATING_ACTUALS",
            categoryColumn: 9,
            amountColumn: 10,
            acceptedCategories: ["Operating Expense", "Operating Expenses", "Expense"]);

        var debtService = 0m;
        for (var row = 7; row <= 506; row++)
        {
            if (!IsYes(overlay.Get("DATA_DEBT_MASTER", row, 4)))
            {
                continue;
            }

            debtService += ToDecimal(
                overlay.Get("DATA_DEBT_MASTER", row, 14) ??
                overlay.Get("DATA_DEBT_MASTER", row, 13));
        }

        var unitMixCount = 0;
        var affordableUnits = 0;
        var marketUnits = 0;
        for (var row = 7; row <= 506; row++)
        {
            unitMixCount += ToInt(overlay.Get("DATA_UNIT_MIX", row, 8));
            affordableUnits += ToInt(overlay.Get("DATA_UNIT_MIX", row, 9));
            marketUnits += ToInt(overlay.Get("DATA_UNIT_MIX", row, 10));
        }

        var noi = revenue - expenses;
        var affo = noi - debtService;
        var dscr = debtService == 0 ? 0 : noi / debtService;

        var openWarnings = warnings
            .Concat(ReadOpenFlags(overlay))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        var dataScore = Math.Clamp(90 - openWarnings.Count * 4, 35, 95);
        var financialScore = debtService == 0
            ? 55
            : Math.Clamp((int)Math.Round(Math.Min(Math.Max(dscr, 0), 1.5m) / 1.5m * 100), 25, 95);
        var governanceScore = openWarnings.Count == 0 ? 82 : 66;
        var impactScore = affordableUnits > 0 ? 74 : 52;
        var riskScore = openWarnings.Count == 0 ? 80 : 58;

        return new DashboardMetrics
        {
            OrganizationName = organizationName,
            ReportingPeriod = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
            ProfileName = profileName,
            OverallStatus = openWarnings.Count > 0 ? "Conditional" : "Ready for review",
            TotalProperties = properties.Count,
            IncludedProperties = properties.Count(property => property.Included),
            ExistingAssets = properties.Count(property =>
                property.Included &&
                property.Status.Contains("Active", StringComparison.OrdinalIgnoreCase)),
            NewProjects = properties.Count(property =>
                property.Included &&
                (property.Status.Contains("New", StringComparison.OrdinalIgnoreCase) ||
                 property.Status.Contains("Develop", StringComparison.OrdinalIgnoreCase))),
            ModeledUnits = Math.Max(
                properties.Where(property => property.Included).Sum(property => property.Units),
                unitMixCount),
            AffordableUnits = affordableUnits,
            MarketUnits = marketUnits,
            Revenue = revenue,
            OperatingExpenses = expenses,
            Noi = noi,
            DebtService = debtService,
            Affo = affo,
            Dscr = dscr,
            DataReadinessScore = dataScore,
            FinancialReadinessScore = financialScore,
            GovernanceReadinessScore = governanceScore,
            ImpactReadinessScore = impactScore,
            RiskManagementScore = riskScore,
            MissionFocus = "Define the organization’s value, risk, readiness and affordability impact before selecting any funding or capital pathway.",
            Warnings = openWarnings,
            Properties = properties.Take(30).ToList(),
            RiskItems =
            [
                new RiskItem
                {
                    Name = "Data risk",
                    Level = openWarnings.Count > 5 ? "High" : "Moderate",
                    Note = "Source conflicts, missing schedules and uncertain mappings remain explicit for organizational review."
                },
                new RiskItem
                {
                    Name = "Liquidity risk",
                    Level = debtService > 0 && dscr >= 1.20m ? "Low" : "High",
                    Note = debtService == 0
                        ? "Debt-service information is incomplete."
                        : "The indicator uses a simplified NOI-to-debt-service calculation and is not a financing opinion."
                },
                new RiskItem
                {
                    Name = "Mission / restriction risk",
                    Level = "Moderate",
                    Note = "Affordability covenants, restrictions and disclosure permissions require confirmation."
                },
                new RiskItem
                {
                    Name = "Operational risk",
                    Level = properties.Count > 0 ? "Moderate" : "High",
                    Note = "The MVP identifies source evidence but does not replace professional operating review."
                }
            ],
            DebtHorizon = BuildDebtHorizon(overlay)
        };
    }

    private static List<PropertySummary> ReadProperties(PatchOverlay overlay)
    {
        var properties = new List<PropertySummary>();

        for (var row = 7; row <= 506; row++)
        {
            var id = overlay.Get("DATA_PROPERTY_MASTER", row, 1)?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var status = overlay.Get("DATA_PROPERTY_MASTER", row, 4)?.ToString() ?? "";
            var includedCell = overlay.Get("DATA_PROPERTY_MASTER", row, 5);

            properties.Add(new PropertySummary
            {
                PropertyId = id,
                Name = overlay.Get("DATA_PROPERTY_MASTER", row, 2)?.ToString() ?? id,
                City = overlay.Get("DATA_PROPERTY_MASTER", row, 3)?.ToString() ?? "",
                Status = status,
                Included = includedCell is null || IsYes(includedCell),
                Classification = overlay.Get("DATA_PROPERTY_MASTER", row, 6)?.ToString() ?? "",
                Units = ToInt(
                    overlay.Get("DATA_PROPERTY_MASTER", row, 9) ??
                    overlay.Get("DATA_PROPERTY_MASTER", row, 8))
            });
        }

        return properties;
    }

    private static decimal SumRows(
        PatchOverlay overlay,
        string sheet,
        int categoryColumn,
        int amountColumn,
        IReadOnlyCollection<string> acceptedCategories)
    {
        var total = 0m;

        for (var row = 7; row <= 1006; row++)
        {
            var category = overlay.Get(sheet, row, categoryColumn)?.ToString()?.Trim() ?? "";
            if (acceptedCategories.Any(value =>
                string.Equals(value, category, StringComparison.OrdinalIgnoreCase)))
            {
                total += ToDecimal(overlay.Get(sheet, row, amountColumn));
            }
        }

        return total;
    }

    private static IEnumerable<string> ReadOpenFlags(PatchOverlay overlay)
    {
        for (var row = 7; row <= 506; row++)
        {
            var status = overlay.Get("ASSUMPTIONS_FLAGS", row, 9)?.ToString();
            var issue = overlay.Get("ASSUMPTIONS_FLAGS", row, 4)?.ToString();

            if (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(issue))
            {
                yield return issue;
            }
        }
    }

    private static List<DebtHorizonPoint> BuildDebtHorizon(PatchOverlay overlay)
    {
        var currentYear = DateTime.UtcNow.Year;
        var groups = new Dictionary<int, decimal>();

        for (var row = 7; row <= 506; row++)
        {
            if (!IsYes(overlay.Get("DATA_DEBT_MASTER", row, 4)))
            {
                continue;
            }

            var term = ToInt(overlay.Get("DATA_DEBT_MASTER", row, 9));
            var principal = ToDecimal(overlay.Get("DATA_DEBT_MASTER", row, 7));
            if (term <= 0 || principal <= 0)
            {
                continue;
            }

            var year = currentYear + term;
            groups[year] = groups.GetValueOrDefault(year) + principal;
        }

        return groups
            .OrderBy(pair => pair.Key)
            .Take(10)
            .Select(pair => new DebtHorizonPoint
            {
                Year = pair.Key,
                Amount = pair.Value
            })
            .ToList();
    }

    private static bool IsYes(object? value) =>
        string.Equals(value?.ToString()?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.ToString()?.Trim(), "True", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.ToString()?.Trim(), "1", StringComparison.OrdinalIgnoreCase);

    private static int ToInt(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is decimal decimalValue)
        {
            return (int)Math.Round(decimalValue);
        }

        return decimal.TryParse(
            value.ToString(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var number)
            ? (int)Math.Round(number)
            : 0;
    }

    private static decimal ToDecimal(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        return decimal.TryParse(
            value.ToString(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var number)
            ? number
            : 0;
    }

    private sealed class PatchOverlay
    {
        private readonly WorkbookSnapshot _template;
        private readonly Dictionary<(string Sheet, string Cell), CellPatch> _patches;

        public PatchOverlay(
            WorkbookSnapshot template,
            IReadOnlyCollection<CellPatch> patches)
        {
            _template = template;
            _patches = patches
                .GroupBy(patch => (
                    patch.Sheet.ToUpperInvariant(),
                    patch.Cell.ToUpperInvariant()))
                .ToDictionary(group => group.Key, group => group.Last());
        }

        public object? Get(string sheet, int row, int column)
        {
            var reference = XlsxAddress.ToCellReference(row, column);
            if (_patches.TryGetValue(
                (sheet.ToUpperInvariant(), reference.ToUpperInvariant()),
                out var patch))
            {
                if (patch.Clear)
                {
                    return null;
                }

                return patch.Formula is null
                    ? patch.Value
                    : patch.CachedValue;
            }

            return _template.Sheets.TryGetValue(sheet, out var targetSheet)
                ? targetSheet.GetValue(row, column)
                : null;
        }
    }
}
