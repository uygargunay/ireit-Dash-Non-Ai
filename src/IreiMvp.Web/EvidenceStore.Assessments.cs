using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

public sealed class EvidenceAssessment
{
    public string TenantId { get; set; } = "";
    public string[] ScopeIds { get; set; } = [];
    public string[] SourceVersionIds { get; set; } = [];
    public V4Assessment Assessment { get; set; } = new();
}
public sealed record EvidenceFactFilter(string Source = "", string Property = "", string Period = "", string Status = "", string Search = "");
public sealed record EvidenceReviewAll(EvidenceFactFilter Filter, string Reason = "", bool Confirmed = false, string Token = "");
public sealed record EvidenceBulkPlan(int Matched, int Eligible, int Blocked, string Token, Dictionary<string, int> Reasons, object[] Examples);
public sealed record EvidenceGroupContext(EvidenceFactFilter Filter, string PropertyId, string Basis, string Currency, decimal Scale,
    string PeriodOverride, string YearConvention, string Reason, bool Confirmed, string Token);
public sealed record EvidenceCreateAssessment(string[] SourceVersionIds, string Basis, string Currency, string CurrentYear,
    decimal? TargetCoverage, string Reason, bool Confirmed, string Token = "", string FullOperationYear = "");
public sealed record EvidenceAssessmentPlan(int Eligible, int Excluded, string[] Years, string Token, string[] Warnings);

