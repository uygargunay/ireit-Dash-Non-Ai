using System.Text.Json.Serialization;

namespace IreiMvp.Web;

public sealed class IreiOptions
{
    public string DataRoot { get; set; } = "App_Data";
    public string TemplatePath { get; set; } = "Data/Templates/IREI_MVP_Stage1_V2_Blank_Template.xlsx";
    public string AdminKey { get; set; } = "";
    public long MaximumUploadBytes { get; set; } = 50 * 1024 * 1024;
    public MaterialityOptions Materiality { get; set; } = new();
}

public sealed class MaterialityOptions
{
    public decimal DscrMovement { get; set; } = 0.10m;
    public decimal CapitalNeedsAmount { get; set; } = 100_000m;
    public decimal CapitalNeedsPercent { get; set; } = 0.10m;
    public int DebtMaturityMonths { get; set; } = 24;
}

public sealed class SubmissionRecord
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string? ParentVersionId { get; set; }
    public string VersionStatus { get; set; } = "Working";
    public string ReportingPeriod { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string Status { get; set; } = "Processing";
    public string ProfileName { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public string InputPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public DashboardMetrics Dashboard { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class OrganizationPortfolioState
{
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string? CurrentVersionId { get; set; }
    public string? LatestApprovedVersionId { get; set; }
    public int NextVersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<AssessmentVersionRecord> Assessments { get; set; } = new();
    public List<FieldHistoryRecord> FieldHistory { get; set; } = new();
    public List<ChangeEventRecord> ChangeEvents { get; set; } = new();
    public List<ActionRecord> Actions { get; set; } = new();
    public List<ObligationRecord> Obligations { get; set; } = new();
    public List<ReportSnapshotRecord> Reports { get; set; } = new();
    public List<EvidenceRecord> Evidence { get; set; } = new();
    public List<FutureEventRecord> FutureEvents { get; set; } = new();
    public List<SourceDocumentRecord> SourceDocuments { get; set; } = new();
}

public sealed class AssessmentVersionRecord
{
    public string VersionId { get; set; } = "";
    public int VersionNumber { get; set; }
    public string SubmissionId { get; set; } = "";
    public string? ParentVersionId { get; set; }
    public string ReportingPeriod { get; set; } = "";
    public string VersionStatus { get; set; } = "Working";
    public string ReadinessStatus { get; set; } = "Conditional";
    public string SourceFileName { get; set; } = "";
    public string SourceFileHash { get; set; } = "";
    public string PrimarySourceId { get; set; } = "";
    public string CreatedBy { get; set; } = "Organization upload";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset SourceCutoffUtc { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public DashboardMetrics Dashboard { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class AssessmentView
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string? ParentVersionId { get; set; }
    public string VersionStatus { get; set; } = "Working";
    public string ReportingPeriod { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public DashboardMetrics Dashboard { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<AssessmentVersionSummary> Versions { get; set; } = new();
    public List<FieldHistoryRecord> FieldHistory { get; set; } = new();
    public List<ChangeEventRecord> Changes { get; set; } = new();
    public List<ActionRecord> Actions { get; set; } = new();
    public List<ObligationRecord> Obligations { get; set; } = new();
    public List<ReportSnapshotView> Reports { get; set; } = new();
    public List<EvidenceRecord> Evidence { get; set; } = new();
    public List<FutureEventRecord> FutureEvents { get; set; } = new();
    public List<SourceDocumentView> SourceDocuments { get; set; } = new();
}

public sealed class AssessmentVersionSummary
{
    public string VersionId { get; set; } = "";
    public string SubmissionId { get; set; } = "";
    public string? ParentVersionId { get; set; }
    public string ReportingPeriod { get; set; } = "";
    public string VersionStatus { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string SourceFileName { get; set; } = "";
}

public sealed class DashboardMetrics
{
    public string OrganizationName { get; set; } = "";
    public string OrganizationType { get; set; } = "Not provided";
    public string HeadOffice { get; set; } = "Not provided";
    public string IncorporatedYear { get; set; } = "Not provided";
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
    public int SupportiveUnits { get; set; }
    public int UnclassifiedUnits { get; set; }
    public int HouseholdsServed { get; set; }
    public decimal? AverageAffordabilityDepth { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? OperatingExpenses { get; set; }
    public decimal? Noi { get; set; }
    public decimal? DebtService { get; set; }
    public decimal? Affo { get; set; }
    public decimal? Dscr { get; set; }
    public decimal? TotalDebt { get; set; }
    public decimal? UnrestrictedLiquidity { get; set; }
    public decimal? InterestCoverage { get; set; }
    public decimal? UnfundedCapitalNeeds { get; set; }
    public string MissionFocus { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
    public List<PropertySummary> Properties { get; set; } = new();
    public List<RiskItem> RiskItems { get; set; } = new();
    public List<DebtHorizonPoint> DebtHorizon { get; set; } = new();
    public List<FinancialTrendPoint> FinancialTrend { get; set; } = new();
    public List<AgreementSummary> Agreements { get; set; } = new();
    public List<OutcomeEvidenceSummary> Outcomes { get; set; } = new();
    public List<AssessmentAreaSummary> AssessmentAreas { get; set; } = new();
    public List<GovernanceIndicator> GovernanceIndicators { get; set; } = new();
    public List<CapacityIndicator> CapacityIndicators { get; set; } = new();
    public List<DecisionRightSummary> DecisionRights { get; set; } = new();
}

public sealed class PropertySummary
{
    public string PropertyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Included { get; set; }
    public int Units { get; set; }
    public int? AlternateUnits { get; set; }
    public bool UnitConflict { get; set; }
    public string Classification { get; set; } = "";
    public string SourceConfidence { get; set; } = "Not assessed";
    public string DataStatus { get; set; } = "Not assessed";
    public int DataGaps { get; set; }
    public string RiskLevel { get; set; } = "Not assessed";
    public decimal? Noi { get; set; }
    public decimal? PreviousNoi { get; set; }
    public decimal? Dscr { get; set; }
    public decimal? YoyNoiChange { get; set; }
    public int AffordableUnits { get; set; }
    public int MarketUnits { get; set; }
    public int SupportiveUnits { get; set; }
}

public sealed class RiskItem
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "Moderate";
    public string Note { get; set; } = "";
}

public sealed class DebtHorizonPoint
{
    public string Label { get; set; } = "";
    public int? Year { get; set; }
    public decimal Amount { get; set; }
}

public sealed class FinancialTrendPoint
{
    public string Period { get; set; } = "";
    public decimal Revenue { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal Noi { get; set; }
    public decimal DebtService { get; set; }
    public decimal? Dscr { get; set; }
}

public sealed class AgreementSummary
{
    public string AgreementId { get; set; } = "";
    public string PropertyId { get; set; } = "PORTFOLIO";
    public string AgreementType { get; set; } = "";
    public DateTimeOffset? ExpiryDate { get; set; }
    public string Impact { get; set; } = "Not assessed";
    public string Source { get; set; } = "";
    public string VerificationStatus { get; set; } = "Needs review";
}

public sealed class OutcomeEvidenceSummary
{
    public string Outcome { get; set; } = "";
    public string CurrentEvidence { get; set; } = "";
    public string PrimarySource { get; set; } = "";
    public string Status { get; set; } = "Needs review";
}

public sealed class AssessmentAreaSummary
{
    public string Area { get; set; } = "";
    public string Status { get; set; } = "Not assessed";
    public string Note { get; set; } = "";
}

public sealed class GovernanceIndicator
{
    public string Indicator { get; set; } = "";
    public string Status { get; set; } = "Not assessed";
}

public sealed class CapacityIndicator
{
    public string Area { get; set; } = "";
    public string Status { get; set; } = "Not assessed";
}

public sealed class DecisionRightSummary
{
    public string Control { get; set; } = "";
    public string DecisionBody { get; set; } = "";
    public string Status { get; set; } = "Not assessed";
}

public sealed class FieldHistoryRecord
{
    public string HistoryId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string? PriorVersionId { get; set; }
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string FieldName { get; set; } = "";
    public string? PreviousValue { get; set; }
    public string? CurrentValue { get; set; }
    public decimal? PreviousNumeric { get; set; }
    public decimal? CurrentNumeric { get; set; }
    public string Unit { get; set; } = "";
    public string ChangeStatus { get; set; } = "No Change";
    public bool IsMaterial { get; set; }
    public string MaterialityRule { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public string ReviewStatus { get; set; } = "Needs review";
    public DateTimeOffset RecordedUtc { get; set; }

    [JsonIgnore]
    public decimal? ChangeAmount => CurrentNumeric.HasValue && PreviousNumeric.HasValue
        ? CurrentNumeric.Value - PreviousNumeric.Value
        : null;

    [JsonIgnore]
    public decimal? ChangePercent => PreviousNumeric.HasValue && PreviousNumeric.Value != 0 && CurrentNumeric.HasValue
        ? (CurrentNumeric.Value - PreviousNumeric.Value) / Math.Abs(PreviousNumeric.Value)
        : null;
}

public sealed class ChangeEventRecord
{
    public string ChangeId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string Category { get; set; } = "";
    public string FieldOrMetric { get; set; } = "";
    public string ChangeType { get; set; } = "Changed";
    public string PriorValue { get; set; } = "";
    public string CurrentValue { get; set; } = "";
    public string Direction { get; set; } = "";
    public bool IsMaterial { get; set; }
    public string MaterialityBasis { get; set; } = "";
    public string Impact { get; set; } = "";
    public string ReviewStatus { get; set; } = "Needs review";
    public string? LinkedActionId { get; set; }
    public DateTimeOffset DetectedUtc { get; set; }
}

public sealed class ActionRecord
{
    public string ActionId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string Issue { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Response { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTimeOffset? DueDate { get; set; }
    public string DecisionBody { get; set; } = "Management";
    public string Status { get; set; } = "New";
    public string RiskLevel { get; set; } = "Medium";
    public string Outcome { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public string? LinkedChangeId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? ClosedUtc { get; set; }
    public string? SupersedesActionId { get; set; }
}

public sealed class ObligationRecord
{
    public string ObligationId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string Source { get; set; } = "";
    public string Obligation { get; set; } = "";
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string Owner { get; set; } = "";
    public DateTimeOffset? EffectiveDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string Recurrence { get; set; } = "";
    public string Status { get; set; } = "Upcoming";
    public string EvidenceReference { get; set; } = "";
    public string ReviewStatus { get; set; } = "Needs review";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ReportSnapshotRecord
{
    public string ReportId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string ReportName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Period { get; set; } = "";
    public int ReportVersion { get; set; } = 1;
    public string Status { get; set; } = "Draft";
    public string Recipient { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? SharedBy { get; set; }
    public DateTimeOffset? SharedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string? SupersedesReportId { get; set; }
    public string ArtifactPath { get; set; } = "";
}

public sealed class ReportSnapshotView
{
    public string ReportId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string ReportName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Period { get; set; } = "";
    public int ReportVersion { get; set; }
    public string Status { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? SharedBy { get; set; }
    public DateTimeOffset? SharedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string? SupersedesReportId { get; set; }
    public bool ArtifactAvailable { get; set; }
}

public sealed class EvidenceRecord
{
    public string EvidenceId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string Value { get; set; } = "";
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string SourceFile { get; set; } = "";
    public string SourceLocation { get; set; } = "";
    public DateTimeOffset? EffectiveDate { get; set; }
    public DateTimeOffset? SourceDate { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public string MappingMethod { get; set; } = "Deterministic header mapping";
    public string Reviewer { get; set; } = "";
    public string VerificationStatus { get; set; } = "Needs review";
    public string? ReplacesEvidenceId { get; set; }
}

public sealed class FutureEventRecord
{
    public string EventId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string EntityType { get; set; } = "Portfolio";
    public string EntityId { get; set; } = "PORTFOLIO";
    public string EventType { get; set; } = "";
    public string EventName { get; set; } = "";
    public DateTimeOffset? EventDate { get; set; }
    public decimal? Amount { get; set; }
    public string Impact { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "Upcoming";
}

public sealed class SourceDocumentRecord
{
    public string SourceId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SourceType { get; set; } = "Uploaded Excel workbook";
    public DateTimeOffset UploadedUtc { get; set; }
    public string ReviewStatus { get; set; } = "Needs review";
    public string Sha256 { get; set; } = "";
    public string StoredPath { get; set; } = "";
}

public sealed class SourceDocumentView
{
    public string SourceId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SourceType { get; set; } = "";
    public DateTimeOffset UploadedUtc { get; set; }
    public string ReviewStatus { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class CreateActionRequest
{
    public string? EntityType { get; set; } = "Portfolio";
    public string? EntityId { get; set; } = "PORTFOLIO";
    public string? Issue { get; set; }
    public string? Cause { get; set; }
    public string? Impact { get; set; }
    public string? Response { get; set; }
    public string? Owner { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? DecisionBody { get; set; } = "Management";
    public string? RiskLevel { get; set; } = "Medium";
    public string? EvidenceReference { get; set; }
    public string? LinkedChangeId { get; set; }
}

public sealed class UpdateActionRequest
{
    public string? Status { get; set; }
    public string? Owner { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? Response { get; set; }
    public string? Outcome { get; set; }
    public string? EvidenceReference { get; set; }
}

public sealed class CreateObligationRequest
{
    public string? Source { get; set; }
    public string? Obligation { get; set; }
    public string? EntityType { get; set; } = "Portfolio";
    public string? EntityId { get; set; } = "PORTFOLIO";
    public string? Owner { get; set; }
    public DateTimeOffset? EffectiveDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? Recurrence { get; set; }
    public string? EvidenceReference { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpdateObligationRequest
{
    public string? Status { get; set; }
    public string? Owner { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? EvidenceReference { get; set; }
    public string? Notes { get; set; }
}

public sealed class CreateReportRequest
{
    public string? ReportName { get; set; }
    public string? Purpose { get; set; }
    public string? Recipient { get; set; }
    public string? Scope { get; set; }
}

public sealed class ApprovalRequest
{
    public string ApprovedBy { get; set; } = "Authorized reviewer";
}

public sealed class ShareReportRequest
{
    public string SharedBy { get; set; } = "Authorized user";
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
    public string SourceFileHash { get; set; } = "";
    public DateTimeOffset? SourceCutoffUtc { get; set; }
}

public sealed class MappingBuildResult
{
    public List<CellPatch> Patches { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string ReportingPeriod { get; set; } = "";
    public string SourceCutoffDate { get; set; } = "";
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
