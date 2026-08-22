using System.Globalization;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class VersionComparisonEngine
{
    private readonly MaterialityOptions _materiality;

    public VersionComparisonEngine(IOptions<IreiOptions> options)
    {
        _materiality = options.Value.Materiality;
    }

    public VersionComparisonResult Compare(
        string organizationId,
        AssessmentVersionRecord current,
        AssessmentVersionRecord? prior)
    {
        var result = new VersionComparisonResult();
        var now = DateTimeOffset.UtcNow;
        var priorValues = prior is null
            ? new Dictionary<FieldKey, FieldValue>()
            : Flatten(prior.Dashboard);
        var currentValues = Flatten(current.Dashboard);
        var keys = priorValues.Keys
            .Union(currentValues.Keys)
            .OrderBy(key => key.EntityType)
            .ThenBy(key => key.EntityId)
            .ThenBy(key => key.FieldName)
            .ToList();

        foreach (var key in keys)
        {
            priorValues.TryGetValue(key, out var previous);
            currentValues.TryGetValue(key, out var present);
            var changeType = ChangeType(previous, present);
            var materiality = EvaluateMateriality(key, previous, present, changeType);
            var history = new FieldHistoryRecord
            {
                HistoryId = NewId("HIST"),
                OrganizationId = organizationId,
                VersionId = current.VersionId,
                PriorVersionId = prior?.VersionId,
                EntityType = key.EntityType,
                EntityId = key.EntityId,
                FieldName = key.FieldName,
                PreviousValue = previous?.Display,
                CurrentValue = present?.Display,
                PreviousNumeric = previous?.Numeric,
                CurrentNumeric = present?.Numeric,
                Unit = present?.Unit ?? previous?.Unit ?? "",
                ChangeStatus = changeType,
                IsMaterial = materiality.IsMaterial,
                MaterialityRule = materiality.Rule,
                SourceReference = current.PrimarySourceId,
                ReviewStatus = prior is null ? "Baseline" : "Needs review",
                RecordedUtc = now
            };
            result.FieldHistory.Add(history);

            if (changeType == "No Change" || prior is null)
            {
                continue;
            }

            result.ChangeEvents.Add(new ChangeEventRecord
            {
                ChangeId = NewId("CHG"),
                OrganizationId = organizationId,
                VersionId = current.VersionId,
                EntityType = key.EntityType,
                EntityId = key.EntityId,
                Category = key.Category,
                FieldOrMetric = key.FieldName,
                ChangeType = changeType,
                PriorValue = previous?.Display ?? "Not present",
                CurrentValue = present?.Display ?? "Not present",
                Direction = Direction(previous, present, changeType),
                IsMaterial = materiality.IsMaterial,
                MaterialityBasis = materiality.Rule,
                Impact = Impact(key, materiality.IsMaterial),
                ReviewStatus = "Needs review",
                DetectedUtc = now
            });
        }

        return result;
    }

    private Dictionary<FieldKey, FieldValue> Flatten(DashboardMetrics dashboard)
    {
        var result = new Dictionary<FieldKey, FieldValue>();

        AddNumber(result, "Portfolio", "PORTFOLIO", "Portfolio", "Total properties", dashboard.TotalProperties, "count");
        AddNumber(result, "Portfolio", "PORTFOLIO", "Portfolio", "Included properties", dashboard.IncludedProperties, "count");
        AddNumber(result, "Portfolio", "PORTFOLIO", "Portfolio", "Modeled units", dashboard.ModeledUnits, "units");
        AddNumber(result, "Portfolio", "PORTFOLIO", "Mission", "Affordable units", dashboard.AffordableUnits, "units");
        AddNumber(result, "Portfolio", "PORTFOLIO", "Mission", "Supportive units", dashboard.SupportiveUnits, "units");
        AddNumber(result, "Portfolio", "PORTFOLIO", "Mission", "Unclassified units", dashboard.UnclassifiedUnits, "units");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Revenue", dashboard.Revenue, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Operating expenses", dashboard.OperatingExpenses, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "NOI", dashboard.Noi, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Debt service", dashboard.DebtService, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Cash flow after debt", dashboard.Affo, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "DSCR", dashboard.Dscr, "x");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Total debt", dashboard.TotalDebt, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Unrestricted liquidity", dashboard.UnrestrictedLiquidity, "CAD");
        AddNullableNumber(result, "Portfolio", "PORTFOLIO", "Financial", "Unfunded capital needs", dashboard.UnfundedCapitalNeeds, "CAD");
        AddText(result, "Portfolio", "PORTFOLIO", "Readiness", "Overall status", dashboard.OverallStatus);

        foreach (var property in dashboard.Properties)
        {
            var id = property.PropertyId;
            AddText(result, "Property", id, "Property", "Property name", property.Name);
            AddText(result, "Property", id, "Property", "Portfolio status", property.Status);
            AddText(result, "Property", id, "Property", "Classification", property.Classification);
            AddText(result, "Property", id, "Risk", "Risk level", property.RiskLevel);
            AddText(result, "Property", id, "Data quality", "Unit conflict", property.UnitConflict ? "Yes" : "No");
            AddNumber(result, "Property", id, "Property", "Units", property.Units, "units");
            AddNullableNumber(result, "Property", id, "Financial", "NOI", property.Noi, "CAD");
            AddNullableNumber(result, "Property", id, "Financial", "DSCR", property.Dscr, "x");
        }

        foreach (var maturity in dashboard.DebtHorizon.Where(item => item.Year.HasValue))
        {
            AddNumber(
                result,
                "Debt maturity",
                $"MATURITY-{maturity.Year!.Value}",
                "Debt maturity",
                "Maturity amount",
                maturity.Amount,
                "CAD");
        }

        foreach (var agreement in dashboard.Agreements)
        {
            AddText(result, "Agreement", agreement.AgreementId, "Mission", "Agreement type", agreement.AgreementType);
            AddText(result, "Agreement", agreement.AgreementId, "Mission", "Agreement impact", agreement.Impact);
            if (agreement.ExpiryDate.HasValue)
            {
                AddText(
                    result,
                    "Agreement",
                    agreement.AgreementId,
                    "Mission",
                    "Agreement expiry",
                    agreement.ExpiryDate.Value.UtcDateTime.ToString("yyyy-MM-dd"));
            }
        }

        return result;
    }

    private MaterialityResult EvaluateMateriality(
        FieldKey key,
        FieldValue? previous,
        FieldValue? current,
        string changeType)
    {
        if (string.Equals(key.Category, "Debt maturity", StringComparison.OrdinalIgnoreCase))
        {
            var yearText = key.EntityId.Split('-').LastOrDefault();
            var withinHorizon = int.TryParse(yearText, out var maturityYear) &&
                                maturityYear <= DateTime.UtcNow.AddMonths(_materiality.DebtMaturityMonths).Year;
            return new(
                withinHorizon,
                $"Debt-maturity change inside the configured {_materiality.DebtMaturityMonths}-month horizon.");
        }

        if (changeType is "Added" or "Removed")
        {
            return new(true, "Entity or tracked field was added/removed.");
        }

        var name = WorkbookValue.Normalize(key.FieldName);
        var delta = previous?.Numeric.HasValue == true && current?.Numeric.HasValue == true
            ? Math.Abs(current.Numeric.Value - previous.Numeric.Value)
            : (decimal?)null;
        var percent = previous?.Numeric is not null and not 0 && current?.Numeric.HasValue == true
            ? delta / Math.Abs(previous.Numeric.Value)
            : null;

        if (name == "dscr")
        {
            return new(
                delta >= _materiality.DscrMovement,
                $"Absolute DSCR movement >= {_materiality.DscrMovement:0.00}x.");
        }

        if (name == "unfundedcapitalneeds")
        {
            return new(
                delta >= _materiality.CapitalNeedsAmount || percent >= _materiality.CapitalNeedsPercent,
                $"Capital-needs movement >= {_materiality.CapitalNeedsAmount:C0} or {_materiality.CapitalNeedsPercent:P0}.");
        }

        if (name is "risklevel" or "classification" or "unitconflict" or "overallstatus")
        {
            return new(true, "Any status, risk-class, classification, or conflict change is material.");
        }

        if (name is "affordableunits" or "supportiveunits" or "unclassifiedunits")
        {
            return new(delta > 0, "Any mission-unit classification change requires review.");
        }

        if (string.Equals(key.EntityType, "Agreement", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "Any affordability/agreement obligation change requires review.");
        }

        return new(false, "Change recorded; no configured materiality threshold was triggered.");
    }

    private static string ChangeType(FieldValue? previous, FieldValue? current)
    {
        if (previous is null && current is not null) return "Added";
        if (previous is not null && current is null) return "Removed";
        if (previous is null || current is null) return "No Change";

        if (previous.Numeric.HasValue && current.Numeric.HasValue)
        {
            return previous.Numeric.Value == current.Numeric.Value ? "No Change" : "Changed";
        }

        return string.Equals(previous.Display, current.Display, StringComparison.OrdinalIgnoreCase)
            ? "No Change"
            : "Changed";
    }

    private static string Direction(
        FieldValue? previous,
        FieldValue? current,
        string changeType)
    {
        if (changeType == "Added") return "Added";
        if (changeType == "Removed") return "Removed";
        if (previous?.Numeric.HasValue == true && current?.Numeric.HasValue == true)
        {
            return current.Numeric.Value > previous.Numeric.Value
                ? "Increase"
                : current.Numeric.Value < previous.Numeric.Value
                    ? "Decrease"
                    : "No change";
        }

        return "Changed";
    }

    private static string Impact(FieldKey key, bool material) => material
        ? $"Material {key.Category.ToLowerInvariant()} change; reviewer confirmation and action assessment required."
        : $"{key.Category} change recorded for traceability.";

    private static void AddNumber(
        IDictionary<FieldKey, FieldValue> values,
        string entityType,
        string entityId,
        string category,
        string name,
        decimal value,
        string unit) => values[new(entityType, entityId, category, name)] =
            new(value.ToString("0.####", CultureInfo.InvariantCulture), value, unit);

    private static void AddNullableNumber(
        IDictionary<FieldKey, FieldValue> values,
        string entityType,
        string entityId,
        string category,
        string name,
        decimal? value,
        string unit)
    {
        if (value.HasValue)
        {
            AddNumber(values, entityType, entityId, category, name, value.Value, unit);
        }
    }

    private static void AddText(
        IDictionary<FieldKey, FieldValue> values,
        string entityType,
        string entityId,
        string category,
        string name,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[new(entityType, entityId, category, name)] = new(value, null, "");
        }
    }

    private static string NewId(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

    private sealed record FieldKey(
        string EntityType,
        string EntityId,
        string Category,
        string FieldName);

    private sealed record FieldValue(string Display, decimal? Numeric, string Unit);

    private sealed record MaterialityResult(bool IsMaterial, string Rule);
}

public sealed class VersionComparisonResult
{
    public List<FieldHistoryRecord> FieldHistory { get; set; } = new();
    public List<ChangeEventRecord> ChangeEvents { get; set; } = new();
}