public sealed partial class EvidenceStore
{
    private static string Digest(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    private bool SourceAvailable(EvidenceDocument d, EvidenceVersion v) => v.Processing == "Accepted" && v.DeletedAt is null &&
        (d.SourceMode == "ClientHosted" || File.Exists(BlobPath(d.TenantId, v.Hash)));
    private EvidenceFact[] FilterFacts(EvidenceState s, EvidenceUser u, string tenant, EvidenceFactFilter filter)
    {
        NeedStaff(u); Tenant(s, u, tenant);
        var versions = s.Versions.Where(v => s.Documents.Any(d => d.Id == v.DocumentId && d.TenantId == tenant && Scope(u, tenant, d.ScopeId))).Select(v => v.Id).ToHashSet();
        return s.Facts.Where(f => f.TenantId == tenant && versions.Contains(f.DocumentVersionId) && f.Status is not ("Rejected" or "Superseded") &&
            (filter.Source.Length == 0 || f.DocumentVersionId == filter.Source) &&
            (filter.Property.Length == 0 || (f.PropertyId.Length > 0 ? f.PropertyId : f.SuggestedProperty.Length > 0 ? f.SuggestedProperty : "__org") == filter.Property) &&
            (filter.Period.Length == 0 || f.Period == filter.Period) && (filter.Status.Length == 0 || f.Status == filter.Status) &&
            $"{f.MetricCode} {f.SourceLabel} {f.Locator}".Contains(filter.Search, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Id).ToArray();
    }
    private string? VerificationProblem(EvidenceState s, EvidenceUser u, EvidenceFact f, Dictionary<string, EvidenceFact[]> activeKeys)
    {
        if (f.Status == "Verified") return "Already verified";
        if (f.SuggestedProperty.Length > 0 || f.Period.Length == 0 || f.Period == "Unconfirmed" || f.Basis.Length == 0 || f.Basis == "Unconfirmed") return "Confirm property, period and reporting basis";
        var d = FactDocument(s, u, f); var v = Version(s, u, f.DocumentVersionId);
        if (!SourceAvailable(d, v)) return "Source unavailable or not screened";
        try { ValidateFact(s, u, f.TenantId, f); } catch (InvalidDataException e) { return e.Message; }
        if (f.Certainty == "Unavailable") return "Value unavailable";
        if (s.Issues.Any(i => i.TenantId == f.TenantId && i.Status == "Open" && i.Severity == "Material" &&
            (i.FactId == f.Id || i.VersionId == f.DocumentVersionId || (i.VersionId.Length == 0 && i.FactId.Length == 0 && i.ScopeId == d.ScopeId)))) return "Resolve material source or scope issue";
        if (f.SupersedesId is not null) { var prior = s.Facts.Single(x => x.Id == f.SupersedesId); FactDocument(s, u, prior); }
        if (activeKeys[FactKey(f)].Any(o => o.Id != f.Id && o.Id != f.SupersedesId)) return "Choose one authoritative fact; duplicate or competing records";
        return null;
    }
    private (EvidenceBulkPlan Plan, EvidenceFact[] Ready) ReviewAllPlan(EvidenceState s, EvidenceUser u, string tenant, EvidenceFactFilter filter)
    {
        var facts = FilterFacts(s, u, tenant, filter); var ready = new List<EvidenceFact>(); var reasons = new Dictionary<string, int>(); var examples = new List<object>();
        var activeKeys = s.Facts.Where(f => f.TenantId == tenant && f.Status is not ("Rejected" or "Superseded")).GroupBy(FactKey).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var f in facts)
        {
            var problem = VerificationProblem(s, u, f, activeKeys);
            if (problem is null) ready.Add(f);
            else { reasons[problem] = reasons.GetValueOrDefault(problem) + 1; if (examples.Count < 30) examples.Add(new { f.Id, f.MetricCode, f.Locator, Reason = problem }); }
        }
        var token = Digest(new { filter, Facts = facts, Ready = ready.Select(f => f.Id), Issues = s.Issues.Where(i => i.TenantId == tenant), Versions = facts.Select(f => s.Versions.Single(v => v.Id == f.DocumentVersionId)).DistinctBy(v => v.Id) });
        return (new(facts.Length, ready.Count, facts.Length - ready.Count, token, reasons, examples.ToArray()), ready.ToArray());
    }
    public Task<EvidenceBulkPlan> PreviewReviewAll(EvidenceUser actor, string tenant, EvidenceFactFilter filter, CancellationToken ct) => Transaction(s => ReviewAllPlan(s, Actor(s, actor), tenant, filter).Plan, ct);
    public Task<EvidenceBulkPlan> ReviewAll(EvidenceUser actor, string tenant, EvidenceReviewAll input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var (plan, ready) = ReviewAllPlan(s, u, tenant, input.Filter);
        if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Reason)) throw new InvalidDataException("Confirm the source review and provide a rationale before reviewing all eligible records.");
        if (input.Token != plan.Token) throw new InvalidDataException("Records changed. Preview the review again.");
        foreach (var f in ready) { ct.ThrowIfCancellationRequested(); RecordFactReview(s, u, f, FactDocument(s, u, f), new("Verified", input.Reason)); }
        Audit(s, u, tenant, "All eligible filtered facts reviewed", tenant, $"{ready.Length} verified; {plan.Blocked} unchanged. {input.Reason}"); return plan;
    }, ct);
    // This operation confirms a shared source context; it never certifies its numbers.
    public Task<object> ContextAll(EvidenceUser actor, string tenant, EvidenceGroupContext input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var facts = FilterFacts(s, u, tenant, input.Filter).Where(f => f.Status != "Verified").ToArray();
        if (input.Filter.Source.Length == 0 || input.Filter.Property.Length == 0 || facts.Select(f => f.DocumentVersionId).Distinct().Count() != 1 ||
            facts.Select(f => f.PropertyId.Length > 0 ? f.PropertyId : f.SuggestedProperty).Distinct().Count() != 1)
            throw new InvalidDataException("Choose one source and proposed property/sheet; a shared context cannot combine different assets.");
        if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Reason) || input.Scale is not (1 or 1000 or 1000000) ||
            input.Basis is not ("Audited actual" or "Management actual" or "Budget" or "Projection") || input.Currency is not ("CAD" or "USD") || input.YearConvention is not ("Keep ranges" or "Ending year" or "Starting year"))
            throw new InvalidDataException("Confirm the source context, reporting basis, fiscal-year convention, scale and rationale.");
        var token = Digest(facts);
        if (input.Token.Length == 0) return (object)new { Token = token, Matched = facts.Length };
        if (input.Token != token) throw new InvalidDataException("Records changed. Preview the context group again.");
        var updated = 0; var blocked = new Dictionary<string, int>();
        foreach (var prior in facts)
        {
            ct.ThrowIfCancellationRequested(); var d = FactDocument(s, u, prior); var v = Version(s, u, prior.DocumentVersionId);
            if (!SourceAvailable(d, v)) throw new InvalidDataException("Source is unavailable.");
            if (prior.Unit != "Unconfirmed" && prior.SuggestedProperty.Length == 0 && prior.Basis != "Unconfirmed" && ValidYear(prior.Period))
            { blocked["Context already confirmed; originals unchanged"] = blocked.GetValueOrDefault("Context already confirmed; originals unchanged") + 1; continue; }
            if (prior.Unit == "Currency" && input.Scale != 1) { blocked["Already expressed in base currency; choose scale 1"] = blocked.GetValueOrDefault("Already expressed in base currency; choose scale 1") + 1; continue; }
            var period = input.PeriodOverride.Length > 0 ? input.PeriodOverride : NormalizeYear(prior.Period, input.YearConvention);
            if (!ValidYear(period)) { blocked["Fiscal period needs correction"] = blocked.GetValueOrDefault("Fiscal period needs correction") + 1; continue; }
            var f = Clone(prior); f.Id = Guid.NewGuid().ToString("N"); f.CreatedAt = DateTimeOffset.UtcNow; f.SupersedesId = prior.Id; f.Status = "Unreviewed"; f.Reviewer = "";
            f.PropertyId = input.PropertyId; f.SuggestedProperty = ""; f.Period = period; f.Basis = input.Basis; f.Operator = u.Id; f.Reason = input.Reason + $"; source monetary scale {input.Scale}";
            var metric = EvidenceMetrics.All.Single(m => m.Code == f.MetricCode); f.Unit = metric.Unit; f.Currency = metric.Unit == "Currency" ? input.Currency : "";
            if (f.Value.HasValue && metric.Unit == "Currency") f.Value *= input.Scale;
            try { ValidateFact(s, u, tenant, f); } catch (InvalidDataException e) { blocked[e.Message] = blocked.GetValueOrDefault(e.Message) + 1; continue; }
            prior.Status = "Superseded"; s.Facts.Add(f); AddContradictions(s, u, f); updated++;
        }
        if (s.Facts.Count(f => f.TenantId == tenant) > 100000) throw new InvalidDataException("Fact capacity reached.");
        Audit(s, u, tenant, "Filtered group context confirmed", tenant, $"{updated} corrected; originals retained. {input.Reason}");
        return (object)new { Updated = updated, Blocked = blocked };
    }, ct);
    internal static bool ValidYear(string year) => Regex.IsMatch(year, @"^20\d{2}-20\d{2}$") && int.Parse(year[5..], CultureInfo.InvariantCulture) == int.Parse(year[..4], CultureInfo.InvariantCulture) + 1;
    private static string NormalizeYear(string period, string convention)
    {
        var p = period.Trim().Replace('–', '-').Replace('/', '-'); if (ValidYear(p)) return p;
        var m = Regex.Match(p, @"^(?:FY)?(20\d{2})$", RegexOptions.IgnoreCase); if (!m.Success || convention == "Keep ranges") return "";
        var y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return convention == "Ending year" ? $"{y - 1}-{y}" : $"{y}-{y + 1}";
    }
    private (EvidenceAssessmentPlan Plan, EvidenceFact[] Facts, EvidenceVersion[] Versions) AssessmentPlan(EvidenceState s, EvidenceUser u, string tenant, EvidenceCreateAssessment input)
    {
        NeedStaff(u); Tenant(s, u, tenant);
        if (input.SourceVersionIds.Length is < 1 or > 100 || input.SourceVersionIds.Distinct().Count() != input.SourceVersionIds.Length ||
            input.Basis is not ("Audited actual" or "Management actual" or "Budget" or "Projection") || input.Currency is not ("CAD" or "USD") || !ValidYear(input.CurrentYear) || (input.FullOperationYear.Length > 0 && !ValidYear(input.FullOperationYear)) ||
            (input.TargetCoverage.HasValue && input.TargetCoverage is < 1 or > 5)) throw new InvalidDataException("Choose source versions, one reporting basis, currency, valid current fiscal year and an optional target coverage between 1 and 5.");
        var versions = input.SourceVersionIds.Select(id => Version(s, u, id)).ToArray();
        foreach (var v in versions) { var d = Document(s, u, v.DocumentId); if (d.TenantId != tenant) throw new EvidenceDeniedException(); if (!SourceAvailable(d, v)) throw new InvalidDataException("All selected source versions must remain available and screened."); }
        var candidates = FilterFacts(s, u, tenant, new()).Where(f => input.SourceVersionIds.Contains(f.DocumentVersionId)).ToArray();
        var facts = candidates.Where(f => f.Status == "Verified" && f.Basis == input.Basis && f.SuggestedProperty.Length == 0 && ValidYear(f.Period) &&
            EvidenceAssessmentBuilder.Supported.Contains(f.MetricCode) && f.Value.HasValue && (f.Unit == "Count" || f.Currency == input.Currency)).ToArray();
        foreach (var f in facts) { ValidateFact(s, u, tenant, f); if (s.Issues.Any(i => i.TenantId == tenant && i.Status == "Open" && i.Severity == "Material" &&
            (i.FactId == f.Id || i.VersionId == f.DocumentVersionId || (i.FactId.Length == 0 && i.VersionId.Length == 0 && i.ScopeId == FactDocument(s, u, f).ScopeId)))) throw new InvalidDataException("Resolve material issues affecting selected verified data before creating an assessment."); }
        var years = facts.Select(f => f.Period).Distinct().Order().ToArray(); var warnings = new List<string>();
        foreach (var g in facts.GroupBy(f => (f.PropertyId, f.Period, f.MetricCode)).Where(g => g.Count() > 1)) warnings.Add($"{g.Key.Period} / {g.Key.PropertyId} / {g.Key.MetricCode}: {g.Count()} competing totals; this metric will be Conflict, never summed.");
        warnings.Add($"{candidates.Length - facts.Length} records excluded: unreviewed, other reporting basis/currency, invalid fiscal period or not supported by the financial engine.");
        warnings.Add("Portfolio controls and property detail stay separate. Missing portfolio totals are not inferred by summing a potentially incomplete property sample.");
        return (new(facts.Length, candidates.Length - facts.Length, years, Digest(new { input.SourceVersionIds, input.Basis, input.Currency, input.CurrentYear, input.FullOperationYear, input.TargetCoverage, facts, versions }), warnings.ToArray()), facts, versions);
    }
    public Task<EvidenceAssessmentPlan> PreviewAssessment(EvidenceUser actor, string tenant, EvidenceCreateAssessment input, CancellationToken ct) => Transaction(s => AssessmentPlan(s, Actor(s, actor), tenant, input).Plan, ct);
    public Task<object> CreateAssessment(EvidenceUser actor, string tenant, EvidenceCreateAssessment input, V4WorkbookImporter importer, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var t = Tenant(s, u, tenant); var (plan, facts, versions) = AssessmentPlan(s, u, tenant, input);
        if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Reason) || input.Token != plan.Token) throw new InvalidDataException("Preview the current inputs and explicitly confirm assessment creation with a rationale.");
        if (facts.Length == 0 || !plan.Years.Contains(input.CurrentYear) || (input.FullOperationYear.Length > 0 && !plan.Years.Contains(input.FullOperationYear))) throw new InvalidDataException("Review source context and verify at least one supported financial or homes fact in the selected current fiscal year first.");
        if (s.Assessments.Count(a => a.TenantId == tenant) >= 100) throw new InvalidDataException("Assessment capacity reached; arrange a reviewed archive.");
        var properties = s.Properties.Where(p => p.TenantId == tenant && facts.Any(f => f.PropertyId == p.Id)).ToArray();
        var manifest = versions.Concat(properties.Select(p => Version(s, u, p.EvidenceVersionId))).DistinctBy(v => v.Id).ToArray();
        foreach (var v in manifest) { var d = Document(s, u, v.DocumentId); if (d.TenantId != tenant || !SourceAvailable(d, v)) throw new InvalidDataException("Reviewed inventory evidence must also remain available."); }
        var a = EvidenceAssessmentBuilder.Build(importer, env.ContentRootPath, t, properties, facts, manifest, input, plan.Warnings);
        var scopes = manifest.Select(v => Document(s, u, v.DocumentId).ScopeId).Concat(facts.Where(f => f.PropertyId.Length > 0).Select(f => s.Properties.Single(p => p.Id == f.PropertyId).ScopeId)).Distinct().ToArray();
        // Client data stays in the encrypted portal store, never the legacy unscoped V4 directory.
        s.Assessments.Add(new() { TenantId = tenant, ScopeIds = scopes, SourceVersionIds = manifest.Select(v => v.Id).ToArray(), Assessment = a });
        Audit(s, u, tenant, "Working V4 assessment created from reviewed evidence", a.Id, input.Reason); return (object)new { a.Id, a.Version, Url = $"v4.html?portal=1&assessment={a.Id}" };
    }, ct);
    private EvidenceAssessment Assessment(EvidenceState s, EvidenceUser u, string id)
    {
        NeedStaff(u); var item = s.Assessments.SingleOrDefault(a => a.Assessment.Id == id) ?? throw new EvidenceDeniedException(); Tenant(s, u, item.TenantId);
        if (item.ScopeIds.Any(scope => !Scope(u, item.TenantId, scope))) throw new EvidenceDeniedException();
        foreach (var source in item.SourceVersionIds) { var v = Version(s, u, source); if (!SourceAvailable(Document(s, u, v.DocumentId), v)) throw new InvalidDataException("A source is now unavailable. Restore its authorized reference or create a new assessment from available evidence."); }
        return item;
    }
    public Task<object> AssessmentPage(EvidenceUser actor, string id, string page, CancellationToken ct) => Transaction(s => V4ViewService.Page(Assessment(s, Actor(s, actor), id).Assessment, page), ct);
    public Task<object> AssessmentEvidence(EvidenceUser actor, string id, string metricId, CancellationToken ct) => Transaction(s =>
    {
        var a = Assessment(s, Actor(s, actor), id).Assessment; var metric = a.Metrics.SingleOrDefault(m => m.Id == metricId) ?? throw new InvalidDataException("Metric not found.");
        return (object)new { Result = metric, MappedFacts = a.Facts.Where(f => metric.InputIds.Contains(f.Id)), Mappings = a.Rows("20_FIELD_MAPPING"), SourceNotes = a.Rows("12_SOURCE_NOTES"), RelatedExceptions = a.Rows("51_EXCEPTIONS"), SourceChecksum = a.Sha256, AssessmentVersion = a.Version };
    }, ct);
    public Task<object> ConfigureAssessment(EvidenceUser actor, string id, V4ConfigRequest input, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var item = Assessment(s, u, id); var a = item.Assessment;
        if (a.Version != input.ExpectedVersion || !V4CalculationEngine.Years(a).Any(y => y.Year == input.CurrentYear)) throw new InvalidDataException("Reload the assessment and choose a configured year.");
        a.Config["CURRENT_FISCAL_YEAR"] = input.CurrentYear; a.Version++; V4CalculationEngine.Calculate(a);
        a.Events.Add(new(Guid.NewGuid().ToString("N"), "Reporting year selected", a.Id, a.Version, DateTimeOffset.UtcNow, u.Id, "Reporting year selection", "", input.CurrentYear));
        Audit(s, u, item.TenantId, "Assessment reporting year selected", id, input.CurrentYear); return (object)new { a.Id, a.Version }; }, ct);
    public Task<object> TestAssessmentOption(EvidenceUser actor, string id, V4OptionRequest input, CancellationToken ct) => Transaction(s => V4CalculationEngine.TestOption(Assessment(s, Actor(s, actor), id).Assessment, input), ct);
}
