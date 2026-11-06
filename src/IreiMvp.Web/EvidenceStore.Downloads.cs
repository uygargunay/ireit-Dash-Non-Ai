using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IreiMvp.Web;
public sealed record EvidenceDownloadRequest(string TenantId, string[] VersionIds, string CategoryId, string PropertyId, string ReportingPeriod, bool Archive);
public sealed record EvidenceDownloadPayload(string TenantId, bool Archive, EvidenceDocument[] Documents, EvidenceVersion[] Versions);
public sealed partial class EvidenceStore
{
    public Task<object> PrepareDownload(EvidenceUser actor, EvidenceDownloadRequest input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); Tenant(s, u, input.TenantId);
        if (input.Archive) NeedStaff(u);
        else if (input.VersionIds.Length != 1) throw new EvidenceDeniedException();
        var docs = s.Documents.Where(d => d.TenantId == input.TenantId && Scope(u, d.TenantId, d.ScopeId) &&
            (input.CategoryId.Length == 0 || d.CategoryIds.Contains(input.CategoryId)) && (input.PropertyId.Length == 0 || d.PropertyIds.Contains(input.PropertyId))).ToArray();
        var versions = s.Versions.Where(v => docs.Any(d => d.Id == v.DocumentId) && (input.VersionIds.Length == 0 || input.VersionIds.Contains(v.Id)) &&
            (input.ReportingPeriod.Length == 0 || (v.ReportingPeriod.Length > 0 ? v.ReportingPeriod.Split(',', StringSplitOptions.TrimEntries).Contains(input.ReportingPeriod) : docs.Single(d => d.Id == v.DocumentId).ReportingPeriods.Contains(input.ReportingPeriod)))).ToArray();
        if (input.VersionIds.Any(id => !versions.Any(v => v.Id == id))) throw new EvidenceDeniedException();
        if (versions.Length == 0) throw new InvalidDataException("No matching source versions.");
        if (versions.Length > 1000) throw new InvalidDataException("Narrow the selection to at most 1,000 source versions.");
        if (versions.Any(v => v.Processing != "Accepted" || v.DeletedAt is not null || !File.Exists(BlobPath(input.TenantId, v.Hash)))) throw new InvalidDataException("Exclude quarantined, rejected, deleted or unavailable/client-held references from downloads.");
        var parts = new List<object>(); var selection = new List<string>(); long size = 0;
        void AddPart()
        { if (selection.Count == 0) return; var token = EvidenceSecurity.Token(); var expires = DateTimeOffset.UtcNow.AddMinutes(2);
            s.Downloads.Add(new(EvidenceSecurity.Hash(token), u.Id, input.TenantId, selection.ToArray(), expires, input.Archive)); parts.Add(new { Part = parts.Count + 1, Token = token, Expires = expires, Count = selection.Count, Bytes = size }); selection.Clear(); size = 0; }
        foreach (var v in versions)
        { if (selection.Count >= 20 || size + v.Size > 40 * 1024 * 1024) AddPart(); selection.Add(v.Id); size += v.Size; }
        AddPart(); Audit(s, u, input.TenantId, "Scoped download prepared", input.TenantId, input.Archive ? "Numbered archive parts; 2-minute single-use authorization" : "Single source download");
        return (object)new { Parts = parts.ToArray(), Archive = input.Archive };
    }, ct);
    public Task<EvidenceDownloadPayload> ConsumeDownload(EvidenceUser actor, string token, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); var grant = s.Downloads.SingleOrDefault(d => d.TokenHash == EvidenceSecurity.Hash(token) && d.UserId == u.Id && d.Expires > DateTimeOffset.UtcNow) ?? throw new EvidenceDeniedException();
        Tenant(s, u, grant.TenantId); if (grant.Archive) NeedStaff(u);
        var versions = grant.VersionIds.Select(id => Version(s, u, id)).ToArray();
        if (versions.Any(v => v.Processing != "Accepted" || v.DeletedAt is not null || !File.Exists(BlobPath(grant.TenantId, v.Hash)))) throw new InvalidDataException("Source access changed; prepare a new download or use the agreed retrieval process.");
        var docs = versions.Select(v => Document(s, u, v.DocumentId)).DistinctBy(d => d.Id).ToArray();
        s.Downloads.Remove(grant); Audit(s, u, grant.TenantId, grant.Archive ? "Batch source download accessed" : "Source download accessed", versions[0].Id);
        return new EvidenceDownloadPayload(grant.TenantId, grant.Archive, Clone(docs), Clone(versions));
    }, ct);
    public async Task WriteDownload(EvidenceDownloadPayload payload, Stream output, CancellationToken ct)
    {
        if (!payload.Archive)
        { var v = payload.Versions.Single(); var bytes = ReadBlob(payload.Documents.Single(d => d.Id == v.DocumentId), v); await output.WriteAsync(bytes, ct); return; }
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = new StringBuilder("tenant_id,category_ids,property_ids,reporting_period,document_id,version_id,version_number,original_filename,archive_path,uploaded_at,sha256,status\r\n");
        foreach (var v in payload.Versions)
        {
            var d = payload.Documents.Single(d => d.Id == v.DocumentId);
            var safeName = string.Concat(v.Name.Select(c => char.IsControl(c) || c is '/' or '\\' or ':' ? '_' : c));
            var path = v.Id + "/" + safeName; var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            await using (var stream = entry.Open()) await stream.WriteAsync(ReadBlob(d, v), ct);
            manifest.AppendLine(string.Join(',', new object?[] { d.TenantId, string.Join(';', d.CategoryIds), string.Join(';', d.PropertyIds), v.ReportingPeriod.Length > 0 ? v.ReportingPeriod : d.ReportingPeriod, d.Id, v.Id, v.Number, v.Name, path, v.UploadedAt.ToString("O"), v.Hash, v.Validation }.Select(CsvCell)));
        }
        await using var manifestStream = archive.CreateEntry("manifest.csv").Open();
        await manifestStream.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToString()), ct);
    }
    public Task<byte[]> RegisterCsv(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedStaff(u); Tenant(s, u, tenant);
        var rows = new List<string> { "tenant_id,document_id,version_id,categories,property_ids,period,filename,size,mime,sha256,processing,validation,retention,uploaded_at,source_mode" };
        foreach (var d in s.Documents.Where(d => d.TenantId == tenant && Scope(u, tenant, d.ScopeId)))
        foreach (var v in s.Versions.Where(v => v.DocumentId == d.Id))
            rows.Add(string.Join(',', new object?[] { tenant, d.Id, v.Id, string.Join(';', d.CategoryIds), string.Join(';', d.PropertyIds), v.ReportingPeriod.Length > 0 ? v.ReportingPeriod : d.ReportingPeriod, v.Name, v.Size, v.Mime, v.Hash, v.Processing, v.Validation, v.Retention, v.UploadedAt.ToString("O"), d.SourceMode }.Select(CsvCell)));
        Audit(s, u, tenant, "Evidence register exported", tenant); return Encoding.UTF8.GetBytes(string.Join("\r\n", rows));
    }, ct);
    public Task<object> Rescan(EvidenceUser actor, string versionId, CancellationToken ct) => Transaction(async s =>
    {
        var u = Actor(s, actor); NeedAdmin(u); var v = Version(s, u, versionId); var d = Document(s, u, v.DocumentId);
        if (v.DeletedAt is not null || d.SourceMode != "TemporaryUpload") throw new InvalidDataException("No temporary source copy available to scan.");
        var bytes = security.Decrypt(await File.ReadAllBytesAsync(BlobPath(d.TenantId, v.Hash), ct), "source:" + d.TenantId + ":" + v.Hash);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != v.Hash) throw new InvalidDataException("Source identity check failed.");
        var outcome = await files.Inspect(bytes, v.Name, ct); v.Processing = outcome.Processing; v.Message = outcome.Message;
        Audit(s, u, d.TenantId, "Source rescreened", v.Id, v.Processing); return (object)new { v.Id, v.Processing, v.Message };
    }, ct);
}
