using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IreiMvp.Web;
public sealed partial class EvidenceStore
{
    private static string FactKey(EvidenceFact f) => string.Join("|", f.TenantId, f.PropertyId, f.MetricCode, f.Unit, f.Currency, f.Period, f.AsOfDate, f.Basis);
    private static EvidenceDocument FactDocument(EvidenceState s, EvidenceUser u, EvidenceFact f) => Document(s, u, Version(s, u, f.DocumentVersionId).DocumentId);
    private static void ValidateFact(EvidenceState s, EvidenceUser u, string tenant, EvidenceFact f)
    {
        var source = Version(s, u, f.DocumentVersionId); var d = Document(s, u, source.DocumentId);
        if (d.TenantId != tenant) throw new EvidenceDeniedException();
        var metric = EvidenceMetrics.All.SingleOrDefault(m => m.Code == f.MetricCode) ?? throw new InvalidDataException("Choose a metric from the common dictionary.");
        if (string.IsNullOrWhiteSpace(f.Locator) || f.Locator.Length > 500 || string.IsNullOrWhiteSpace(f.Unit) || string.IsNullOrWhiteSpace(f.Period) || string.IsNullOrWhiteSpace(f.Basis) || string.IsNullOrWhiteSpace(f.Reason))
            throw new InvalidDataException("Source page/sheet/cell, period, unit, reporting basis and reason are required.");
        if (f.PropertyId.Length > 0 && !s.Properties.Any(p => p.Id == f.PropertyId && p.TenantId == tenant && p.ScopeId == d.ScopeId && Scope(u, tenant, p.ScopeId))) throw new EvidenceDeniedException();
        if (metric.Unit == "Text" ? f.Value is not null || string.IsNullOrWhiteSpace(f.TextValue) : f.Value is null && f.Certainty != "Unavailable")
            throw new InvalidDataException("Supply the expected text or numeric value. Missing evidence is not zero.");
        if (f.Certainty == "Unavailable" && f.Value is not null) throw new InvalidDataException("Unavailable evidence must not be entered as a numeric zero or estimate.");
        if (metric.Unit == "Currency" && (f.Unit != "Currency" || !new[] { "CAD", "USD", "Other" }.Contains(f.Currency))) throw new InvalidDataException("Confirm currency and Currency unit for monetary facts.");
        if (metric.Unit == "Count" && (f.Unit != "Count" || f.Value < 0 || (f.Value.HasValue && decimal.Truncate(f.Value.Value) != f.Value.Value))) throw new InvalidDataException("Counts must be nonnegative whole numbers.");
        if (f.Certainty is not ("Verified source" or "Estimated" or "Client-reported" or "Unavailable")) throw new InvalidDataException("Choose an evidence certainty state.");
        if (f.AsOfDate.Length > 0 && !DateOnly.TryParseExact(f.AsOfDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new InvalidDataException("Use an ISO as-of date.");
    }
    public Task<EvidenceFact> AddFact(EvidenceUser actor, string tenant, EvidenceFact input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant); ValidateFact(s, u, tenant, input);
        if (s.Facts.Count(f => f.TenantId == tenant) >= 100000) throw new InvalidDataException("Fact capacity reached; arrange a reviewed capacity extension.");
        var f = new EvidenceFact { TenantId = tenant, DocumentVersionId = input.DocumentVersionId, PropertyId = input.PropertyId, MetricCode = input.MetricCode,
            Value = input.Value, TextValue = input.TextValue, Unit = input.Unit, Currency = input.Currency, Period = input.Period, AsOfDate = input.AsOfDate, Basis = input.Basis,
            Locator = input.Locator, Method = "Manual", Certainty = input.Certainty, Operator = u.Id, Reason = input.Reason, SupersedesId = input.SupersedesId,
            OriginalValue = input.Value?.ToString(CultureInfo.InvariantCulture) ?? input.TextValue };
        if (f.SupersedesId is not null)
        { var prior = s.Facts.SingleOrDefault(x => x.Id == f.SupersedesId && x.TenantId == tenant) ?? throw new EvidenceDeniedException(); FactDocument(s, u, prior); f.OriginalValue = prior.OriginalValue; }
        s.Facts.Add(f); AddContradictions(s, u, f); Audit(s, u, tenant, "Fact proposed", f.Id, f.SupersedesId is null ? "New source-linked observation" : "Correction retains prior fact"); return Clone(f);
    }, ct);
    private static void AddContradictions(EvidenceState s, EvidenceUser u, EvidenceFact fact)
    {
        if (fact.Certainty == "Unavailable") return;
        var conflicts = s.Facts.Where(x => x.Id != fact.Id && x.Certainty != "Unavailable" && FactKey(x) == FactKey(fact) && x.Status is not ("Rejected" or "Superseded") && (x.Value != fact.Value || x.TextValue != fact.TextValue)).ToArray();
        if (conflicts.Length > 0) s.Issues.Add(new() { TenantId = fact.TenantId, VersionId = fact.DocumentVersionId, FactId = fact.Id, Kind = "Contradictory figures", Owner = u.Id,
            Question = "Conflicting sources report different values for the same metric, scope, period, unit, currency and reporting basis. Review both source versions before selecting a value." });
    }
    public Task<EvidenceFact> ReviewFact(EvidenceUser actor, string fact, EvidenceFactReview input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); var f = s.Facts.SingleOrDefault(f => f.Id == fact) ?? throw new EvidenceDeniedException(); var d = FactDocument(s, u, f);
        if (f.Status is "Verified" or "Superseded" or "Rejected") throw new InvalidDataException("Reviewed values are immutable. Propose a correction instead.");
        if (input.Status is not ("Verified" or "Qualified" or "Needs clarification" or "Rejected") || string.IsNullOrWhiteSpace(input.Reason)) throw new InvalidDataException("Review status and rationale required.");
        if (input.Status == "Verified")
        {
            ValidateFact(s, u, d.TenantId, f);
            if (f.Certainty == "Unavailable") throw new InvalidDataException("Unavailable values cannot become verified numeric facts.");
            if (Version(s, u, f.DocumentVersionId).Processing != "Accepted") throw new InvalidDataException("Source must clear file screening before factual verification.");
            if (s.Issues.Any(i => i.TenantId == d.TenantId && i.Status == "Open" && i.Severity == "Material" && (i.FactId == f.Id || i.VersionId == f.DocumentVersionId))) throw new InvalidDataException("Resolve the affected material exception before verifying this fact.");
            if (s.Facts.Any(other => other.Id != f.Id && FactKey(other) == FactKey(f) && other.Status == "Verified" && other.Id != f.SupersedesId)) throw new InvalidDataException("A verified fact already exists. Select it explicitly as the fact being corrected.");
            if (f.SupersedesId is not null)
            { var prior = s.Facts.Single(x => x.Id == f.SupersedesId); FactDocument(s, u, prior); prior.Status = "Superseded"; Audit(s, u, d.TenantId, "Fact superseded", prior.Id, f.Id); }
        }
        f.Status = input.Status; f.Reviewer = u.Id; Audit(s, u, d.TenantId, "Fact reviewed", f.Id, input.Status + ": " + input.Reason); return Clone(f);
    }, ct);
    public Task<object> ImportSpreadsheet(EvidenceUser actor, string versionId, IntakeExtractor extractor, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); var version = Version(s, u, versionId); var doc = Document(s, u, version.DocumentId);
        if (version.Processing != "Accepted" || version.DeletedAt is not null || version.Extension is not (".xlsx" or ".csv")) throw new InvalidDataException("Choose an accepted XLSX/CSV source. PDFs and other documents can be manually indexed.");
        var bytes = ReadBlob(doc, version); var snapshot = extractor.ReadBytes(bytes, version.Name); var inserted = 0; var skipped = 0;
        var unitDefault = "Currency";
        // Canonical long-format CSV/table import; the source file itself supplies all values.
        foreach (var sheet in snapshot.Sheets.Values)
        {
            var header = Enumerable.Range(1, Math.Min(100, sheet.MaxRow)).FirstOrDefault(r => Enumerable.Range(1, sheet.MaxColumn).Any(c => WorkbookValue.Text(sheet.GetValue(r, c)).Trim().ToLowerInvariant() == "metric_code"));
            if (header == 0) continue;
            var columns = Enumerable.Range(1, sheet.MaxColumn).Where(c => WorkbookValue.Text(sheet.GetValue(header, c)).Length > 0).ToDictionary(c => WorkbookValue.Text(sheet.GetValue(header, c)).Trim().ToLowerInvariant(), c => c);
            if (!columns.ContainsKey("value")) throw new InvalidDataException("Structured import needs metric_code and value columns.");
            for (var row = header + 1; row <= sheet.MaxRow; row++)
            {
                string Get(string field) => columns.TryGetValue(field, out var col) ? WorkbookValue.Text(sheet.GetValue(row, col)) : "";
                var code = Get("metric_code"); if (code.Length == 0) continue;
                var textMetric = EvidenceMetrics.All.Any(m => m.Code == code && m.Unit == "Text");
                var valueColumn = textMetric && columns.ContainsKey("text_value") ? columns["text_value"] : columns["value"];
                var locator = sheet.Name + "!" + XlsxAddress.ToCellReference(row, valueColumn);
                if (s.Facts.Any(f => f.DocumentVersionId == version.Id && f.Locator == locator && f.MetricCode == code)) { skipped++; continue; }
                var f = new EvidenceFact { TenantId = doc.TenantId, DocumentVersionId = version.Id, MetricCode = code, Value = WorkbookValue.Decimal(Get("value")), TextValue = Get("text_value"),
                    Unit = Get("unit"), Currency = Get("currency"), Period = Get("period"), AsOfDate = Get("as_of_date"), Basis = Get("basis"), PropertyId = Get("property_id"), Locator = locator,
                    Method = "Spreadsheet import", Certainty = "Client-reported", Reason = "Imported candidate; analyst validation required.", Operator = u.Id, OriginalValue = textMetric ? Get("text_value") : Get("value") };
                try { ValidateFact(s, u, doc.TenantId, f); }
                catch (InvalidDataException)
                { s.Issues.Add(new() { TenantId = doc.TenantId, ScopeId = doc.ScopeId, VersionId = version.Id, Kind = "Import validation", Owner = u.Id,
                    Question = $"Review {sheet.Name} row {row}: metric, value, unit, period or reporting basis is missing or unsupported. No zero or corrected context was inferred." }); continue; }
                s.Facts.Add(f); AddContradictions(s, u, f); inserted++;
            }
        }
        if (inserted == 0 && skipped == 0)
        {
            var source = new IntakeSource { Id = version.Id }; var candidates = new List<IntakeCandidate>(); extractor.Extract(snapshot, source, candidates);
            foreach (var candidate in candidates.Where(c => c.SuggestedCode.Length > 0 && WorkbookValue.Decimal(c.RawText).HasValue && EvidenceMetrics.All.Any(m => m.Code == c.SuggestedCode)))
            {
                var locator = candidate.Sheet + "!" + candidate.Cell;
                if (s.Facts.Any(f => f.DocumentVersionId == version.Id && f.Locator == locator)) { skipped++; continue; }
                var f = new EvidenceFact { TenantId = doc.TenantId, DocumentVersionId = version.Id, MetricCode = candidate.SuggestedCode, Value = WorkbookValue.Decimal(candidate.RawText),
                    Unit = "Unconfirmed", Period = candidate.RawYear, Basis = "Unconfirmed", Locator = locator, Method = "Spreadsheet proposal", Certainty = "Client-reported", Operator = u.Id,
                    OriginalValue = candidate.RawText, Reason = "Layout-based proposal; confirm property, unit, reporting basis and source cache before correction/verification." };
                s.Facts.Add(f); AddContradictions(s, u, f); inserted++;
            }
        }
        Audit(s, u, doc.TenantId, "Spreadsheet candidates imported", version.Id, inserted.ToString(CultureInfo.InvariantCulture));
        return (object)new { Inserted = inserted, Skipped = skipped, State = "Unreviewed", Unit = unitDefault, Sheets = snapshot.Sheets.Keys.ToArray() };
    }, ct);
    private byte[] ReadBlob(EvidenceDocument doc, EvidenceVersion version)
    {
        if (version.DeletedAt is not null || version.Processing != "Accepted" || doc.SourceMode != "TemporaryUpload") throw new InvalidDataException("Source is unavailable or has not cleared screening.");
        var path = BlobPath(doc.TenantId, version.Hash); if (!File.Exists(path)) throw new InvalidDataException("Source copy is unavailable. Use the agreed client retrieval process; snapshots remain retained.");
        var bytes = security.Decrypt(File.ReadAllBytes(path), "source:" + doc.TenantId + ":" + version.Hash);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != version.Hash) throw new InvalidDataException("Source identity check failed."); return bytes;
    }
    public Task<EvidenceIssue> AddIssue(EvidenceUser actor, string tenant, EvidenceIssue input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); var t = Tenant(s, u, tenant);
        if (input.VersionId.Length > 0) { var d = Document(s, u, Version(s, u, input.VersionId).DocumentId); if (d.TenantId != tenant) throw new EvidenceDeniedException(); input.ScopeId = d.ScopeId; }
        ScopeAllowed(t, u, input.ScopeId);
        if (input.FactId.Length > 0) { var f = s.Facts.SingleOrDefault(f => f.Id == input.FactId && f.TenantId == tenant) ?? throw new EvidenceDeniedException(); FactDocument(s, u, f); }
        if (string.IsNullOrWhiteSpace(input.Question) || input.Severity is not ("Material" or "Advisory")) throw new InvalidDataException("Question and severity required.");
        var issue = new EvidenceIssue { TenantId = tenant, ScopeId = input.ScopeId, VersionId = input.VersionId, FactId = input.FactId, Kind = input.Kind, Severity = input.Severity, Owner = input.Owner, DueDate = input.DueDate, Question = input.Question };
        s.Issues.Add(issue); Audit(s, u, tenant, "Clarification requested", issue.Id); return Clone(issue);
    }, ct);
    public Task<bool> RespondIssue(EvidenceUser actor, string issueId, string response, bool resolve, CancellationToken ct, string availability = "Not stated") => Transaction(s =>
    { var u = Actor(s, actor); var issue = s.Issues.SingleOrDefault(i => i.Id == issueId) ?? throw new EvidenceDeniedException(); Tenant(s, u, issue.TenantId);
        if (issue.VersionId.Length > 0) Version(s, u, issue.VersionId); else if (!Scope(u, issue.TenantId, issue.ScopeId)) throw new EvidenceDeniedException();
        if (string.IsNullOrWhiteSpace(response)) throw new InvalidDataException("Response or resolution required.");
        if (!new[] { "Not stated", "Not available", "Not applicable", "Will provide later", "Provided", "Clarification required" }.Contains(availability)) throw new InvalidDataException("Choose a permitted evidence availability state.");
        if (resolve) { NeedStaff(u); issue.Status = "Resolved"; issue.Resolution = response; } else { issue.Response = response; issue.Availability = availability; }
        Audit(s, u, issue.TenantId, resolve ? "Issue resolved" : "Client response recorded", issue.Id); return true;
    }, ct);
    public Task<EvidenceSnapshot> Freeze(EvidenceUser actor, string tenant, EvidenceSnapshot input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); var t = Tenant(s, u, tenant);
        if (!DateOnly.TryParseExact(input.Cutoff, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || string.IsNullOrWhiteSpace(input.ModelVersion) || string.IsNullOrWhiteSpace(input.ReportVersion) || string.IsNullOrWhiteSpace(input.ScopeDescription)) throw new InvalidDataException("Cut-off, model/report version and scope description required.");
        if (!t.Policy.ClientPreservesExactVersions || string.IsNullOrWhiteSpace(t.Policy.ClientSourceLocation) || string.IsNullOrWhiteSpace(t.Policy.AgreementReference)) throw new InvalidDataException("Record the agreed exact-version client preservation and retrieval protocol before freezing.");
        var selectedIds = input.Facts.Select(f => f.Id).Distinct().ToArray();
        if (selectedIds.Length == 0) throw new InvalidDataException("Select verified facts for the snapshot.");
        var selected = selectedIds.Select(id => s.Facts.SingleOrDefault(f => f.Id == id && f.TenantId == tenant) ?? throw new EvidenceDeniedException()).ToArray();
        var selectedScopes = selected.Select(f => FactDocument(s, u, f).ScopeId).Distinct().ToArray();
        foreach (var f in selected) { FactDocument(s, u, f); ValidateFact(s, u, tenant, f); if (f.Status != "Verified") throw new InvalidDataException("Only verified facts enter a validated snapshot."); }
        if (selected.GroupBy(FactKey).Any(g => g.Count() > 1)) throw new InvalidDataException("Select one verified fact per reporting key.");
        var versions = selected.Select(f => Version(s, u, f.DocumentVersionId)).DistinctBy(v => v.Id).ToArray();
        if (versions.Any(v => v.Processing != "Accepted" || v.DeletedAt is not null)) throw new InvalidDataException("New snapshots require available, screened source versions.");
        if (s.Issues.Any(i => i.TenantId == tenant && i.Status == "Open" && i.Severity == "Material" && (selected.Any(f => f.Id == i.FactId) || versions.Any(v => v.Id == i.VersionId)))) throw new InvalidDataException("Resolve affected material exceptions before snapshot freeze.");
        if (s.Issues.Any(i => i.TenantId == tenant && i.Status == "Open" && i.Severity == "Material" && i.VersionId.Length == 0 && selectedScopes.Contains(i.ScopeId))) throw new InvalidDataException("Resolve the selected scope's material evidence requests before freezing its baseline.");
        var snap = new EvidenceSnapshot { TenantId = tenant, Cutoff = input.Cutoff, ModelVersion = input.ModelVersion, Assumptions = input.Assumptions, ReportVersion = input.ReportVersion, ReportText = input.ReportText,
            ScopeDescription = input.ScopeDescription, RepresentativeSample = t.RepresentativeSample, Facts = Clone(selected), Manifest = Clone(versions), Documents = Clone(versions.Select(v => Document(s, u, v.DocumentId)).DistinctBy(d => d.Id).ToArray()),
            Exceptions = Clone(s.Issues.Where(i => i.TenantId == tenant && (versions.Any(v => v.Id == i.VersionId) || selected.Any(f => f.Id == i.FactId) || (i.VersionId.Length == 0 && selectedScopes.Contains(i.ScopeId)))).ToArray()), CreatedBy = u.Id, Policy = Clone(t.Policy) };
        var folder = Path.Combine(Root, "snapshots"); Directory.CreateDirectory(folder);
        using (var file = new FileStream(Path.Combine(folder, snap.Id + ".enc"), FileMode.CreateNew)) file.Write(security.Encrypt(JsonSerializer.SerializeToUtf8Bytes(snap, Json), "snapshot:" + snap.Id));
        s.Snapshots.Add(snap); Audit(s, u, tenant, "Analytical snapshot frozen", snap.Id); return Clone(snap);
    }, ct);
    public Task<bool> ValidateSnapshot(EvidenceUser actor, EvidenceValidationRequest input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); if (u.Role != "ClientApprover") throw new EvidenceDeniedException();
        var snap = s.Snapshots.SingleOrDefault(snap => snap.Id == input.SnapshotId) ?? throw new EvidenceDeniedException(); Tenant(s, u, snap.TenantId);
        if (snap.Documents.Any(d => !Scope(u, snap.TenantId, d.ScopeId))) throw new EvidenceDeniedException();
        if (input.Decision is not ("Facts confirmed" or "Corrections requested") || string.IsNullOrWhiteSpace(input.Note)) throw new InvalidDataException("Choose factual confirmation or corrections and record sign-off scope/notes.");
        s.Signoffs.Add(new(snap.Id, u.Id, DateTimeOffset.UtcNow, input.Decision, input.Note, input.Decision == "Facts confirmed" && input.AuthorizePublication));
        if (input.Decision != "Facts confirmed" || !input.AuthorizePublication)
        { if (s.Publications.RemoveAll(p => p.SnapshotId == snap.Id) > 0) Audit(s, u, snap.TenantId, "Client publication permission withdrawn", snap.Id); }
        Audit(s, u, snap.TenantId, "Client factual validation recorded", snap.Id, input.Decision + "; publication permission: " + input.AuthorizePublication); return true;
    }, ct);
    public Task<bool> Publish(EvidenceUser actor, string snapshotId, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedAdmin(u); var snap = s.Snapshots.SingleOrDefault(snap => snap.Id == snapshotId) ?? throw new EvidenceDeniedException(); Tenant(s, u, snap.TenantId);
        if (snap.Documents.Any(d => !Scope(u, snap.TenantId, d.ScopeId))) throw new EvidenceDeniedException();
        var signoff = s.Signoffs.LastOrDefault(x => x.SnapshotId == snap.Id);
        if (signoff?.Decision != "Facts confirmed" || !signoff.AuthorizePublication || string.IsNullOrWhiteSpace(snap.ReportText)) throw new InvalidDataException("Client factual confirmation, explicit publication permission and prepared report text required.");
        if (!s.Publications.Any(p => p.SnapshotId == snap.Id)) s.Publications.Add(new(snap.Id, u.Id, DateTimeOffset.UtcNow));
        Audit(s, u, snap.TenantId, "Report published to client", snap.Id); return true;
    }, ct);
    public Task<object> Snapshot(EvidenceUser actor, string id, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); var snap = s.Snapshots.SingleOrDefault(snap => snap.Id == id) ?? throw new EvidenceDeniedException(); Tenant(s, u, snap.TenantId);
        if (snap.Documents.Any(d => !Scope(u, snap.TenantId, d.ScopeId))) throw new EvidenceDeniedException(); Audit(s, u, snap.TenantId, "Internal snapshot accessed", snap.Id); return (object)Clone(snap);
    }, ct);
    public static string CsvCell(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is string && text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    public Task<byte[]> ExportFacts(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant);
        var rows = new List<string> { "fact_id,tenant_id,property_id,metric_code,value,text_value,unit,currency,period,as_of_date,basis,evidence_version_id,source_locator,method,status,source_retention" };
        foreach (var f in s.Facts.Where(f => f.TenantId == tenant && f.Status == "Verified"))
        { FactDocument(s, u, f); var v = Version(s, u, f.DocumentVersionId);
            if (v.Processing != "Accepted" || s.Issues.Any(i => i.TenantId == tenant && i.Status == "Open" && i.Severity == "Material" && (i.FactId == f.Id || i.VersionId == v.Id))) continue;
            rows.Add(string.Join(',', new object?[] { f.Id, tenant, f.PropertyId, f.MetricCode, f.Value, f.TextValue, f.Unit, f.Currency, f.Period, f.AsOfDate, f.Basis, f.DocumentVersionId, f.Locator, f.Method, f.Status, v.Retention }.Select(CsvCell))); }
        Audit(s, u, tenant, "Validated facts exported internally", tenant); return Encoding.UTF8.GetBytes(string.Join("\r\n", rows));
    }, ct);
    public Task<object> AuditLog(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedTenantAdmin(u, tenant); Tenant(s, u, tenant); var events = s.Audit.Where(e => e.TenantId == tenant).ToArray(); Audit(s, u, tenant, "Audit register accessed", tenant); return (object)events; }, ct);
}
