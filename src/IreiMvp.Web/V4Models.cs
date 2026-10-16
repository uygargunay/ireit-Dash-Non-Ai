namespace IreiMvp.Web;

public sealed class V4Assessment
{
    public string Id { get; set; } = "";
    public string AssessmentId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? PriorRuntimeAssessmentId { get; set; }
    public string Status { get; set; } = "Working";
    public string MethodologyVersion { get; set; } = "V4.2-PILOT/1";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Config { get; set; } = [];
    public Dictionary<string, List<V4Row>> Registers { get; set; } = [];
    public List<V4Fact> Facts { get; set; } = [];
    public List<V4Metric> Metrics { get; set; } = [];
    public List<string> ImportFindings { get; set; } = [];
    public List<V4Event> Events { get; set; } = [];
    public V4Baseline? Baseline { get; set; }
    public List<V4Outcome> Outcomes { get; set; } = [];
    public IEnumerable<V4Row> Rows(string sheet) => Registers.GetValueOrDefault(sheet) ?? [];
    public string Setting(string key) => Config.GetValueOrDefault(key, "");
    public V4Metric? Metric(string scope, string year, string code) => Metrics.FirstOrDefault(m => m.Scope == scope && m.Year == year && m.Code == code);
}

public sealed record V4Row(string Id, string Sheet, int Row, Dictionary<string, string> Fields,
    Dictionary<string, string> Formulas)
{
    public string Text(string key) => Fields.GetValueOrDefault(key, "");
    public decimal? Number(string key) => WorkbookValue.Decimal(Text(key));
}
public sealed record V4Fact(string Id, string Scope, string Year, string Scenario, string Code,
    decimal? Value, string ValueState, string Grain, string ReviewStatus, string MappingStatus,
    string Certainty, string Currency, string SourceId, string SourceLocation, string Cell,
    string Formula, string[] InputCells);
public sealed record V4Metric(string Id, string Scope, string Year, string Scenario, string Code,
    decimal? Value, string ValueState, string DisplayState, string ReviewState, string CalcId,
    string CalcVersion, string MethodologyVersion, string[] InputIds, string Severity = "None");
public sealed record V4Baseline(string Id, string AssessmentId, string OrganizationId,
    DateTimeOffset Date, string Period, string Scope, string MethodologyVersion, V4Metric[] Metrics);
public sealed record V4Event(string Id, string Kind, string RecordId, int Version, DateTimeOffset Date,
    string Reviewer, string Reason, string Before, string After);
public sealed record V4Outcome(string Id, string ActionId, string DecisionId, string MetricDefinition,
    string Domain, string Unit, string Direction, string Scope, string MethodologyVersion,
    decimal? BaselineValue, decimal? CurrentValue, string ValueState, string Period, string EvidenceId,
    string ContributionState, string ReviewStatus, string Reviewer, decimal? Change, string ComparisonState);
public sealed record V4ConfigRequest(string CurrentYear, int ExpectedVersion);
public sealed record V4RecordRequest(string Sheet, string Id, Dictionary<string, string> Fields,
    string Reviewer, string Reason, int ExpectedVersion);
public sealed record V4OptionRequest(string Year, string Case, string Kind, decimal Amount,
    decimal AdditionalDebtService = 0, bool SubsidyConfirmed = false);
public sealed record V4OutcomeRequest(string Id, string ActionId, string DecisionId, string MetricDefinition,
    string Domain, string Unit, string Direction, string Scope, string MethodologyVersion,
    decimal? BaselineValue, decimal? CurrentValue, string Period, string EvidenceId,
    string ContributionState, string Reviewer, int ExpectedVersion);
