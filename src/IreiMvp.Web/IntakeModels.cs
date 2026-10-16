namespace IreiMvp.Web;

// Staff-only spreadsheet demonstration. These states are not workbook review enums.
public sealed class IntakeBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string AssessmentId { get; set; } = "";
    public string Currency { get; set; } = "CAD";
    public string CurrentYear { get; set; } = "";
    public int FiscalStartMonth { get; set; } = 4;
    public int Version { get; set; } = 1;
    public List<IntakeSource> Sources { get; set; } = [];
    public List<IntakeCandidate> Candidates { get; set; } = [];
    public List<IntakeReviewEvent> Reviews { get; set; } = [];
    public List<IntakeEffort> Effort { get; set; } = [];
    public List<string> AssessmentRuntimeIds { get; set; } = [];
    public int ExportedVersion { get; set; }
}
public sealed class IntakeSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "Registered";
    public string Error { get; set; } = "";
    public string ParserVersion { get; set; } = "spreadsheet/1";
    public DateTimeOffset UploadedUtc { get; set; } = DateTimeOffset.UtcNow;
    public double ProcessingMilliseconds { get; set; }
    public List<IntakeSheet> Sheets { get; set; } = [];
}
public sealed record IntakeSheet(string Name, int Rows, int Columns, int NonemptyCells, int FormulaCells, int MissingCaches, string State);
public sealed class IntakeCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceId { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string Cell { get; set; } = "";
    public string Label { get; set; } = "";
    public string RawText { get; set; } = "";
    public string Formula { get; set; } = "";
    public string ValueState { get; set; } = "Missing";
    public string SuggestedCode { get; set; } = "";
    public string RawYear { get; set; } = "";
    public string SuggestedProperty { get; set; } = "";
    public string State { get; set; } = "Proposed";
    public string Code { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Year { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string Grain { get; set; } = "";
    public decimal? Value { get; set; }
    public string Certainty { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string SupersedesId { get; set; } = "";
}
public sealed record IntakeReviewEvent(string CandidateId, string Reviewer, DateTimeOffset Date, string Before, string After, string Reason);
public sealed record IntakeReview(string State, string Code, string Scope, string Year, string Scenario, string Grain,
    decimal? Value, string Certainty, string Reviewer, string Rationale, int ExpectedVersion, string SupersedesId = "");
public sealed record IntakeEffort(string Role, string Task, decimal Minutes, decimal MachineCost, string CostCurrency, string Note);
