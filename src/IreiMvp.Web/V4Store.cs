using System.Text.Json;

namespace IreiMvp.Web;

public sealed class V4Store(V4WorkbookImporter importer, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "V4Assessments");
    private string Folder(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid assessment ID.");
        return Path.Combine(Root, id);
    }
    public async Task<V4Assessment> ImportAsync(Stream input, string fileName, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N"); var folder = Folder(id); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "source.xlsx");
            await using (var output = File.Create(path)) await input.CopyToAsync(output, ct);
            var a = importer.Read(path, id, fileName); await SaveAsync(a, ct); return a;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
    public async Task<V4Assessment?> GetAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(Folder(id), "assessment.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<V4Assessment>(await File.ReadAllTextAsync(path, ct), Json) : null;
    }
    private async Task SaveAsync(V4Assessment a, CancellationToken ct)
    {
        var folder = Folder(a.Id); Directory.CreateDirectory(Path.Combine(folder, "Versions"));
        var data = JsonSerializer.Serialize(a, Json);
        // Historical versions are never rewritten. Latest pointer is replaced atomically.
        var versionPath = Path.Combine(folder, "Versions", $"{a.Version:D6}.json");
        await using (var file = new FileStream(versionPath, FileMode.CreateNew, FileAccess.Write))
        await using (var writer = new StreamWriter(file)) await writer.WriteAsync(data.AsMemory(), ct);
        var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(temp, data, ct); File.Move(temp, Path.Combine(folder, "assessment.json"), true);
    }
    private async Task<V4Assessment> ChangeAsync(string id, int expected, string reviewer, string reason,
        Action<V4Assessment> update, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            throw new InvalidDataException("Reviewer and reason are required.");
        await gate.WaitAsync(ct);
        try
        {
            var a = await GetAsync(id, ct) ?? throw new InvalidDataException("Assessment not found.");
            if (a.Version != expected) throw new InvalidDataException("Assessment changed. Reload before saving.");
            if (a.Status == "Approved") throw new InvalidDataException("Approved versions are immutable; import a new version.");
            update(a); a.Version++;
            a.Events.Add(new V4Event(Guid.NewGuid().ToString("N"), "Assessment updated", a.AssessmentId, a.Version,
                DateTimeOffset.UtcNow, reviewer, reason, $"Version {expected}", $"Version {a.Version}"));
            await SaveAsync(a, ct); return a;
        }
        finally { gate.Release(); }
    }
    public Task<V4Assessment> ConfigureAsync(string id, V4ConfigRequest request, CancellationToken ct) =>
        ChangeAsync(id, request.ExpectedVersion, "Authorized analyst", "Current reporting period selection", a =>
        {
            if (!V4CalculationEngine.Years(a).Any(y => y.Year == request.CurrentYear)) throw new InvalidDataException("Choose a configured fiscal year.");
            a.Config["CURRENT_FISCAL_YEAR"] = request.CurrentYear;
            Invalidate(a); V4CalculationEngine.Calculate(a);
        }, ct);
    public Task<V4Assessment> EditAsync(string id, V4RecordRequest request, CancellationToken ct) =>
        ChangeAsync(id, request.ExpectedVersion, request.Reviewer, request.Reason, a =>
        {
            string[] allowed = request.Sheet switch
            {
                "45_NARRATIVE" => ["Page_ID", "Section_ID", "Heading_Text", "Narrative_Text", "Basis_Record_IDs", "Review_Status"],
                "46_RISKS" => ["Short_Name", "Risk_Title", "Domain", "Size_Metric_ID", "When_Text", "What_Would_Settle_It", "Show_On_Pages", "Review_Status"],
                "47_OPTIONS" => ["Option_Name", "Option_Group", "Amount_Metric_ID", "Needs_First", "What_It_Does", "Effect_On_Homes", "What_It_Risks", "Show_On_Pages", "Review_Status", "Management_Proposed"],
                "48_ACTIONS" => ["Action_Text", "Short_Title", "Detail_Text", "Owner_Role", "Owner_Name", "Due_Date", "Timing_Trigger", "Status", "Board_Decision", "Linked_Risk_IDs", "Linked_Exception_IDs", "Option_ID", "Finding_ID"],
                "49_DECISIONS" => ["Decision_Text", "Decision_Date", "Decided_By", "Evidence_Record_IDs", "Linked_Action_ID", "Decision_Status", "Implementation_Status"],
                "20_FIELD_MAPPING" => ["Mapping_Status"],
                "51_EXCEPTIONS" => ["Status", "Resolution", "Acceptance_Reason"],
                _ => throw new InvalidDataException("Generated values and methodology registries cannot be edited here.")
            };
            if (request.Fields.Keys.Any(k => !allowed.Contains(k))) throw new InvalidDataException("Unsupported field.");
            if (request.Id.Length == 0 || request.Id.Length > 160) throw new InvalidDataException("A stable record ID is required.");
            var rows = a.Registers.GetValueOrDefault(request.Sheet) ?? [];
            var old = rows.FirstOrDefault(r => r.Id == request.Id);
            if (request.Sheet == "49_DECISIONS" && old is not null) throw new InvalidDataException("Decisions are append-only; record a correction with a new ID.");
            if (request.Sheet is "20_FIELD_MAPPING" or "51_EXCEPTIONS" && old is null) throw new InvalidDataException("Record not found.");
            var fields = old is null ? new Dictionary<string, string>() : new Dictionary<string, string>(old.Fields);
            foreach (var (key, value) in request.Fields) fields[key] = value;
            fields[request.Sheet switch { "45_NARRATIVE" => "Narrative_ID", "46_RISKS" => "Risk_ID", "47_OPTIONS" => "Option_ID", "48_ACTIONS" => "Action_ID", "49_DECISIONS" => "Decision_ID", "20_FIELD_MAPPING" => "Mapping_ID", _ => "Exception_ID" }] = request.Id;
            fields["Assessment_ID"] = a.AssessmentId; fields["Version_ID"] = (a.Version + 1).ToString();
            fields["Reviewer"] = request.Reviewer; fields["Review_Date"] = DateTimeOffset.UtcNow.ToString("O");
            if (request.Sheet is "45_NARRATIVE" or "46_RISKS" or "47_OPTIONS")
            {
                fields.TryAdd("Review_Status", "Draft");
                if (fields["Review_Status"] is not ("Draft" or "Approved" or "Rejected")) throw new InvalidDataException("Invalid interpretation review status.");
                fields["Draft_Method"] = "Manual";
                if (fields["Review_Status"] == "Approved" && a.Rows("51_EXCEPTIONS").Any(r => r.Text("Status") == "Open" && r.Text("Materiality") == "Material"))
                    throw new InvalidDataException("Resolve material exceptions before approving conclusions.");
            }
            if (request.Sheet == "48_ACTIONS")
            {
                fields.TryAdd("Status", "Proposed");
                if (fields["Status"] is not ("Proposed" or "In progress" or "Done" or "Overdue")) throw new InvalidDataException("Invalid action status.");
                if (fields.GetValueOrDefault("Action_Text", "").Length == 0) throw new InvalidDataException("Action text is required.");
                if (fields.GetValueOrDefault("Linked_Risk_IDs", "").Length == 0 && fields.GetValueOrDefault("Linked_Exception_IDs", "").Length == 0 && fields.GetValueOrDefault("Finding_ID", "").Length == 0)
                    throw new InvalidDataException("Link the proposed action to its originating finding, risk or exception.");
            }
            if (request.Sheet == "49_DECISIONS")
            {
                if (!a.Rows("48_ACTIONS").Any(r => r.Id == fields.GetValueOrDefault("Linked_Action_ID"))) throw new InvalidDataException("Link an existing action.");
                foreach (var key in new[] { "Decision_Text", "Decision_Date", "Decided_By", "Decision_Status", "Implementation_Status", "Evidence_Record_IDs" })
                    if (fields.GetValueOrDefault(key, "").Length == 0) throw new InvalidDataException($"{key} is required.");
                if (fields["Decision_Status"] is not ("Proposed" or "Approved" or "Rejected")) throw new InvalidDataException("Invalid decision status.");
                fields["Evidence_Assessment_Version"] = a.Version.ToString(); fields["Recorded_By"] = request.Reviewer;
            }
            if (request.Sheet == "51_EXCEPTIONS")
            {
                if (fields.GetValueOrDefault("Status") is not ("Open" or "Resolved" or "Exception accepted")) throw new InvalidDataException("Invalid exception status.");
                if (fields["Status"] == "Exception accepted") { fields["Accepted_By"] = request.Reviewer; fields["Acceptance_Reason"] = request.Reason; }
                if (fields["Status"] == "Resolved" && fields.GetValueOrDefault("Resolution", "").Length == 0) throw new InvalidDataException("Resolution is required.");
            }
            if (request.Sheet == "20_FIELD_MAPPING")
            {
                if (fields["Mapping_Status"] is not ("Proposed" or "Confirmed" or "Rejected" or "Conflict")) throw new InvalidDataException("Invalid mapping status.");
                if (fields["Mapping_Status"] == "Confirmed" && a.Rows("51_EXCEPTIONS").Any(r => r.Text("Status") == "Open" && r.Text("Affected_Record_IDs").Contains(request.Id)))
                    throw new InvalidDataException("Resolve the mapping's open exceptions first.");
                // Recover source values from the retained source on each mapping change.
                var original = importer.Read(Path.Combine(Folder(a.Id), "source.xlsx"), a.Id, a.FileName);
                a.Facts = original.Facts.Select(f =>
                {
                    var mappingId = a.Rows("32_FINANCIAL").FirstOrDefault(r => r.Id == f.Id)?.Text("Mapping_ID");
                    var status = mappingId == request.Id ? fields["Mapping_Status"] : a.Rows("20_FIELD_MAPPING").FirstOrDefault(r => r.Id == mappingId)?.Text("Mapping_Status") ?? f.MappingStatus;
                    var candidate = original.Rows("32_FINANCIAL").FirstOrDefault(r => r.Id == f.Id)?.Number("Amount") ?? f.Value;
                    return f with { MappingStatus = status, Value = status is "Rejected" or "Conflict" ? null : candidate,
                        ValueState = status == "Conflict" ? "Conflict" : status == "Rejected" ? "Missing" : candidate.HasValue ? "Available" : "Missing" };
                }).ToList();
                Invalidate(a); V4CalculationEngine.Calculate(a);
            }
            if (old is not null) rows.Remove(old);
            rows.Add(new V4Row(request.Id, request.Sheet, old?.Row ?? 0, fields, old?.Formulas ?? []));
            a.Registers[request.Sheet] = rows;
            a.Events.Add(new V4Event(Guid.NewGuid().ToString("N"), "Record edited", request.Id, a.Version + 1,
                DateTimeOffset.UtcNow, request.Reviewer, request.Reason, JsonSerializer.Serialize(old), JsonSerializer.Serialize(fields)));
        }, ct);

    public Task<V4Assessment> ObserveAsync(string id, V4OutcomeRequest r, CancellationToken ct) =>
        ChangeAsync(id, r.ExpectedVersion, r.Reviewer, "Outcome observation", a =>
        {
            if (a.Outcomes.Any(o => o.Id == r.Id)) throw new InvalidDataException("Observations are append-only.");
            if (!a.Rows("49_DECISIONS").Any(d => d.Id == r.DecisionId && d.Text("Linked_Action_ID") == r.ActionId && d.Text("Decision_Status") == "Approved"))
                throw new InvalidDataException("An outcome must link to an approved decision and its action.");
            if (new[] { r.Id, r.MetricDefinition, r.Domain, r.Unit, r.Direction, r.Scope, r.MethodologyVersion, r.Period, r.EvidenceId }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Outcome definition, unit, scope, period, method and evidence are required.");
            if (r.ContributionState is not ("Direct fact" or "Client-reported contribution" or "IREI-supported decision")) throw new InvalidDataException("Unsupported attribution claim.");
            if (!a.Rows("10_SOURCE_REGISTER").Any(s => s.Id == r.EvidenceId)) throw new InvalidDataException("Choose a retained evidence source.");
            // The first follow-up cannot claim comparability to a definition absent from the baseline.
            var prior = a.Outcomes.FirstOrDefault(o => o.MetricDefinition == r.MetricDefinition);
            var comparable = prior is not null && prior.Unit == r.Unit && prior.Scope == r.Scope && prior.Direction == r.Direction && prior.MethodologyVersion == r.MethodologyVersion;
            var baseline = prior?.BaselineValue ?? r.BaselineValue;
            a.Outcomes.Add(new V4Outcome(r.Id, r.ActionId, r.DecisionId, r.MetricDefinition, r.Domain, r.Unit, r.Direction,
                r.Scope, r.MethodologyVersion, baseline, r.CurrentValue, r.CurrentValue.HasValue ? "Available" : "Not yet measured",
                r.Period, r.EvidenceId, r.ContributionState, "Draft", r.Reviewer,
                comparable ? r.CurrentValue - baseline : null, comparable ? "Comparable" : "Definition/scope/method baseline requires review"));
        }, ct);

    private static void Invalidate(V4Assessment a)
    {
        foreach (var row in a.Rows("45_NARRATIVE"))
        {
            row.Fields["Review_Status"] = "Draft"; row.Fields["Notes"] = "Basis changed; re-review required.";
        }
    }
}
