using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IreiMvp.Web;
public sealed partial class EvidenceStore
{
    public Task<object> Workspace(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var t = Tenant(s, u, tenant);
        var docs = s.Documents.Where(d => d.TenantId == tenant && Scope(u, tenant, d.ScopeId)).ToArray();
        var versions = s.Versions.Where(v => docs.Any(d => d.Id == v.DocumentId)).ToArray();
        var properties = s.Properties.Where(p => p.TenantId == tenant && Scope(u, tenant, p.ScopeId)).ToArray();
        var visibleSnapshots = s.Snapshots.Where(snap => snap.TenantId == tenant && snap.Documents.All(d => Scope(u, tenant, d.ScopeId))).ToArray();
        var snapshots = visibleSnapshots.Select(snap => new {
            snap.Id, snap.Cutoff, snap.CreatedAt, snap.ScopeDescription, snap.RepresentativeSample,
            Signoffs = s.Signoffs.Where(x => x.SnapshotId == snap.Id).Select(x => new { x.Decision, x.Date, x.Note, x.AuthorizePublication }),
            Published = s.Publications.Any(p => p.SnapshotId == snap.Id),
            FactualSummary = snap.Facts.Select(f => new { f.Id, f.PropertyId, f.MetricCode, f.Value, f.TextValue, f.Unit, f.Currency, f.Period, f.AsOfDate, f.Basis, f.Certainty, f.Locator, f.DocumentVersionId }),
            Report = s.Publications.Any(p => p.SnapshotId == snap.Id) ? snap.ReportText : null
        }).ToArray();
        var issues = s.Issues.Where(i => i.TenantId == tenant && (i.VersionId.Length > 0 ? versions.Any(v => v.Id == i.VersionId) : Scope(u, tenant, i.ScopeId))).ToArray();
        var result = new {
            Organization = new { t.Id, t.Name, t.PilotScope, t.ReportingCutoff, t.RepresentativeSample, t.FiscalYearEnd, t.Contact,
                Scopes = t.ScopeIds.Where(sc => Scope(u, tenant, sc)).ToArray(), t.Policy },
            Categories = EvidenceCategories.All.Select(c => new { c.Id, c.Title, c.Description, c.Examples, c.Priority,
                States = t.CategoryStates.Where(st => st.CategoryId == c.Id && Scope(u, tenant, st.ScopeId)),
                FileCount = versions.Count(v => docs.Any(d => d.Id == v.DocumentId && d.CategoryIds.Contains(c.Id))),
                Bytes = versions.Where(v => docs.Any(d => d.Id == v.DocumentId && d.CategoryIds.Contains(c.Id))).Sum(v => v.Size),
                Outstanding = versions.Count(v => v.Validation is not ("Verified" or "Superseded") && docs.Any(d => d.Id == v.DocumentId && d.CategoryIds.Contains(c.Id))),
                LastUpload = versions.Where(v => docs.Any(d => d.Id == v.DocumentId && d.CategoryIds.Contains(c.Id))).Select(v => (DateTimeOffset?)v.UploadedAt).Max()
            }).ToArray(),
            Documents = docs, Versions = versions, Properties = properties,
            SourceAvailability = versions.Select(v => new { v.Id, Available = v.DeletedAt is null && v.Processing == "Accepted" && (docs.Single(d => d.Id == v.DocumentId).SourceMode == "ClientHosted" || File.Exists(BlobPath(tenant, v.Hash))), Note = "A hash identifies a version; retrieval depends on the recorded preservation agreement." }).ToArray(),
            Facts = Staff(u) ? s.Facts.Where(f => f.TenantId == tenant && versions.Any(v => v.Id == f.DocumentVersionId)).ToArray() : [],
            Issues = issues.Select(i => new { i.Id, i.VersionId, i.FactId, i.Kind, i.Severity, i.Owner, i.DueDate, i.Question, i.Status, i.Availability, i.Response, Resolution = Staff(u) ? i.Resolution : null }).ToArray(),
            Snapshots = snapshots,
            Receipts = s.Reservations.Where(r => r.TenantId == tenant && r.UserId == u.Id).Select(r => new { r.Id, r.CategoryIds, r.ReportingPeriod, r.Expires, r.Files }).ToArray(),
            Submissions = t.Submissions.Where(r => Scope(u, tenant, r.ScopeId)).ToArray(),
            Capacity = new { config.FileLimitBytes, config.BatchLimitBytes, config.BatchFileLimit, config.TenantQuotaBytes, UsedBytes = Staff(u) ? (long?)Stored(s, tenant) : null },
            Prototype = config.Prototype,
            Disclosure = "Mode B creates an encrypted temporary IREI-controlled source copy. Original files remain the client's authoritative records. Derived data and snapshots have a separate agreed retention period. No third-party AI/OCR processing is performed."
        };
        Audit(s, u, tenant, "Workspace accessed", tenant); return (object)result;
    }, ct);
    public Task<object> Prepare(EvidenceUser actor, EvidenceReservation input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var t = Tenant(s, u, input.TenantId); ScopeAllowed(t, u, input.ScopeId); Categories(input.CategoryIds); Properties(s, u, t.Id, input.PropertyIds);
        if (input.PropertyIds.Any(id => s.Properties.Single(p => p.Id == id).ScopeId != input.ScopeId)) throw new InvalidDataException("Link properties within the same authorized submission scope.");
        if (!input.ConfirmedNoPersonalData) throw new InvalidDataException("Confirm that uploads exclude unnecessary personal information and contain only material you are authorized to provide.");
        if (input.Coverage is not ("Partial" or "Complete")) throw new InvalidDataException("Select partial or complete coverage.");
        if (!config.Prototype && (!config.InfrastructureSpendBlocked || !t.Policy.WrittenAuthority || !t.Policy.SecurityApproved || !t.Policy.BackupReviewed || t.Policy.ApprovedAt is null || string.IsNullOrWhiteSpace(t.Policy.AgreementReference) || string.IsNullOrWhiteSpace(t.Policy.HostingLocation) || string.IsNullOrWhiteSpace(t.Policy.ProcessorsAndBackups)))
            throw new InvalidDataException("Confidential intake is blocked until hosting, security, authority, backup and retention agreements are recorded. Use approved client-hosted sharing in the interim.");
        if (input.Files.Count is < 1 || input.Files.Count > config.BatchFileLimit || input.Files.Any(f => f.Size <= 0 || f.Size > config.FileLimitBytes || f.Name.Length > 200 || f.Name.Contains('\r') || f.Name.Contains('\n') || f.Name != Path.GetFileName(f.Name) || !EvidenceFiles.Types.ContainsKey(Path.GetExtension(f.Name))))
            throw new InvalidDataException("Check file types, filenames and per-file/batch limits. Split the package or contact support for an approved alternate transfer.");
        var amount = input.Files.Sum(f => f.Size + 29);
        if (input.Files.Sum(f => f.Size) > config.BatchLimitBytes || PhysicalBytes(t.Id) + Reserved(s, t.Id) + amount > config.TenantQuotaBytes || PhysicalBytes() + Reserved(s) + amount + 65536 > config.GlobalQuotaBytes)
            throw new InvalidDataException("Storage capacity or batch limit reached. Use smaller batches or arrange approved client-hosted access; no paid overage is enabled.");
        if (input.ExistingDocumentId is not null) { var d = Document(s, u, input.ExistingDocumentId); if (d.TenantId != t.Id || d.ScopeId != input.ScopeId) throw new EvidenceDeniedException(); if (input.Files.Count != 1) throw new InvalidDataException("Replace one document with one file at a time."); }
        var r = new EvidenceReservation { TenantId = t.Id, UserId = u.Id, ScopeId = input.ScopeId, CategoryIds = input.CategoryIds.Distinct().ToArray(), PropertyIds = input.PropertyIds.Distinct().ToArray(),
            ReportingPeriod = input.ReportingPeriod, DocumentDate = input.DocumentDate, Coverage = input.Coverage, Notes = input.Notes, ExistingDocumentId = input.ExistingDocumentId, DeliberateNewVersion = input.DeliberateNewVersion, ConfirmedNoPersonalData = true,
            Files = input.Files.Select(f => new EvidenceTransfer { Name = f.Name, Size = f.Size }).ToList() };
        s.Reservations.Add(r); Audit(s, u, t.Id, "Upload capacity reserved", r.Id);
        return (object)new { r.Id, r.Expires, r.Files, config.FileLimitBytes, config.BatchLimitBytes };
    }, ct);
    public Task<object> Receive(EvidenceUser actor, string reservation, int index, Stream input, long? length, CancellationToken ct) => Transaction(async s =>
    {
        var u = Actor(s, actor); var r = s.Reservations.SingleOrDefault(x => x.Id == reservation && x.UserId == u.Id && x.Expires > DateTimeOffset.UtcNow) ?? throw new EvidenceDeniedException();
        var t = Tenant(s, u, r.TenantId); ScopeAllowed(t, u, r.ScopeId);
        if (index < 0 || index >= r.Files.Count) throw new EvidenceDeniedException();
        var transfer = r.Files[index]; if (transfer.State is "Uploaded" or "Duplicate" or "Quarantined" or "Rejected") return (object)Clone(transfer);
        if (length != transfer.Size)
        { transfer.State = "Failed"; transfer.Message = "Transfer size does not match the reservation; retry this file with its exact bytes."; Audit(s, u, t.Id, "Transfer failed size check", r.Id); return (object)Clone(transfer); }
        try
        {
            using var memory = new MemoryStream(); var buffer = new byte[81920]; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0) { if (memory.Length + count > transfer.Size) throw new InvalidDataException("Transfer exceeded its reserved limit."); await memory.WriteAsync(buffer.AsMemory(0, count), ct); }
            if (memory.Length != transfer.Size) throw new InvalidDataException("Transfer incomplete; retry this file.");
            var data = memory.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(data));
            var duplicate = s.Versions.FirstOrDefault(v => v.Hash == hash && v.DeletedAt is null && s.Documents.Any(d => d.Id == v.DocumentId && d.TenantId == t.Id && d.ScopeId == r.ScopeId && Scope(u, t.Id, d.ScopeId)));
            if (duplicate is not null && !r.DeliberateNewVersion && r.ExistingDocumentId is null)
            {
                var d = Document(s, u, duplicate.DocumentId); s.DocumentHistory.Add(new(Clone(d), u.Id, DateTimeOffset.UtcNow));
                d.CategoryIds = d.CategoryIds.Concat(r.CategoryIds).Distinct().ToArray(); d.PropertyIds = d.PropertyIds.Concat(r.PropertyIds).Distinct().ToArray();
                transfer.State = "Duplicate"; transfer.DocumentId = d.Id; transfer.VersionId = duplicate.Id; transfer.Message = "Identical evidence already received in your authorized scope; categories/properties were linked without another source copy.";
                Audit(s, u, t.Id, "Duplicate linked", duplicate.Id); return (object)Clone(transfer);
            }
            var inspected = await files.Inspect(data, transfer.Name, ct);
            EvidenceDocument doc;
            if (r.ExistingDocumentId is not null) { doc = Document(s, u, r.ExistingDocumentId); s.DocumentHistory.Add(new(Clone(doc), u.Id, DateTimeOffset.UtcNow)); doc.ReportingPeriod = r.ReportingPeriod; }
            else { doc = new() { TenantId = t.Id, ScopeId = r.ScopeId, Label = transfer.Name, CategoryIds = r.CategoryIds, PropertyIds = r.PropertyIds, ReportingPeriod = r.ReportingPeriod, Coverage = r.Coverage, Notes = r.Notes }; s.Documents.Add(doc); }
            doc.ReportingPeriods = doc.ReportingPeriods.Concat(r.ReportingPeriod.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
            var prior = s.Versions.Where(v => v.DocumentId == doc.Id).OrderByDescending(v => v.Number).FirstOrDefault();
            var version = new EvidenceVersion { DocumentId = doc.Id, BatchId = r.Id, Name = transfer.Name, Size = data.Length, Mime = inspected.Mime, Extension = Path.GetExtension(transfer.Name).ToLowerInvariant(), Hash = hash,
                UploadedBy = u.Id, DocumentDate = r.DocumentDate, ReportingPeriod = r.ReportingPeriod, RevisionReason = r.Notes, AgreementReference = t.Policy.AgreementReference, Number = (prior?.Number ?? 0) + 1, SupersedesId = prior?.Id,
                Processing = inspected.Processing, Message = inspected.Message, RetentionDue = DateTimeOffset.UtcNow.AddDays(t.Policy.SourceRetentionDays) };
            var path = BlobPath(t.Id, hash); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) await File.WriteAllBytesAsync(path, security.Encrypt(data, "source:" + t.Id + ":" + hash), ct);
            s.Versions.Add(version); transfer.VersionId = version.Id; transfer.DocumentId = doc.Id; transfer.State = inspected.Processing == "Accepted" ? "Uploaded" : inspected.Processing; transfer.Message = inspected.Message;
            if (inspected.Processing != "Accepted") s.Issues.Add(new() { TenantId = t.Id, VersionId = version.Id, Kind = "File handling", Question = inspected.Message, Owner = "IREI administrator" });
            Audit(s, u, t.Id, "Source received", version.Id, inspected.Processing); return (object)Clone(transfer);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException)
        { transfer.State = "Failed"; transfer.Message = "Transfer was not completed. Retry this file; previously received files remain saved."; Audit(s, u, t.Id, "Transfer failed", r.Id); return (object)Clone(transfer); }
    }, CancellationToken.None);
    public Task<bool> CancelReservation(EvidenceUser actor, string id, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var r = s.Reservations.SingleOrDefault(r => r.Id == id && r.UserId == u.Id) ?? throw new EvidenceDeniedException();
        r.Expires = DateTimeOffset.UtcNow; foreach (var f in r.Files.Where(f => f.State is "Queued" or "Failed")) f.State = "Cancelled"; Audit(s, u, r.TenantId, "Upload cancelled", r.Id); return true; }, ct);
    public Task<EvidenceDocument> Metadata(EvidenceUser actor, string document, EvidenceMetadata input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var d = Document(s, u, document); Categories(input.CategoryIds); Properties(s, u, d.TenantId, input.PropertyIds);
        if (input.PropertyIds.Any(id => s.Properties.Single(p => p.Id == id).ScopeId != d.ScopeId)) throw new InvalidDataException("Link properties within the document's authorized submission scope.");
        if (input.Coverage is not ("Partial" or "Complete")) throw new InvalidDataException("Choose partial or complete coverage.");
        s.DocumentHistory.Add(new(Clone(d), u.Id, DateTimeOffset.UtcNow)); d.CategoryIds = input.CategoryIds.Distinct().ToArray(); d.PropertyIds = input.PropertyIds.Distinct().ToArray(); d.ReportingPeriod = input.ReportingPeriod; d.ReportingPeriods = input.ReportingPeriod.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); d.Coverage = input.Coverage; d.Notes = input.Notes;
        Audit(s, u, d.TenantId, "Document metadata changed", d.Id); return Clone(d);
    }, ct);
    public Task<bool> CategoryStatus(EvidenceUser actor, string tenant, string category, string scope, EvidenceStatusRequest input, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var t = Tenant(s, u, tenant); ScopeAllowed(t, u, scope); Categories([category]);
        if (input.State is not ("Not started" or "In progress" or "Submitted" or "Not applicable" or "Not available" or "Will provide later" or "Clarification required" or "Review complete") || (input.State == "Review complete" && !Staff(u))) throw new InvalidDataException("Choose a permitted completeness state.");
        t.CategoryStates.RemoveAll(x => x.ScopeId == scope && x.CategoryId == category); t.CategoryStates.Add(new(category, scope, input.State, input.Note, u.Id, DateTimeOffset.UtcNow));
        Audit(s, u, tenant, "Category completeness changed", category, input.State); return true;
    }, ct);
    public Task<bool> Ready(EvidenceUser actor, string tenant, string scope, string note, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var t = Tenant(s, u, tenant); ScopeAllowed(t, u, scope); t.Submissions.Add(new(scope, u.Id, DateTimeOffset.UtcNow, note)); Audit(s, u, tenant, "Ready for IREI review", scope, "Intake completeness only; facts remain unverified."); return true; }, ct);
    public Task<object> RegisterReference(EvidenceUser actor, string tenant, string scope, string category, string reference, string label, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var t = Tenant(s, u, tenant); ScopeAllowed(t, u, scope); Categories([category]);
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback || uri.UserInfo.Length > 0) throw new InvalidDataException("Use an approved HTTPS client-controlled reference. Access tokens and credentials must not be embedded.");
        var d = new EvidenceDocument { TenantId = tenant, ScopeId = scope, Label = label, CategoryIds = [category], SourceMode = "ClientHosted", SourceReference = reference };
        var v = new EvidenceVersion { DocumentId = d.Id, Name = label, Processing = "Reference recorded", Retention = "Client-held original", UploadedBy = u.Id, Message = "No file has been retrieved; connector access and exact-version verification require separate review." };
        s.Documents.Add(d); s.Versions.Add(v); Audit(s, u, tenant, "Client-hosted reference recorded", v.Id); return (object)new { Document = d, Version = v };
    }, ct);
    public Task<bool> MarkUnavailable(EvidenceUser actor, string version, string reason, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); var v = Version(s, u, version); var d = Document(s, u, v.DocumentId);
        // Any user authorized to this source may report an accidental sensitive upload.
        v.Processing = d.SourceMode == "ClientHosted" ? "Access revoked" : "Quarantined"; v.Message = "Source unavailable; contact the organization or IREI support.";
        s.Issues.Add(new() { TenantId = d.TenantId, VersionId = v.Id, Kind = "Source unavailable", Question = reason }); Audit(s, u, d.TenantId, "Source access withdrawn or isolated", v.Id); return true;
    }, ct);
    public Task<bool> ReviewVersion(EvidenceUser actor, string id, EvidenceStatusRequest input, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedStaff(u); var v = Version(s, u, id); var d = Document(s, u, v.DocumentId);
        if (input.State is not ("Verified" or "Qualified" or "Needs clarification") || string.IsNullOrWhiteSpace(input.Note)) throw new InvalidDataException("Context validation state and reason required.");
        if (v.Processing != "Accepted" || v.DeletedAt is not null) throw new InvalidDataException("Source must be available and clear screening before context validation.");
        v.Validation = input.State; Audit(s, u, d.TenantId, "Source context reviewed", v.Id, input.State + ": " + input.Note); return true;
    }, ct);
    public Task<bool> DeleteSource(EvidenceUser actor, string version, EvidenceDeleteRequest input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedAdmin(u); var v = Version(s, u, version); var d = Document(s, u, v.DocumentId); var t = Tenant(s, u, d.TenantId);
        if (!input.Authorized || !input.BackupAndRetrievalReviewed || string.IsNullOrWhiteSpace(input.Reason) || !t.Policy.BackupReviewed || string.IsNullOrWhiteSpace(t.Policy.AgreementReference)) throw new InvalidDataException("Deletion requires documented policy authority, reason and backup/retrieval review.");
        if (s.Facts.Any(f => f.DocumentVersionId == v.Id && f.Status == "Verified" && !s.Snapshots.Any(snap => snap.Facts.Any(saved => saved.Id == f.Id)))) throw new InvalidDataException("Freeze the latest verified factual records before deleting their temporary source copy.");
        if (s.Snapshots.Any(snap => snap.Manifest.Any(m => m.Id == v.Id)) && (!t.Policy.ClientPreservesExactVersions || string.IsNullOrWhiteSpace(t.Policy.ClientSourceLocation))) throw new InvalidDataException("Preserve and document exact-version client retrieval before deleting snapshot evidence.");
        v.DeletedAt = DateTimeOffset.UtcNow; v.Retention = "Deleted temporary copy";
        var otherReferences = s.Versions.Any(other => other.Id != v.Id && other.Hash == v.Hash && other.DeletedAt is null && s.Documents.Any(doc => doc.Id == other.DocumentId && doc.TenantId == d.TenantId));
        if (!otherReferences && v.Hash.Length > 0) File.Delete(BlobPath(t.Id, v.Hash));
        Audit(s, u, t.Id, "Temporary source deletion authorized", v.Id, "Exact source reference retained; snapshots unchanged. Backup disposition remains governed by recorded policy."); return true;
    }, ct);
}
