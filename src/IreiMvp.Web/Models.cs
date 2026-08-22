using System.Text.Json.Serialization;

namespace IreiMvp.Web;

public sealed class IreiOptions
{
    public string DataRoot { get; set; } = "App_Data";
    public string TemplatePath { get; set; } = "Data/Templates/IREI_MVP_Blank_Template_Generic_v1.1.xlsx";
    public string AdminKey { get; set; } = "kelly";
    public long MaximumUploadBytes { get; set; } = 50 * 1024 * 1024;
    public AiOptions Ai { get; set; } = new();
}

public sealed class AiOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://api.openai.com/v1/responses";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4.1-mini";
    public int MaxOutputTokens { get; set; } = 3000;
    public double MinimumAutoApplyConfidence { get; set; } = 0.90;
}

public sealed class SubmissionRecord
{
    public string Id { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string Status { get; set; } = "Processing";
    public string ProfileName { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string InputPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public DashboardMetrics Dashboard { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class DashboardMetrics
{
    public string OrganizationName { get; set; } = "";
    public string ReportingPeriod { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string OverallStatus { get; set; } = "Conditional";
    public int TotalProperties { get; set; }
    public int IncludedProperties { get; set; }
    public int ExistingAssets { get; set; }
    public int NewProjects { get; set; }
    public int ModeledUnits { get; set; }
    public int AffordableUnits { get; set; }
    public int MarketUnits { get; set; }
    public decimal Revenue { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal Noi { get; set; }
    public decimal DebtService { get; set; }
    public decimal Affo { get; set; }
    public decimal Dscr { get; set; }
    public int DataReadinessScore { get; set; }
    public int FinancialReadinessScore { get; set; }
    public int GovernanceReadinessScore { get; set; }
    public int ImpactReadinessScore { get; set; }
    public int RiskManagementScore { get; set; }
    public string MissionFocus { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
    public List<PropertySummary> Properties { get; set; } = new();
    public List<RiskItem> RiskItems { get; set; } = new();
    public List<DebtHorizonPoint> DebtHorizon { get; set; } = new();

    [JsonIgnore]
    public int OverallReadinessScore =>
        (DataReadinessScore + FinancialReadinessScore + GovernanceReadinessScore +
         ImpactReadinessScore + RiskManagementScore) / 5;
}

public sealed class PropertySummary
{
    public string PropertyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Included { get; set; }
    public int Units { get; set; }
    public string Classification { get; set; } = "";
}

public sealed class RiskItem
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "Moderate";
    public string Note { get; set; } = "";
}

public sealed class DebtHorizonPoint
{
    public int Year { get; set; }
    public decimal Amount { get; set; }
}

public sealed class CellPatch
{
    public string Sheet { get; set; } = "";
    public string Cell { get; set; } = "";
    public object? Value { get; set; }
    public string? Formula { get; set; }
    public object? CachedValue { get; set; }
    public bool Clear { get; set; }
}

public sealed class TransformResult
{
    public string ProfileName { get; set; } = "";
    public DashboardMetrics Dashboard { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int PatchCount { get; set; }
}

public sealed class MappingBuildResult
{
    public List<CellPatch> Patches { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<MappingSuggestion> Suggestions { get; set; } = new();
    public string ReportingPeriod { get; set; } = "";
}

public sealed class MappingSuggestion
{
    public string TargetSheet { get; set; } = "";
    public string TargetField { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public string SourceField { get; set; } = "";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AiMappingResponse
{
    public List<MappingSuggestion> Mappings { get; set; } = new();
}

public sealed class WorkbookSnapshot
{
    public string Sha256 { get; init; } = "";
    public Dictionary<string, SheetSnapshot> Sheets { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasSheet(string name) => Sheets.ContainsKey(name);
}

public sealed class SheetSnapshot
{
    public string Name { get; init; } = "";
    public Dictionary<string, CellSnapshot> Cells { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int MaxRow { get; init; }
    public int MaxColumn { get; init; }

    public CellSnapshot? GetCell(int row, int column)
    {
        Cells.TryGetValue(XlsxAddress.ToCellReference(row, column), out var cell);
        return cell;
    }

    public object? GetValue(int row, int column) => GetCell(row, column)?.Value;
}

public sealed class CellSnapshot
{
    public object? Value { get; init; }
    public string? Formula { get; init; }
}

public sealed class SheetProfile
{
    public string Name { get; set; } = "";
    public int HeaderRow { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<List<object?>> SampleRows { get; set; } = new();
}

public static class XlsxAddress
{
    public static (int Row, int Column) Parse(string reference)
    {
        var column = 0;
        var index = 0;

        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = column * 26 + char.ToUpperInvariant(reference[index]) - 'A' + 1;
            index++;
        }

        if (column == 0 || index >= reference.Length ||
            !int.TryParse(reference[index..], out var row))
        {
            throw new FormatException($"Invalid cell reference: {reference}");
        }

        return (row, column);
    }

    public static string ToCellReference(int row, int column)
    {
        if (row < 1 || column < 1)
        {
            throw new ArgumentOutOfRangeException();
        }

        var letters = "";
        var current = column;
        while (current > 0)
        {
            current--;
            letters = (char)('A' + current % 26) + letters;
            current /= 26;
        }

        return $"{letters}{row}";
    }
}
