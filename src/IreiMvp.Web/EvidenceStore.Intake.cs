using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;
public sealed partial class EvidenceStore
{
    private object ImportPdf(EvidenceState s, EvidenceUser u, EvidenceDocument doc, EvidenceVersion version)
    {
        var parsed = EvidencePdfExtractor.Read(ReadBlob(doc, version)); var inserted = 0; var skipped = 0;
        foreach (var p in parsed.Facts)
        {
            if (s.Facts.Any(f => f.DocumentVersionId == version.Id && f.Locator == p.Locator && f.MetricCode == p.Code)) { skipped++; continue; }
            var f = new EvidenceFact { TenantId = doc.TenantId, DocumentVersionId = version.Id, MetricCode = p.Code, Value = p.Value, Unit = "Unconfirmed", Period = p.Period,
                Basis = "Unconfirmed", Locator = p.Locator, SourceLabel = p.Label, Method = "PDF text proposal", Certainty = "Client-reported", Operator = u.Id,
                OriginalValue = p.Value.ToString(CultureInfo.InvariantCulture), Reason = "Text-PDF candidate; confirm scale, context and source alignment." };
            s.Facts.Add(f); AddContradictions(s, u, f); inserted++;
        }
        if (s.Facts.Count(f => f.TenantId == doc.TenantId) > 100000) throw new InvalidDataException("Fact capacity reached.");
        version.Extraction = new() { State = parsed.Pages.All(p => p.Text.Trim().Length < 30) ? "OCR required" : inserted + skipped > 0 ? "Extracted" : "Text available; mapping needed",
            Proposed = inserted + skipped, Sections = parsed.Pages.Select(p => $"page {p.Page}").ToArray(), Warnings = parsed.Warnings };
        Audit(s, u, doc.TenantId, "PDF candidates extracted", version.Id, inserted.ToString(CultureInfo.InvariantCulture));
        return new { Inserted = inserted, Skipped = skipped, State = "Unreviewed", Sheets = version.Extraction.Sections, Warnings = parsed.Warnings };
    }
    public Task<bool> ExtractionFailed(EvidenceUser actor, string versionId, string message, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedStaff(u); var v = Version(s, u, versionId); var d = Document(s, u, v.DocumentId);
        if (v.Processing != "Accepted" || v.DeletedAt is not null) return false;
        v.Extraction = new() { State = "Extraction failed", Warnings = [message] }; Audit(s, u, d.TenantId, "Extraction failed; source retained", v.Id); return true; }, ct);
    public Task<object> PdfText(EvidenceUser actor, string versionId, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedStaff(u); var v = Version(s, u, versionId); var d = Document(s, u, v.DocumentId);
        if (v.Extension != ".pdf") throw new InvalidDataException("Choose a PDF.");
        var parsed = EvidencePdfExtractor.Read(ReadBlob(d, v)); Audit(s, u, d.TenantId, "Extracted PDF text viewed", v.Id);
        return (object)new { parsed.Pages, parsed.Warnings }; }, ct);
    public Task<object> BatchReview(EvidenceUser actor, string tenant, EvidenceBatchReview input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant);
        if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Reason) || input.FactIds.Length is < 1 or > 200 || input.FactIds.Distinct().Count() != input.FactIds.Length)
            throw new InvalidDataException("Select 1–200 facts and explicitly confirm the review with a rationale.");
        foreach (var id in input.FactIds)
        { var f = s.Facts.SingleOrDefault(f => f.Id == id && f.TenantId == tenant) ?? throw new EvidenceDeniedException(); FactDocument(s, u, f); }
        foreach (var id in input.FactIds) ReviewFactCore(s, u, id, new(input.Status, input.Reason));
        Audit(s, u, tenant, "Selected facts reviewed together", tenant, $"{input.FactIds.Length}: {input.Reason}"); return (object)new { Reviewed = input.FactIds.Length };
    }, ct);
    public Task<object> BatchContext(EvidenceUser actor, string tenant, EvidenceBatchContext input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant);
        if (!input.Confirmed || input.FactIds.Length is < 1 or > 200 || input.FactIds.Distinct().Count() != input.FactIds.Length || string.IsNullOrWhiteSpace(input.Reason)
            || string.IsNullOrWhiteSpace(input.Basis) || input.Basis == "Unconfirmed" || !Regex.IsMatch(input.Period, @"^(?:FY)?20\d{2}(?:[-–/]20\d{2})?$", RegexOptions.IgnoreCase)
            || input.Scale is not (1 or 1000 or 1000000)) throw new InvalidDataException("Confirm the selected facts, period, basis, source scale and rationale. Select up to 200 facts.");
        var facts = input.FactIds.Select(id => s.Facts.SingleOrDefault(f => f.Id == id && f.TenantId == tenant) ?? throw new EvidenceDeniedException()).ToArray();
        if (facts.Select(f => f.SuggestedProperty).Distinct().Count() > 1 || facts.Select(f => f.DocumentVersionId).Distinct().Count() > 1 || facts.Select(f => f.Period).Distinct().Count() > 1) throw new InvalidDataException("Filter to one source file, proposed property/sheet and source period before confirming a shared context.");
        foreach (var prior in facts)
        {
            var doc = FactDocument(s, u, prior); if (prior.Status is "Verified" or "Rejected" or "Superseded") throw new InvalidDataException("Choose current unverified candidates only.");
            if (Version(s, u, prior.DocumentVersionId).Processing != "Accepted") throw new InvalidDataException("Source is not available for review.");
            var f = Clone(prior); f.Id = Guid.NewGuid().ToString("N"); f.CreatedAt = DateTimeOffset.UtcNow; f.SupersedesId = prior.Id; f.Status = "Unreviewed";
            f.PropertyId = input.PropertyId; f.SuggestedProperty = ""; f.Period = input.Period; f.Basis = input.Basis; f.Operator = u.Id; f.Reason = input.Reason + $"; explicit source scale {input.Scale}";
            var metric = EvidenceMetrics.All.Single(m => m.Code == f.MetricCode); f.Unit = metric.Unit; f.Currency = metric.Unit == "Currency" ? input.Currency : "";
            if (f.Value.HasValue && metric.Unit == "Currency") f.Value *= input.Scale;
            ValidateFact(s, u, tenant, f); prior.Status = "Superseded"; s.Facts.Add(f); AddContradictions(s, u, f);
        }
        if (s.Facts.Count(f => f.TenantId == tenant) > 100000) throw new InvalidDataException("Fact capacity reached.");
        Audit(s, u, tenant, "Candidate context confirmed in batch", tenant, $"{facts.Length}: {input.Reason}"); return (object)new { Updated = facts.Length };
    }, ct);
    public Task<byte[]> ExportDraft(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant);
        var rows = new List<string> { "notice,source_file,source_version,proposed_property,metric_code,value,text_value,unit,currency,period,basis,source_locator,status,source_label" };
        foreach (var f in s.Facts.Where(f => f.TenantId == tenant && f.Status is not ("Rejected" or "Superseded")))
        { var d = s.Documents.Single(x => x.Id == s.Versions.Single(v => v.Id == f.DocumentVersionId).DocumentId); if (!Scope(u, tenant, d.ScopeId)) continue;
            var v = Version(s, u, f.DocumentVersionId); if (v.Processing != "Accepted" || v.DeletedAt is not null) continue;
            rows.Add(string.Join(',', new object?[] { "DRAFT - NOT VALIDATED - NOT A V4 WORKBOOK", v.Name, v.Id, f.SuggestedProperty, f.MetricCode, f.Value, f.TextValue, f.Unit, f.Currency, f.Period, f.Basis, f.Locator, f.Status, f.SourceLabel }.Select(CsvCell))); }
        if (rows.Count == 1) throw new InvalidDataException("No candidate facts yet. Extract a supported document first.");
        Audit(s, u, tenant, "Draft evidence preview exported", tenant); return Encoding.UTF8.GetBytes(string.Join("\r\n", rows));
    }, ct);
}

public sealed record EvidenceBatchContext(string[] FactIds, string PropertyId, string Period, string Basis, string Currency, decimal Scale, string Reason, bool Confirmed);
