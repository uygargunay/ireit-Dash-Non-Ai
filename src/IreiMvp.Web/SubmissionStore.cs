using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class SubmissionStore
{
    private static readonly HashSet<string> EvidenceExtensions = new(
        [".xlsx", ".xls", ".csv", ".pdf", ".docx"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IreiOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly TemplateTransformer _transformer;
    private readonly VersionComparisonEngine _comparison;
    private readonly V2WorkbookSync _workbookSync;
    private readonly XlsxWorkbookService _xlsx;
    private readonly ILogger<SubmissionStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SubmissionStore(
        IOptions<IreiOptions> options,
        IWebHostEnvironment environment,
        TemplateTransformer transformer,
        VersionComparisonEngine comparison,
        V2WorkbookSync workbookSync,
        XlsxWorkbookService xlsx,
        ILogger<SubmissionStore> logger)
    {
        _options = options.Value;
        _environment = environment;
        _transformer = transformer;
        _comparison = comparison;
        _workbookSync = workbookSync;
        _xlsx = xlsx;
        _logger = logger;
    }

    public async Task<SubmissionRecord> CreateAsync(
        Stream input,
        string originalFileName,
        string organizationName,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(originalFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only .xlsx workbooks are supported for an assessment upload.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var organizationId = OrganizationId(organizationName);
            var state = await LoadOrganizationAsync(organizationId, cancellationToken) ?? new OrganizationPortfolioState
            {
                OrganizationId = organizationId,
                OrganizationName = organizationName.Trim(),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            state.OrganizationName = organizationName.Trim();

            var submissionId = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            var submissionFolder = Path.Combine(GetSubmissionsRoot(), submissionId);
            Directory.CreateDirectory(submissionFolder);
            var inputPath = Path.Combine(submissionFolder, "input.xlsx");
            var outputPath = Path.Combine(submissionFolder, "admin-output.xlsx");

            await using (var file = File.Create(inputPath))
            {
                await input.CopyToAsync(file, cancellationToken);
            }

            ValidateWorkbookPackage(inputPath);
            var parentVersionId = state.LatestApprovedVersionId ?? state.CurrentVersionId;
            var versionNumber = state.NextVersionNumber++;
            var versionId = $"{organizationId}-V{versionNumber:000}";
            var record = new SubmissionRecord
            {
                Id = submissionId,
                OrganizationId = organizationId,
                OrganizationName = state.OrganizationName,
                VersionId = versionId,
                ParentVersionId = parentVersionId,
                VersionStatus = "Working",
                OriginalFileName = SafeFileName(originalFileName),
                Status = "Processing",
                CreatedUtc = now,
                InputPath = inputPath,
                OutputPath = outputPath
            };
            await SaveSubmissionAsync(record, cancellationToken);

            try
            {
                var result = await _transformer.TransformAsync(
                    inputPath,
                    outputPath,
                    state.OrganizationName,
                    record.OriginalFileName,
                    cancellationToken);
                record.Status = "Completed";
                record.CompletedUtc = DateTimeOffset.UtcNow;
                record.ProfileName = result.ProfileName;
                record.ReportingPeriod = result.Dashboard.ReportingPeriod;
                record.Dashboard = result.Dashboard;
                record.Warnings = result.Warnings;

                var sourceId = NewId("SRC");
                var version = new AssessmentVersionRecord
                {
                    VersionId = versionId,
                    VersionNumber = versionNumber,
                    SubmissionId = submissionId,
                    ParentVersionId = parentVersionId,
                    ReportingPeriod = record.ReportingPeriod,
                    VersionStatus = "Working",
                    ReadinessStatus = record.Dashboard.OverallStatus,
                    SourceFileName = record.OriginalFileName,
                    SourceFileHash = result.SourceFileHash,
                    PrimarySourceId = sourceId,
                    CreatedUtc = now,
                    SourceCutoffUtc = result.SourceCutoffUtc ?? now,
                    Dashboard = record.Dashboard,
                    Warnings = record.Warnings.ToList()
                };
                var prior = parentVersionId is null
                    ? null
                    : state.Assessments.FirstOrDefault(item => item.VersionId == parentVersionId);
                var comparison = _comparison.Compare(organizationId, version, prior);

                state.Assessments.Add(version);
                state.FieldHistory.AddRange(comparison.FieldHistory);
                state.ChangeEvents.AddRange(comparison.ChangeEvents);
                state.CurrentVersionId = versionId;
                state.UpdatedUtc = DateTimeOffset.UtcNow;
                state.SourceDocuments.Add(new SourceDocumentRecord
                {
                    SourceId = sourceId,
                    OrganizationId = organizationId,
                    VersionId = versionId,
                    FileName = record.OriginalFileName,
                    SourceType = "Uploaded Excel workbook",
                    UploadedUtc = now,
                    ReviewStatus = "Needs review",
                    Sha256 = result.SourceFileHash,
                    StoredPath = inputPath
                });
                AddGeneratedEvidence(state, version, record);
                SeedWorkflowIfEmpty(state, record);
                AddFutureEvents(state, version);
                LinkMaterialChangesToActions(state, version);

                _workbookSync.Sync(record, state);
                await SaveOrganizationAsync(state, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Submission {SubmissionId} failed.", submissionId);
                record.Status = "Failed";
                record.CompletedUtc = DateTimeOffset.UtcNow;
                record.Warnings.Add(exception.Message);
            }

            await SaveSubmissionAsync(record, cancellationToken);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SubmissionRecord?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!IsSafeIdentifier(id)) return null;
        var path = SubmissionPath(id);
        return File.Exists(path)
            ? await ReadJsonAsync<SubmissionRecord>(path, cancellationToken)
            : null;
    }

    public async Task<AssessmentView?> GetViewAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var record = await GetAsync(id, cancellationToken);
        if (record is null) return null;
        var state = await LoadOrganizationAsync(record.OrganizationId, cancellationToken);
        return state is null ? null : ToView(record, state);
    }

    public async Task<List<SubmissionRecord>> ListAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetSubmissionsRoot());
        var records = new List<SubmissionRecord>();
        foreach (var directory in Directory.EnumerateDirectories(GetSubmissionsRoot()))
        {
            var path = Path.Combine(directory, "submission.json");
            if (!File.Exists(path)) continue;
            try
            {
                var record = await ReadJsonAsync<SubmissionRecord>(path, cancellationToken);
                if (record is not null) records.Add(record);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not read submission metadata from {Path}.", path);
            }
        }

        return records.OrderByDescending(record => record.CreatedUtc).ToList();
    }

    public async Task<AssessmentView?> ApproveAsync(
        string submissionId,
        string approvedBy,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var context = await LoadContextAsync(submissionId, cancellationToken);
            if (context is null) return null;
            var (record, state) = context.Value;
            var version = state.Assessments.FirstOrDefault(item => item.VersionId == record.VersionId);
            if (version is null) return null;
            if (!string.Equals(version.VersionStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                var now = DateTimeOffset.UtcNow;
                version.VersionStatus = "Approved";
                version.ApprovedUtc = now;
                version.ApprovedBy = FirstNonBlank(approvedBy, "Authorized reviewer");
                version.LastVerifiedUtc = now;
                record.VersionStatus = "Approved";
                record.LastVerifiedUtc = now;
                state.LatestApprovedVersionId = version.VersionId;
                state.UpdatedUtc = now;
                foreach (var item in state.FieldHistory.Where(item => item.VersionId == version.VersionId))
                    item.ReviewStatus = "Approved";
                foreach (var item in state.ChangeEvents.Where(item => item.VersionId == version.VersionId))
                    item.ReviewStatus = "Reviewed";
                foreach (var item in state.Evidence.Where(item => item.VersionId == version.VersionId))
                {
                    item.VerificationStatus = "Verified";
                    item.Reviewer = version.ApprovedBy;
                    item.LastVerifiedUtc = now;
                }

                _workbookSync.Sync(record, state);
                await SaveOrganizationAsync(state, cancellationToken);
                await SaveSubmissionAsync(record, cancellationToken);
            }

            return ToView(record, state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ActionRecord?> CreateActionAsync(
        string submissionId,
        CreateActionRequest request,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (record, state) =>
        {
            if (string.IsNullOrWhiteSpace(request.Issue))
                throw new InvalidDataException("Action issue/description is required.");
            var now = DateTimeOffset.UtcNow;
            var item = new ActionRecord
            {
                ActionId = NewId("ACT"),
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                EntityType = FirstNonBlank(request.EntityType, "Portfolio"),
                EntityId = FirstNonBlank(request.EntityId, "PORTFOLIO"),
                Issue = FirstNonBlank(request.Issue),
                Cause = FirstNonBlank(request.Cause),
                Impact = FirstNonBlank(request.Impact),
                Response = FirstNonBlank(request.Response),
                Owner = FirstNonBlank(request.Owner),
                DueDate = request.DueDate,
                DecisionBody = FirstNonBlank(request.DecisionBody, "Management"),
                Status = "New",
                RiskLevel = FirstNonBlank(request.RiskLevel, "Medium"),
                EvidenceReference = FirstNonBlank(request.EvidenceReference),
                LinkedChangeId = request.LinkedChangeId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            state.Actions.Add(item);
            LinkChange(state, item);
            await Task.CompletedTask;
            return item;
        }, cancellationToken);

    public Task<ActionRecord?> UpdateActionAsync(
        string submissionId,
        string actionId,
        UpdateActionRequest request,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (_, state) =>
        {
            var item = state.Actions.FirstOrDefault(action => action.ActionId == actionId);
            if (item is null) return null;
            if (!string.IsNullOrWhiteSpace(request.Status)) item.Status = request.Status.Trim();
            if (request.Owner is not null) item.Owner = request.Owner.Trim();
            if (request.DueDate.HasValue) item.DueDate = request.DueDate;
            if (request.Response is not null) item.Response = request.Response.Trim();
            if (request.Outcome is not null) item.Outcome = request.Outcome.Trim();
            if (request.EvidenceReference is not null) item.EvidenceReference = request.EvidenceReference.Trim();
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            if (IsClosed(item.Status) && !item.ClosedUtc.HasValue) item.ClosedUtc = item.UpdatedUtc;
            await Task.CompletedTask;
            return item;
        }, cancellationToken);

    public Task<ObligationRecord?> CreateObligationAsync(
        string submissionId,
        CreateObligationRequest request,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (record, state) =>
        {
            if (string.IsNullOrWhiteSpace(request.Obligation))
                throw new InvalidDataException("Obligation description is required.");
            var now = DateTimeOffset.UtcNow;
            var item = new ObligationRecord
            {
                ObligationId = NewId("OBL"),
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                Source = FirstNonBlank(request.Source),
                Obligation = FirstNonBlank(request.Obligation),
                EntityType = FirstNonBlank(request.EntityType, "Portfolio"),
                EntityId = FirstNonBlank(request.EntityId, "PORTFOLIO"),
                Owner = FirstNonBlank(request.Owner),
                EffectiveDate = request.EffectiveDate,
                DueDate = request.DueDate,
                Recurrence = FirstNonBlank(request.Recurrence),
                Status = "Upcoming",
                EvidenceReference = FirstNonBlank(request.EvidenceReference),
                Notes = FirstNonBlank(request.Notes),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            state.Obligations.Add(item);
            if (item.DueDate.HasValue)
            {
                state.FutureEvents.Add(new FutureEventRecord
                {
                    EventId = NewId("EVT"),
                    OrganizationId = record.OrganizationId,
                    VersionId = record.VersionId,
                    EntityType = "Obligation",
                    EntityId = item.ObligationId,
                    EventType = "Obligation due",
                    EventName = item.Obligation,
                    EventDate = item.DueDate,
                    Impact = item.Notes,
                    Owner = item.Owner,
                    Priority = "Medium",
                    Source = item.Source,
                    Status = item.Status
                });
            }
            await Task.CompletedTask;
            return item;
        }, cancellationToken);

    public Task<ObligationRecord?> UpdateObligationAsync(
        string submissionId,
        string obligationId,
        UpdateObligationRequest request,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (_, state) =>
        {
            var item = state.Obligations.FirstOrDefault(obligation => obligation.ObligationId == obligationId);
            if (item is null) return null;
            if (!string.IsNullOrWhiteSpace(request.Status)) item.Status = request.Status.Trim();
            if (request.Owner is not null) item.Owner = request.Owner.Trim();
            if (request.DueDate.HasValue) item.DueDate = request.DueDate;
            if (request.EvidenceReference is not null) item.EvidenceReference = request.EvidenceReference.Trim();
            if (request.Notes is not null) item.Notes = request.Notes.Trim();
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            var futureEvent = state.FutureEvents.FirstOrDefault(future =>
                string.Equals(future.EntityType, "Obligation", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(future.EntityId, item.ObligationId, StringComparison.OrdinalIgnoreCase));
            if (futureEvent is not null)
            {
                futureEvent.EventDate = item.DueDate;
                futureEvent.EventName = item.Obligation;
                futureEvent.Owner = item.Owner;
                futureEvent.Impact = item.Notes;
                futureEvent.Status = item.Status;
            }
            await Task.CompletedTask;
            return item;
        }, cancellationToken);

    public async Task<SourceDocumentView?> AddEvidenceAsync(
        string submissionId,
        Stream input,
        string fileName,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (!EvidenceExtensions.Contains(extension))
            throw new InvalidDataException("Evidence files must be .xlsx, .xls, .csv, .pdf, or .docx.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var context = await LoadContextAsync(submissionId, cancellationToken);
            if (context is null) return null;
            var (record, state) = context.Value;
            var sourceId = NewId("SRC");
            var folder = Path.Combine(GetOrganizationRoot(record.OrganizationId), "Evidence", sourceId);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SafeFileName(fileName));
            await using (var output = File.Create(path))
            {
                await input.CopyToAsync(output, cancellationToken);
            }
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > _options.MaximumUploadBytes)
            {
                File.Delete(path);
                throw new InvalidDataException("Evidence file is empty or exceeds the configured upload limit.");
            }
            var item = new SourceDocumentRecord
            {
                SourceId = sourceId,
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                FileName = SafeFileName(fileName),
                SourceType = FirstNonBlank(sourceType, "Supporting evidence"),
                UploadedUtc = DateTimeOffset.UtcNow,
                ReviewStatus = "Needs review",
                Sha256 = ComputeSha256(path),
                StoredPath = path
            };
            state.SourceDocuments.Add(item);
            var replaced = state.Evidence
                .Where(evidence =>
                    string.Equals(evidence.FieldName, "Supporting document", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(evidence.SourceFile, item.FileName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(evidence => evidence.SourceDate)
                .FirstOrDefault();
            state.Evidence.Add(new EvidenceRecord
            {
                EvidenceId = NewId("EVD"),
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                FieldName = "Supporting document",
                Value = item.FileName,
                EntityType = "Portfolio",
                EntityId = "PORTFOLIO",
                SourceFile = item.FileName,
                SourceLocation = "Uploaded supporting evidence",
                SourceDate = item.UploadedUtc,
                MappingMethod = "Manual evidence upload",
                VerificationStatus = "Needs review",
                ReplacesEvidenceId = replaced?.EvidenceId
            });
            state.UpdatedUtc = DateTimeOffset.UtcNow;
            await SaveOrganizationAsync(state, cancellationToken);
            return ToView(item);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ReportSnapshotView?> CreateReportAsync(
        string submissionId,
        CreateReportRequest request,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (record, state) =>
        {
            if (string.IsNullOrWhiteSpace(request.ReportName))
                throw new InvalidDataException("Report name is required.");
            if (string.IsNullOrWhiteSpace(request.Purpose) ||
                string.IsNullOrWhiteSpace(request.Recipient) ||
                string.IsNullOrWhiteSpace(request.Scope))
                throw new InvalidDataException("Report purpose, intended recipient and approved scope are required.");
            if (!File.Exists(record.OutputPath))
                throw new InvalidDataException("The assessment workbook is unavailable.");
            var reportId = NewId("RPT");
            var reportFolder = Path.Combine(GetOrganizationRoot(record.OrganizationId), "Reports");
            Directory.CreateDirectory(reportFolder);
            var artifactPath = Path.Combine(reportFolder, $"{reportId}.xlsx");
            File.Copy(record.OutputPath, artifactPath, overwrite: false);
            var prior = state.Reports
                .Where(item => string.Equals(item.ReportName, request.ReportName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.ReportVersion)
                .FirstOrDefault();
            var item = new ReportSnapshotRecord
            {
                ReportId = reportId,
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                ReportName = FirstNonBlank(request.ReportName),
                Purpose = FirstNonBlank(request.Purpose),
                Period = record.ReportingPeriod,
                ReportVersion = (prior?.ReportVersion ?? 0) + 1,
                Status = "Draft",
                Recipient = FirstNonBlank(request.Recipient),
                Scope = FirstNonBlank(request.Scope),
                SupersedesReportId = prior?.ReportId,
                CreatedUtc = DateTimeOffset.UtcNow,
                ArtifactPath = artifactPath
            };
            state.Reports.Add(item);
            await Task.CompletedTask;
            return ToView(item);
        }, cancellationToken);

    public Task<ReportSnapshotView?> ApproveReportAsync(
        string submissionId,
        string reportId,
        string approvedBy,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (record, state) =>
        {
            var item = state.Reports.FirstOrDefault(report => report.ReportId == reportId);
            if (item is null) return null;
            var version = state.Assessments.FirstOrDefault(version => version.VersionId == item.VersionId);
            if (!string.Equals(version?.VersionStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Approve the assessment version before approving its report snapshot.");
            if (!string.Equals(item.VersionId, record.VersionId, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(record.OutputPath))
                throw new InvalidDataException("The approved assessment artifact for this report is unavailable.");
            if (string.IsNullOrWhiteSpace(item.ArtifactPath))
            {
                var reportFolder = Path.Combine(GetOrganizationRoot(record.OrganizationId), "Reports");
                Directory.CreateDirectory(reportFolder);
                item.ArtifactPath = Path.Combine(reportFolder, $"{item.ReportId}.xlsx");
            }
            File.Copy(record.OutputPath, item.ArtifactPath, overwrite: true);
            item.Status = "Approved";
            item.ApprovedBy = FirstNonBlank(approvedBy, "Authorized reviewer");
            item.ApprovedUtc = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(item.SupersedesReportId))
            {
                var prior = state.Reports.FirstOrDefault(report => report.ReportId == item.SupersedesReportId);
                if (prior is not null) prior.Status = "Superseded";
            }
            _workbookSync.Sync(new SubmissionRecord
            {
                OutputPath = item.ArtifactPath,
                VersionId = record.VersionId,
                ParentVersionId = record.ParentVersionId
            }, state);
            await Task.CompletedTask;
            return ToView(item);
        }, cancellationToken);

    public Task<ReportSnapshotView?> ShareReportAsync(
        string submissionId,
        string reportId,
        string sharedBy,
        CancellationToken cancellationToken) => MutateAsync(
        submissionId,
        async (_, state) =>
        {
            var item = state.Reports.FirstOrDefault(report => report.ReportId == reportId);
            if (item is null) return null;
            if (!string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, "Shared", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only an approved report snapshot can be shared.");
            item.Status = "Shared";
            item.SharedBy = FirstNonBlank(sharedBy, "Authorized user");
            item.SharedUtc = DateTimeOffset.UtcNow;
            await Task.CompletedTask;
            return ToView(item);
        }, cancellationToken);

    public async Task<string?> GetReportPathAsync(
        string submissionId,
        string reportId,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(submissionId, cancellationToken);
        if (context is null) return null;
        var item = context.Value.State.Reports.FirstOrDefault(report => report.ReportId == reportId);
        return item is not null &&
               (string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Status, "Shared", StringComparison.OrdinalIgnoreCase)) &&
               File.Exists(item.ArtifactPath)
            ? item.ArtifactPath
            : null;
    }

    public string? GetOutputPath(SubmissionRecord record) =>
        File.Exists(record.OutputPath) ? record.OutputPath : null;

    private async Task<T?> MutateAsync<T>(
        string submissionId,
        Func<SubmissionRecord, OrganizationPortfolioState, Task<T?>> mutation,
        CancellationToken cancellationToken)
        where T : class
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var context = await LoadContextAsync(submissionId, cancellationToken);
            if (context is null) return null;
            var (record, state) = context.Value;
            var result = await mutation(record, state);
            if (result is null) return null;
            state.UpdatedUtc = DateTimeOffset.UtcNow;
            if (!string.Equals(record.VersionStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                _workbookSync.Sync(record, state);
            await SaveOrganizationAsync(state, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SeedWorkflowIfEmpty(OrganizationPortfolioState state, SubmissionRecord record)
    {
        var source = _xlsx.Read(record.InputPath);
        var view = new WorkbookDataView(source);
        var now = DateTimeOffset.UtcNow;

        foreach (var row in view.Rows("STG_SOURCE_INTAKE"))
        {
            var fileName = row.Text("File / Source Name");
            var sourceId = row.Text("Source_ID");
            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(fileName) ||
                state.SourceDocuments.Any(item =>
                    string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            state.SourceDocuments.Add(new SourceDocumentRecord
            {
                SourceId = UniqueId(state.SourceDocuments.Select(item => item.SourceId), sourceId, "SRC"),
                OrganizationId = record.OrganizationId,
                VersionId = record.VersionId,
                FileName = fileName,
                SourceType = FirstNonBlank(row.Text("Source Type"), "Referenced source"),
                UploadedUtc = row.Date("Version / Date") ?? now,
                ReviewStatus = FirstNonBlank(row.Text("Review Status"), "Needs review"),
                Sha256 = "",
                StoredPath = ""
            });
        }

        if (state.Actions.Count == 0)
        {
            foreach (var row in view.Rows("ACTION_DECISION"))
            {
                var issue = row.Text("Description");
                if (string.IsNullOrWhiteSpace(row.Text("Action_ID")) || string.IsNullOrWhiteSpace(issue)) continue;
                var created = row.Date("Date_Identified") ?? now;
                var status = FirstNonBlank(row.Text("Status"), "New");
                state.Actions.Add(new ActionRecord
                {
                    ActionId = UniqueId(state.Actions.Select(item => item.ActionId), row.Text("Action_ID"), "ACT"),
                    OrganizationId = record.OrganizationId,
                    VersionId = record.VersionId,
                    EntityType = string.Equals(row.Text("Property_ID"), "PORTFOLIO", StringComparison.OrdinalIgnoreCase) ? "Portfolio" : "Property",
                    EntityId = FirstNonBlank(row.Text("Property_ID"), "PORTFOLIO"),
                    Issue = issue,
                    Response = row.Text("Action_Type"),
                    Owner = row.Text("Owner"),
                    DueDate = row.Date("Due_Date"),
                    DecisionBody = FirstNonBlank(row.Text("Decision_Body"), "Management"),
                    Status = status,
                    RiskLevel = FirstNonBlank(row.Text("Priority"), "Medium"),
                    Outcome = row.Text("Outcome"),
                    EvidenceReference = row.Text("Evidence_ID"),
                    CreatedUtc = created,
                    UpdatedUtc = created,
                    ClosedUtc = row.Date("Closed_Date")
                });
            }
        }

        if (state.Obligations.Count == 0)
        {
            foreach (var row in view.Rows("DATA_OBLIGATIONS"))
            {
                var text = row.Text("Obligation");
                if (string.IsNullOrWhiteSpace(row.Text("Obligation_ID")) || string.IsNullOrWhiteSpace(text)) continue;
                state.Obligations.Add(new ObligationRecord
                {
                    ObligationId = UniqueId(state.Obligations.Select(item => item.ObligationId), row.Text("Obligation_ID"), "OBL"),
                    OrganizationId = record.OrganizationId,
                    VersionId = record.VersionId,
                    Source = row.Text("Source_or_Agreement"),
                    Obligation = text,
                    EntityType = FirstNonBlank(row.Text("Obligation_Type"), "Portfolio"),
                    EntityId = FirstNonBlank(row.Text("Property_ID"), "PORTFOLIO"),
                    Owner = row.Text("Owner"),
                    DueDate = row.Date("Due_Date"),
                    Recurrence = row.Text("Frequency"),
                    Status = FirstNonBlank(row.Text("Status"), "Upcoming"),
                    EvidenceReference = row.Text("Evidence_ID"),
                    Notes = row.Text("Notes"),
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
        }

        if (state.Reports.Count == 0)
        {
            foreach (var row in view.Rows("REPORT_SNAPSHOT"))
            {
                var name = row.Text("Report_Type");
                if (string.IsNullOrWhiteSpace(row.Text("Report_ID")) || string.IsNullOrWhiteSpace(name)) continue;
                var reportVersion = state.Reports.Count(item =>
                    string.Equals(item.ReportName, name, StringComparison.OrdinalIgnoreCase)) + 1;
                state.Reports.Add(new ReportSnapshotRecord
                {
                    ReportId = UniqueId(state.Reports.Select(item => item.ReportId), row.Text("Report_ID"), "RPT"),
                    OrganizationId = record.OrganizationId,
                    VersionId = record.VersionId,
                    ReportName = name,
                    Purpose = row.Text("Purpose"),
                    Period = FirstNonBlank(row.Text("Reporting_Period"), record.ReportingPeriod),
                    ReportVersion = reportVersion,
                    Status = FirstNonBlank(row.Text("Report_Status"), "Draft"),
                    Recipient = row.Text("Intended_Recipient"),
                    Scope = row.Text("Scope"),
                    ApprovedBy = NullIfBlank(row.Text("Approved_By")),
                    ApprovedUtc = row.Date("Approved_Date"),
                    CreatedUtc = row.Date("Generated_Date") ?? now,
                    SupersedesReportId = NullIfBlank(row.Text("Supersedes_Report_ID")),
                    ArtifactPath = ""
                });
            }
        }
    }

    private static void AddGeneratedEvidence(
        OrganizationPortfolioState state,
        AssessmentVersionRecord version,
        SubmissionRecord record)
    {
        var items = new List<(string EntityType, string EntityId, string Field, string Value, string Location)>
        {
            ("Portfolio", "PORTFOLIO", "Modeled units", version.Dashboard.ModeledUnits.ToString(), "DATA_PROPERTY_MASTER / DATA_UNIT_MIX"),
            ("Portfolio", "PORTFOLIO", "Revenue", version.Dashboard.Revenue?.ToString("0.##") ?? "Not assessed", "DATA_OPERATING_ACTUALS"),
            ("Portfolio", "PORTFOLIO", "NOI", version.Dashboard.Noi?.ToString("0.##") ?? "Not assessed", "DATA_OPERATING_ACTUALS"),
            ("Portfolio", "PORTFOLIO", "Debt service", version.Dashboard.DebtService?.ToString("0.##") ?? "Not assessed", "DATA_DEBT_SCHEDULE"),
            ("Portfolio", "PORTFOLIO", "DSCR", version.Dashboard.Dscr?.ToString("0.####") ?? "Not assessed", "Derived: NOI / debt service")
        };
        items.AddRange(version.Dashboard.Properties.Select(property =>
            ("Property", property.PropertyId, "Modeled units", property.Units.ToString(), $"DATA_PROPERTY_MASTER row for {property.PropertyId}")));

        foreach (var item in items)
        {
            var replaced = state.Evidence
                .Where(evidence => evidence.EntityType == item.EntityType &&
                                   evidence.EntityId == item.EntityId &&
                                   evidence.FieldName == item.Field)
                .OrderByDescending(evidence => evidence.SourceDate)
                .FirstOrDefault();
            state.Evidence.Add(new EvidenceRecord
            {
                EvidenceId = NewId("EVD"),
                OrganizationId = record.OrganizationId,
                VersionId = version.VersionId,
                FieldName = item.Field,
                Value = item.Value,
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                SourceFile = record.OriginalFileName,
                SourceLocation = item.Location,
                EffectiveDate = version.SourceCutoffUtc,
                SourceDate = version.SourceCutoffUtc,
                MappingMethod = "Deterministic canonical/header mapping",
                VerificationStatus = "Needs review",
                ReplacesEvidenceId = replaced?.EvidenceId
            });
        }
    }

    private static void AddFutureEvents(OrganizationPortfolioState state, AssessmentVersionRecord version)
    {
        foreach (var agreement in version.Dashboard.Agreements.Where(item => item.ExpiryDate.HasValue))
        {
            state.FutureEvents.Add(new FutureEventRecord
            {
                EventId = NewId("EVT"),
                OrganizationId = state.OrganizationId,
                VersionId = version.VersionId,
                EntityType = "Agreement",
                EntityId = agreement.AgreementId,
                EventType = "Agreement expiry",
                EventName = agreement.AgreementType,
                EventDate = agreement.ExpiryDate,
                Impact = agreement.Impact,
                Priority = "Medium",
                Source = agreement.Source
            });
        }
        foreach (var debt in version.Dashboard.DebtHorizon.Where(item => item.Year.HasValue))
        {
            state.FutureEvents.Add(new FutureEventRecord
            {
                EventId = NewId("EVT"),
                OrganizationId = state.OrganizationId,
                VersionId = version.VersionId,
                EventType = "Debt maturity",
                EventName = debt.Label,
                EventDate = new DateTimeOffset(debt.Year!.Value, 12, 31, 0, 0, 0, TimeSpan.Zero),
                Amount = debt.Amount,
                Impact = "Refinancing / liquidity planning",
                Priority = "High",
                Source = "DATA_DEBT_MASTER"
            });
        }
        foreach (var obligation in state.Obligations.Where(item => item.DueDate.HasValue))
        {
            state.FutureEvents.Add(new FutureEventRecord
            {
                EventId = NewId("EVT"),
                OrganizationId = state.OrganizationId,
                VersionId = version.VersionId,
                EntityType = "Obligation",
                EntityId = obligation.ObligationId,
                EventType = "Obligation due",
                EventName = obligation.Obligation,
                EventDate = obligation.DueDate,
                Impact = obligation.Notes,
                Owner = obligation.Owner,
                Priority = "Medium",
                Source = obligation.Source,
                Status = obligation.Status
            });
        }
    }

    private static void LinkMaterialChangesToActions(OrganizationPortfolioState state, AssessmentVersionRecord version)
    {
        foreach (var change in state.ChangeEvents.Where(item => item.VersionId == version.VersionId && item.IsMaterial))
        {
            var existing = state.Actions.FirstOrDefault(item => item.LinkedChangeId == change.ChangeId);
            if (existing is not null)
            {
                change.LinkedActionId = existing.ActionId;
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            var action = new ActionRecord
            {
                ActionId = NewId("ACT"),
                OrganizationId = state.OrganizationId,
                VersionId = version.VersionId,
                EntityType = change.EntityType,
                EntityId = change.EntityId,
                Issue = $"Review material change: {change.FieldOrMetric}",
                Impact = change.Impact,
                Owner = "Unassigned",
                DecisionBody = "Management",
                Status = "New",
                RiskLevel = "High",
                LinkedChangeId = change.ChangeId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            state.Actions.Add(action);
            change.LinkedActionId = action.ActionId;
        }
    }

    private static void LinkChange(OrganizationPortfolioState state, ActionRecord action)
    {
        if (string.IsNullOrWhiteSpace(action.LinkedChangeId)) return;
        var change = state.ChangeEvents.FirstOrDefault(item => item.ChangeId == action.LinkedChangeId);
        if (change is not null) change.LinkedActionId = action.ActionId;
    }

    private AssessmentView ToView(SubmissionRecord record, OrganizationPortfolioState state) => new()
    {
        Id = record.Id,
        OrganizationId = record.OrganizationId,
        OrganizationName = record.OrganizationName,
        OriginalFileName = record.OriginalFileName,
        Status = record.Status,
        ProfileName = record.ProfileName,
        VersionId = record.VersionId,
        ParentVersionId = record.ParentVersionId,
        VersionStatus = record.VersionStatus,
        ReportingPeriod = record.ReportingPeriod,
        CreatedUtc = record.CreatedUtc,
        CompletedUtc = record.CompletedUtc,
        LastVerifiedUtc = record.LastVerifiedUtc,
        Dashboard = record.Dashboard,
        Warnings = record.Warnings,
        Versions = state.Assessments.OrderByDescending(item => item.VersionNumber).Select(item => new AssessmentVersionSummary
        {
            VersionId = item.VersionId,
            SubmissionId = item.SubmissionId,
            ParentVersionId = item.ParentVersionId,
            ReportingPeriod = item.ReportingPeriod,
            VersionStatus = item.VersionStatus,
            CreatedUtc = item.CreatedUtc,
            ApprovedUtc = item.ApprovedUtc,
            SourceFileName = item.SourceFileName
        }).ToList(),
        FieldHistory = state.FieldHistory.Where(item => item.VersionId == record.VersionId).ToList(),
        Changes = state.ChangeEvents.Where(item => item.VersionId == record.VersionId).ToList(),
        Actions = state.Actions.OrderBy(item => IsClosed(item.Status)).ThenBy(item => item.DueDate).ToList(),
        Obligations = state.Obligations.OrderBy(item => item.DueDate).ToList(),
        Reports = state.Reports.OrderByDescending(item => item.CreatedUtc).Select(ToView).ToList(),
        Evidence = state.Evidence.Where(item => item.VersionId == record.VersionId).ToList(),
        FutureEvents = state.FutureEvents.Where(item => item.VersionId == record.VersionId).OrderBy(item => item.EventDate).ToList(),
        SourceDocuments = state.SourceDocuments.OrderByDescending(item => item.UploadedUtc).Select(ToView).ToList()
    };

    private static ReportSnapshotView ToView(ReportSnapshotRecord item) => new()
    {
        ReportId = item.ReportId,
        VersionId = item.VersionId,
        ReportName = item.ReportName,
        Purpose = item.Purpose,
        Period = item.Period,
        ReportVersion = item.ReportVersion,
        Status = item.Status,
        Recipient = item.Recipient,
        Scope = item.Scope,
        ApprovedBy = item.ApprovedBy,
        ApprovedUtc = item.ApprovedUtc,
        SharedBy = item.SharedBy,
        SharedUtc = item.SharedUtc,
        CreatedUtc = item.CreatedUtc,
        SupersedesReportId = item.SupersedesReportId,
        ArtifactAvailable =
            (string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Status, "Shared", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(item.ArtifactPath) &&
            File.Exists(item.ArtifactPath)
    };

    private static SourceDocumentView ToView(SourceDocumentRecord item) => new()
    {
        SourceId = item.SourceId,
        VersionId = item.VersionId,
        FileName = item.FileName,
        SourceType = item.SourceType,
        UploadedUtc = item.UploadedUtc,
        ReviewStatus = item.ReviewStatus,
        Sha256 = item.Sha256
    };

    private async Task<(SubmissionRecord Record, OrganizationPortfolioState State)?> LoadContextAsync(
        string submissionId,
        CancellationToken cancellationToken)
    {
        var record = await GetAsync(submissionId, cancellationToken);
        if (record is null) return null;
        var state = await LoadOrganizationAsync(record.OrganizationId, cancellationToken);
        return state is null ? null : (record, state);
    }

    private async Task<OrganizationPortfolioState?> LoadOrganizationAsync(
        string organizationId,
        CancellationToken cancellationToken)
    {
        var path = OrganizationPath(organizationId);
        return File.Exists(path)
            ? await ReadJsonAsync<OrganizationPortfolioState>(path, cancellationToken)
            : null;
    }

    private Task SaveSubmissionAsync(SubmissionRecord record, CancellationToken cancellationToken) =>
        WriteJsonAtomicAsync(SubmissionPath(record.Id), record, cancellationToken);

    private Task SaveOrganizationAsync(OrganizationPortfolioState state, CancellationToken cancellationToken) =>
        WriteJsonAtomicAsync(OrganizationPath(state.OrganizationId), state, cancellationToken);

    private async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private void ValidateWorkbookPackage(string path)
    {
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > _options.MaximumUploadBytes)
            throw new InvalidDataException($"Workbook size must be between 1 byte and {_options.MaximumUploadBytes:N0} bytes.");
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry("xl/workbook.xml") is null || archive.GetEntry("[Content_Types].xml") is null)
                throw new InvalidDataException("The uploaded file is not a valid Office Open XML workbook.");
            var expandedLimit = Math.Min(_options.MaximumUploadBytes * 10, 500L * 1024 * 1024);
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > expandedLimit || archive.Entries.Count > 5_000)
                    throw new InvalidDataException("The workbook package expands beyond the safe processing limit.");
            }
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception)
        {
            throw new InvalidDataException("The uploaded file is not a readable .xlsx package.", exception);
        }
    }

    private string SubmissionPath(string id) => Path.Combine(GetSubmissionsRoot(), id, "submission.json");
    private string OrganizationPath(string id) => Path.Combine(GetOrganizationRoot(id), "organization.json");
    private string GetSubmissionsRoot() => Path.Combine(ResolvePath(_options.DataRoot), "Submissions");
    private string GetOrganizationRoot(string id) => Path.Combine(ResolvePath(_options.DataRoot), "Organizations", id);
    private string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path);

    private static string OrganizationId(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "organization";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant())))[..8];
        return $"ORG-{slug}-{hash}";
    }

    private static string SafeFileName(string name)
    {
        var result = Path.GetFileName(name);
        foreach (var character in Path.GetInvalidFileNameChars()) result = result.Replace(character, '-');
        return string.IsNullOrWhiteSpace(result) || result is "." or ".." ? "upload" : result;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string UniqueId(IEnumerable<string> existing, string preferred, string prefix) =>
        !string.IsNullOrWhiteSpace(preferred) && !existing.Contains(preferred, StringComparer.OrdinalIgnoreCase)
            ? preferred
            : NewId(prefix);

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";
    private static string FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsClosed(string value) => WorkbookValue.Normalize(value) is "closed" or "complete" or "completed" or "resolved";
    private static bool IsSafeIdentifier(string value) => value.Length is > 5 and < 120 && value.All(character => char.IsLetterOrDigit(character) || character == '-');
}
