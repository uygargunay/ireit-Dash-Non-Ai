using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed record BoardSource(string Name, string StoredName, string Sha256, string Kind);
public sealed record BoardCheck(string Id, string Area, string Period, decimal? Expected, decimal? Observed,
    decimal? Difference, string State, string Explanation, string Source);
public sealed class BoardRecord
{
    public V4Assessment Assessment { get; set; } = new();
    public List<BoardSource> Sources { get; set; } = [];
    public List<BoardCheck> Checks { get; set; } = [];
}
public sealed class BoardStore(BoardWorkbookImporter importer, XlsxWorkbookService xlsx,
    IWebHostEnvironment environment, IOptions<IreiOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Root => Path.Combine(Path.IsPathRooted(options.Value.DataRoot) ? options.Value.DataRoot : Path.Combine(environment.ContentRootPath, options.Value.DataRoot), "BoardAssessments");
    private string Folder(string id) => Guid.TryParseExact(id, "N", out _) ? Path.Combine(Root, id) : throw new InvalidDataException("Invalid board assessment ID.");
    public async Task<BoardRecord?> Get(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(Folder(id), "record.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<BoardRecord>(await File.ReadAllTextAsync(path, ct), Json) : null;
    }
    private async Task Save(BoardRecord record, CancellationToken ct)
    {
        var folder = Folder(record.Assessment.Id); Directory.CreateDirectory(Path.Combine(folder, "Versions"));
        var body = JsonSerializer.Serialize(record, Json);
        await File.WriteAllTextAsync(Path.Combine(folder, "Versions", $"{record.Assessment.Version:D6}.json"), body, ct);
        var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(temp, body, ct); File.Move(temp, Path.Combine(folder, "record.json"), true);
    }
    public async Task<BoardRecord> Import(IFormFileCollection files, string organization, string currentYear, CancellationToken ct)
    {
        if (files.Count is < 1 or > 5 || files.Any(f => f.Length == 0 || !f.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) ||
            files.Sum(f => f.Length) > options.Value.MaximumUploadBytes) throw new InvalidDataException("Choose 1–5 non-empty .xlsx workbooks, up to 50 MB in total.");
        var id = Guid.NewGuid().ToString("N"); var folder = Folder(id); Directory.CreateDirectory(folder);
        try
        {
            var sources = new List<BoardSource>(); var snapshots = new List<WorkbookSnapshot>();
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i]; var stored = $"source-{i + 1}.xlsx"; var path = Path.Combine(folder, stored);
                await using (var output = File.Create(path)) await file.CopyToAsync(output, ct);
                var snapshot = xlsx.Read(path); snapshots.Add(snapshot);
                var kind = snapshot.HasSheet("02_DISPLAY_CONFIG") && snapshot.HasSheet("57_OPTION_ANALYSIS") ? "Mapped assessment" : "Original source model";
                if (sources.Any(s => s.Sha256 == snapshot.Sha256)) throw new InvalidDataException("The same workbook was selected more than once. Remove duplicate files and retry.");
                sources.Add(new(Path.GetFileName(file.FileName), stored, snapshot.Sha256, kind));
            }
            var mapped = sources.Select((s, i) => (s, i)).Where(x => x.s.Kind == "Mapped assessment").ToArray();
            if (mapped.Length > 1 || mapped.Length == 0 && sources.Count > 1)
                throw new InvalidDataException("Choose one controlling mapped assessment with optional source workbooks, or one original cash-flow model. Multiple competing primary workbooks require a source-authority decision.");
            var primary = mapped.Length == 1 ? mapped[0].i : 0;
            var a = importer.Read(Path.Combine(folder, sources[primary].StoredName), id, sources[primary].Name, organization, currentYear);
            if (!string.IsNullOrWhiteSpace(organization)) a.Organization = organization.Trim();
            if (!string.IsNullOrWhiteSpace(currentYear))
            {
                if (!V4CalculationEngine.Years(a).Any(y => y.Year == currentYear)) throw new InvalidDataException("The selected reporting year is outside the uploaded source horizon.");
                a.Config["CURRENT_FISCAL_YEAR"] = currentYear;
            }
            V4CalculationEngine.Calculate(a);
            a.Baseline = new("BASE-" + id, a.AssessmentId, a.OrganizationId, a.CreatedUtc, a.Setting("CURRENT_FISCAL_YEAR"), a.Setting("PORTFOLIO_PROPERTY_ID"), a.MethodologyVersion, a.Metrics.ToArray());
            var record = new BoardRecord { Assessment = a, Sources = sources };
            record.Checks = Checks(a, snapshots, sources, primary);
            await Save(record, ct); return record;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
    public async Task<BoardRecord> Configure(string id, V4ConfigRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var record = await Get(id, ct) ?? throw new InvalidDataException("Assessment not found."); var a = record.Assessment;
            if (a.Version != request.ExpectedVersion) throw new InvalidDataException("Assessment changed. Reload before saving.");
            if (!V4CalculationEngine.Years(a).Any(y => y.Year == request.CurrentYear)) throw new InvalidDataException("Choose a configured fiscal year.");
            var before = a.Setting("CURRENT_FISCAL_YEAR"); a.Config["CURRENT_FISCAL_YEAR"] = request.CurrentYear; a.Version++;
            a.Events.Add(new(Guid.NewGuid().ToString("N"), "Reporting year changed", a.Id, a.Version, DateTimeOffset.UtcNow, "Administrator", "Presentation reporting year", before, request.CurrentYear));
            await Save(record, ct); return record;
        }
        finally { gate.Release(); }
    }
    public async Task<BoardRecord> AddAction(string id, BoardActionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Basis) ||
            request.Status is not ("Proposed" or "In progress" or "Done")) throw new InvalidDataException("Action, owner, originating finding and a valid status are required.");
        await gate.WaitAsync(ct);
        try
        {
            var record = await Get(id, ct) ?? throw new InvalidDataException("Assessment not found."); var a = record.Assessment;
            if (a.Version != request.ExpectedVersion) throw new InvalidDataException("Assessment changed. Reload before saving.");
            if (request.Basis.Length > 2000 || request.Text.Length > 4000 || request.Owner.Length > 200) throw new InvalidDataException("Shorten the action fields.");
            var rows = a.Registers.GetValueOrDefault("48_ACTIONS") ?? []; a.Registers["48_ACTIONS"] = rows;
            var actionId = "ACT-" + Guid.NewGuid().ToString("N");
            rows.Add(BoardWorkbookImporter.Row("48_ACTIONS", actionId, ("Action_Text", request.Text), ("Owner_Name", request.Owner),
                ("Due_Date", request.DueDate), ("Status", request.Status), ("Finding_ID", request.Basis), ("Assessment_ID", a.AssessmentId)));
            a.Version++; a.Events.Add(new(Guid.NewGuid().ToString("N"), "Action recorded", actionId, a.Version, DateTimeOffset.UtcNow, request.Owner, request.Basis, "", request.Text));
            await Save(record, ct); return record;
        }
        finally { gate.Release(); }
    }
    private static List<BoardCheck> Checks(V4Assessment a, List<WorkbookSnapshot> books, List<BoardSource> sources, int primary)
    {
        var checks = new List<BoardCheck>(); var scope = a.Setting("PORTFOLIO_PROPERTY_ID");
        void Compare(string area, string year, decimal? expected, decimal? observed, string explanation, string source)
        {
            var diff = observed - expected;
            checks.Add(new("CHECK-" + checks.Count, area, year, expected, observed, diff,
                diff.HasValue ? Math.Abs(diff.Value) <= .01m ? "Pass" : "Review required" : "Not testable", explanation, source));
        }
        foreach (var y in V4CalculationEngine.Years(a))
        {
            decimal? V(string c) => a.Metric(scope, y.Year, c)?.Value;
            Compare("Revenue components", y.Year, V("REVENUE"), V("REV_RENT_EXISTING") + V("REV_RENT_NEW") + V("REV_SUBSIDY") + V("REV_OTHER"),
                "Revenue versus rent, subsidy and other-income components. Blank components remain untestable.", "Portfolio source components");
            var debts = a.Facts.Where(f => f.Scope != scope && f.Year == y.Year && f.Code == "DEBT_SERVICE").ToArray();
            Compare("Property / portfolio payment reconciliation", y.Year, V("DEBT_SERVICE"),
                debts.Length > 0 && debts.All(f => f.Value.HasValue && f.ValueState == "Available") ? debts.Sum(f => f.Value) : null,
                "Property schedules must reconcile before building coverage is relied on. Signed or missing payments are not silently converted to positive amounts.", string.Join("; ", debts.Select(f => f.SourceLocation)));
        }
        foreach (var r in a.Rows("11_RAW_STAGING").Where(r => r.Text("Raw_Label").StartsWith("Opening reserve", StringComparison.OrdinalIgnoreCase)))
        {
            var year = r.Text("Context_Text"); var previous = V4CalculationEngine.Years(a).Where(y => y.Year.CompareTo(year) < 0).OrderBy(y => y.Year).LastOrDefault().Year;
            if (previous is not null) Compare("Reserve opening continuity", year, a.Metric(scope, previous, "RESERVE_CLOSING")?.Value, r.Number("Raw_Value_Number"),
                "A reset in opening cash is separate from the year's operating surplus or shortfall.", r.Text("Source_Location"));
        }
        // Audit the original numeric observations against their mapped counterparts; never add both sources together.
        for (var i = 0; i < books.Count; i++)
        {
            if (i == primary && sources[i].Kind == "Mapped assessment") continue;
            foreach (var fact in a.Facts)
            {
                var match = Regex.Match(fact.SourceLocation, @"^'((?:[^']|'')+)'!\$?([A-Z]+)\$?(\d+)$");
                if (!match.Success || !books[i].Sheets.TryGetValue(match.Groups[1].Value.Replace("''", "'"), out var sheet)) continue;
                var address = match.Groups[2].Value + match.Groups[3].Value;
                Compare("Source cell / mapped input", fact.Year, fact.Value, WorkbookValue.Decimal(sheet.Cells.GetValueOrDefault(address)?.Value),
                    "A disagreement does not silently replace the selected mapped assessment. Resolve source authority, then upload a corrected version.", sources[i].Name + " · " + fact.SourceLocation);
            }
            var errors = books[i].Sheets.Values.SelectMany(s => s.Cells.Where(c => WorkbookValue.Text(c.Value.Value).StartsWith('#')).Select(c => s.Name + "!" + c.Key)).ToArray();
            if (errors.Length > 0) checks.Add(new("CHECK-" + checks.Count, "Original workbook formula errors", "Source workbook", 0, errors.Length, errors.Length,
                "Review required", "Source errors are retained as missing observations; no dashboard formula cache is used as a replacement.", sources[i].Name + " · " + string.Join("; ", errors.Take(30))));
        }
        return checks;
    }
}
public sealed record BoardActionRequest(string Text, string Owner, string DueDate, string Status, string Basis, int ExpectedVersion);
