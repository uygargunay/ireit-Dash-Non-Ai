using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

namespace IreiMvp.Web;

public sealed class IntakeStore(IntakeExtractor extractor, IntakeExporter exporter, V4Store assessments, IWebHostEnvironment env)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private string Folder(string id) => Guid.TryParseExact(id, "N", out _) ? Path.Combine(env.ContentRootPath, "App_Data", "Intake", id) : throw new InvalidDataException("Invalid batch ID.");
    public static bool ValidYear(string year) => Regex.IsMatch(year, @"^\d{4}-\d{4}$") && int.Parse(year[5..]) == int.Parse(year[..4]) + 1;
    public async Task<IntakeBatch> Create(IntakeBatch b, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(b.OrganizationId) || string.IsNullOrWhiteSpace(b.OrganizationName) || string.IsNullOrWhiteSpace(b.AssessmentId) ||
            !ValidYear(b.CurrentYear) || b.Currency is not ("CAD" or "USD") || b.FiscalStartMonth is < 1 or > 12)
            throw new InvalidDataException("Organization, assessment, CAD/USD currency, fiscal calendar and consecutive fiscal-year label are required.");
        // Do not accept client-supplied records or runtime assessment IDs at creation.
        var clean = new IntakeBatch { OrganizationId = b.OrganizationId, OrganizationName = b.OrganizationName, AssessmentId = b.AssessmentId,
            Currency = b.Currency, CurrentYear = b.CurrentYear, FiscalStartMonth = b.FiscalStartMonth };
        Directory.CreateDirectory(Folder(clean.Id)); await Save(clean, ct); return clean;
    }
    public async Task<IntakeBatch> Get(string id, CancellationToken ct) => JsonSerializer.Deserialize<IntakeBatch>(
        await File.ReadAllTextAsync(Path.Combine(Folder(id), "batch.json"), ct), Json) ?? throw new InvalidDataException("Batch not found.");
    private async Task Save(IntakeBatch b, CancellationToken ct)
    {
        var folder = Folder(b.Id); var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(b, Json), ct); File.Move(temp, Path.Combine(folder, "batch.json"), true);
    }
    public async Task<IntakeBatch> Upload(string id, Stream stream, string name, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var b = await Get(id, ct); var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext is not (".xlsx" or ".csv")) throw new InvalidDataException("This staff demonstration supports XLSX and UTF-8 comma-delimited CSV only. Convert or use manual review for other formats.");
            var s = new IntakeSource { Name = Path.GetFileName(name) }; var path = Path.Combine(Folder(id), s.Id + ext);
            try
            {
                await using (var output = File.Create(path))
                {
                    var buffer = new byte[81920]; long bytes = 0; int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) > 0) { bytes += read; if (bytes > 20 * 1024 * 1024) throw new InvalidDataException("File exceeds 20 MB demonstration limit."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
                }
                using (var input = File.OpenRead(path)) s.Sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
                if (b.Sources.Any(x => x.Sha256 == s.Sha256)) { File.Delete(path); return b; }
                var timer = Stopwatch.StartNew(); var extracted = new List<IntakeCandidate>();
                try { extractor.Extract(extractor.Read(path, s.Name), s, extracted); b.Candidates.AddRange(extracted); }
                catch (Exception e) when (e is InvalidDataException or IOException or FormatException or System.Xml.XmlException or DecoderFallbackException)
                { s.Status = "Failed; manual review or conversion required"; s.Error = e.Message; }
                timer.Stop(); s.ProcessingMilliseconds = timer.Elapsed.TotalMilliseconds;
                b.Sources.Add(s); b.Version++; await Save(b, ct); return b;
            }
            catch { File.Delete(path); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<IntakeBatch> Review(string id, string candidateId, IntakeReview r, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var b = await Get(id, ct); if (b.Version != r.ExpectedVersion) throw new InvalidDataException("Batch changed; reload before review.");
            var c = b.Candidates.SingleOrDefault(x => x.Id == candidateId) ?? throw new InvalidDataException("Candidate not found.");
            var prior = JsonSerializer.Serialize(c);
            if (c.State is "Accepted" or "Superseded") throw new InvalidDataException("Reviewed facts are immutable. Accept a replacement candidate with an explicit supersedes link.");
            if (r.State is not ("Accepted" or "Rejected") || string.IsNullOrWhiteSpace(r.Reviewer) || string.IsNullOrWhiteSpace(r.Rationale))
                throw new InvalidDataException("Accept/reject, reviewer and reason are required.");
            if (r.State == "Accepted")
            {
                if (!IntakeExtractor.Codes.Contains(r.Code) || !ValidYear(r.Year) || r.Scenario is not ("Actual" or "Budget" or "Projection") ||
                    r.Grain is not ("Portfolio control" or "Property detail" or "Project detail") ||
                    (r.Scope == "PORTFOLIO") != (r.Grain == "Portfolio control") || !Regex.IsMatch(r.Scope, @"^[A-Za-z0-9_-]{1,100}$") ||
                    !r.Value.HasValue || r.Certainty is not ("Confirmed" or "Projected" or "Placeholder"))
                    throw new InvalidDataException("Confirm a supported code, stable scope, period, scenario, grain, amount and certainty. Units/basis/conversion must be explained in the reason.");
                if (c.ValueState is "Formula error" or "Missing formula cache" or "Missing" or "Text evidence")
                    throw new InvalidDataException("This observation has no usable numeric source; obtain a corrected source. Notes/dates cannot become financial facts.");
                var source = b.Sources.Single(s => s.Id == c.SourceId);
                var snapshot = extractor.Read(SourcePath(id, source), source.Name);
                if (!snapshot.Sheets.TryGetValue(c.Sheet, out var sheet) || !sheet.Cells.ContainsKey(c.Cell)) throw new InvalidDataException("Valid source locator required.");
                if ((r.Code == "HOMES_TOTAL" && (r.Value < 0 || decimal.Truncate(r.Value.Value) != r.Value)) || (r.Code.StartsWith("DEBT_") && r.Value < 0))
                    throw new InvalidDataException("Homes must be nonnegative integers; debt sign issues require source reconciliation.");
                var active = b.Candidates.Where(x => x.State == "Accepted" && x.Scope == r.Scope && x.Year == r.Year && x.Scenario == r.Scenario && x.Code == r.Code).ToArray();
                if (active.Length > 0 && (active.Length != 1 || active[0].Id != r.SupersedesId)) throw new InvalidDataException("Conflicting active fact: explicitly select the candidate being superseded.");
                if (r.SupersedesId.Length > 0 && !active.Any(x => x.Id == r.SupersedesId)) throw new InvalidDataException("Replacement must match the full supported fact key.");
                if (active.Length == 1) { var old = active[0]; var before = JsonSerializer.Serialize(old); old.State = "Superseded";
                    b.Reviews.Add(new(old.Id, r.Reviewer, DateTimeOffset.UtcNow, before, JsonSerializer.Serialize(old), r.Rationale)); }
                c.Code = r.Code; c.Scope = r.Scope; c.Year = r.Year; c.Scenario = r.Scenario; c.Grain = r.Grain; c.Value = r.Value; c.Certainty = r.Certainty; c.SupersedesId = r.SupersedesId;
            }
            c.State = r.State; c.Rationale = r.Rationale;
            b.Reviews.Add(new(c.Id, r.Reviewer, DateTimeOffset.UtcNow, prior, JsonSerializer.Serialize(c), r.Rationale));
            b.Version++; await Save(b, ct); return b;
        }
        finally { gate.Release(); }
    }
    public string SourcePath(string id, IntakeSource s) => Path.Combine(Folder(id), s.Id + Path.GetExtension(s.Name).ToLowerInvariant());
    public async Task<object> Preview(string id, string sourceId, string sheet, int row, int column, CancellationToken ct)
    {
        var b = await Get(id, ct); var s = b.Sources.SingleOrDefault(s => s.Id == sourceId) ?? throw new InvalidDataException("Source not found.");
        var w = extractor.Read(SourcePath(id, s), s.Name); if (!w.Sheets.TryGetValue(sheet, out var tab)) throw new InvalidDataException("Sheet not found.");
        row = Math.Max(1, row); column = Math.Max(1, column);
        return new { s.Name, Sheet = sheet, Cells = tab.Cells.Where(p => { var (r, c) = XlsxAddress.Parse(p.Key); return r >= row && r < row + 15 && c >= column && c < column + 12; })
            .Select(p => new { Cell = p.Key, Value = WorkbookValue.Text(p.Value.Value), p.Value.Formula }).ToArray() };
    }
    public async Task<IntakeBatch> AddEffort(string id, IntakeEffort entry, CancellationToken ct)
    {
        if (entry.Role is not ("Analyst" or "Senior underwriter" or "Founder" or "IT") || string.IsNullOrWhiteSpace(entry.Task) || entry.Minutes < 0 || entry.MachineCost < 0 ||
            !Regex.IsMatch(entry.CostCurrency, "^[A-Z]{3}$")) throw new InvalidDataException("Role, task, nonnegative minutes/cost and cost currency required.");
        await gate.WaitAsync(ct); try { var b = await Get(id, ct); b.Effort.Add(entry); b.Version++; await Save(b, ct); return b; } finally { gate.Release(); }
    }
    public async Task<IntakeBatch> Commit(string id, int expectedVersion, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var b = await Get(id, ct); if (b.ExportedVersion == b.Version && b.AssessmentRuntimeIds.Count > 0) return b;
            if (b.Version != expectedVersion) throw new InvalidDataException("Batch changed; review the latest selection.");
            var path = Path.Combine(Folder(id), "reviewed.xlsx"); exporter.Export(b, path);
            await using var stream = File.OpenRead(path);
            var prior = b.AssessmentRuntimeIds.LastOrDefault();
            var a = await assessments.ImportAsync(stream, "reviewed-intake.xlsx", ct, prior);
            b.AssessmentRuntimeIds.Add(a.Id); b.ExportedVersion = b.Version; await Save(b, ct); return b;
        }
        finally { gate.Release(); }
    }
    public string ExportPath(string id) => Path.Combine(Folder(id), "reviewed.xlsx");
}
