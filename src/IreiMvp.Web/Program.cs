using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<IreiOptions>(builder.Configuration.GetSection("Irei"));
        var maximumUploadBytes = builder.Configuration.GetValue<long?>("Irei:MaximumUploadBytes")
                                 ?? 50 * 1024 * 1024;
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maximumUploadBytes);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumUploadBytes);

        builder.Services.AddSingleton<XlsxWorkbookService>();
        builder.Services.AddSingleton<DashboardMetricsFactory>();
        builder.Services.AddSingleton<SmartMappingEngine>();
        builder.Services.AddSingleton<TemplateTransformer>();
        builder.Services.AddSingleton<VersionComparisonEngine>();
        builder.Services.AddSingleton<V2WorkbookSync>();
        builder.Services.AddSingleton<SubmissionStore>();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "IREI Stage 1 V2",
            mode = "deterministic non-AI"
        }));

        app.MapPost("/api/submissions", UploadAssessment);
        app.MapGet("/api/submissions/{id}", async (
            string id,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
        {
            var view = await store.GetViewAsync(id, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

        app.MapPost("/api/submissions/{id}/approve", async (
            string id,
            ApprovalRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
        {
            var view = await store.ApproveAsync(id, request.ApprovedBy, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

        app.MapPost("/api/submissions/{id}/actions", async (
            string id,
            CreateActionRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.CreateActionAsync(id, request, cancellationToken)));
        app.MapPatch("/api/submissions/{id}/actions/{actionId}", async (
            string id,
            string actionId,
            UpdateActionRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.UpdateActionAsync(id, actionId, request, cancellationToken)));

        app.MapPost("/api/submissions/{id}/obligations", async (
            string id,
            CreateObligationRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.CreateObligationAsync(id, request, cancellationToken)));
        app.MapPatch("/api/submissions/{id}/obligations/{obligationId}", async (
            string id,
            string obligationId,
            UpdateObligationRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.UpdateObligationAsync(id, obligationId, request, cancellationToken)));

        app.MapPost("/api/submissions/{id}/evidence", async (
            string id,
            HttpRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Use multipart/form-data with a file field named 'file'." });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Select a non-empty evidence file." });
            try
            {
                await using var stream = file.OpenReadStream();
                var item = await store.AddEvidenceAsync(
                    id,
                    stream,
                    file.FileName,
                    form["sourceType"].FirstOrDefault() ?? "Supporting evidence",
                    cancellationToken);
                return item is null ? Results.NotFound() : Results.Ok(item);
            }
            catch (InvalidDataException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/submissions/{id}/reports", async (
            string id,
            CreateReportRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.CreateReportAsync(id, request, cancellationToken)));
        app.MapPost("/api/submissions/{id}/reports/{reportId}/approve", async (
            string id,
            string reportId,
            ApprovalRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.ApproveReportAsync(id, reportId, request.ApprovedBy, cancellationToken)));
        app.MapPost("/api/submissions/{id}/reports/{reportId}/share", async (
            string id,
            string reportId,
            ShareReportRequest request,
            SubmissionStore store,
            CancellationToken cancellationToken) =>
            await SafeMutation(() => store.ShareReportAsync(id, reportId, request.SharedBy, cancellationToken)));
        app.MapGet("/api/submissions/{id}/reports/{reportId}/download", async (
            string id,
            string reportId,
            HttpRequest request,
            SubmissionStore store,
            IOptions<IreiOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(request, options.Value)) return Results.Unauthorized();
            var path = await store.GetReportPathAsync(id, reportId, cancellationToken);
            return path is null
                ? Results.NotFound()
                : Results.File(
                    path,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"IREI-{reportId}.xlsx");
        });

        app.MapGet("/api/admin/submissions", async (
            HttpRequest request,
            SubmissionStore store,
            IOptions<IreiOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(request, options.Value)) return Results.Unauthorized();
            var records = await store.ListAsync(cancellationToken);
            return Results.Ok(records.Select(record => new
            {
                record.Id,
                record.OrganizationId,
                record.OrganizationName,
                record.VersionId,
                record.VersionStatus,
                record.ReportingPeriod,
                record.OriginalFileName,
                record.Status,
                record.ProfileName,
                record.CreatedUtc,
                record.CompletedUtc,
                record.Warnings
            }));
        });

        app.MapGet("/api/admin/submissions/{id}/output", async (
            string id,
            HttpRequest request,
            SubmissionStore store,
            IOptions<IreiOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(request, options.Value)) return Results.Unauthorized();
            var record = await store.GetAsync(id, cancellationToken);
            if (record is null) return Results.NotFound();
            var path = store.GetOutputPath(record);
            return path is null
                ? Results.NotFound(new { error = "Generated workbook is unavailable." })
                : Results.File(
                    path,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"IREI-{SanitizeFileName(record.OrganizationName)}-{record.VersionId}.xlsx");
        });

        app.MapFallbackToFile("index.html");
        app.Run();
    }

    private static async Task<IResult> UploadAssessment(
        HttpRequest request,
        SubmissionStore store,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Use multipart/form-data with a file field named 'file'." });
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        var organizationName = form["organizationName"].FirstOrDefault()?.Trim();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "Select a non-empty .xlsx file." });
        if (string.IsNullOrWhiteSpace(organizationName))
            organizationName = Path.GetFileNameWithoutExtension(file.FileName);

        try
        {
            await using var stream = file.OpenReadStream();
            var record = await store.CreateAsync(stream, file.FileName, organizationName, cancellationToken);
            if (record.Status != "Completed")
                return Results.Problem(
                    title: "Workbook processing failed",
                    detail: string.Join(" ", record.Warnings),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            var view = await store.GetViewAsync(record.Id, cancellationToken);
            return Results.Ok(view);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> SafeMutation<T>(Func<Task<T?>> operation) where T : class
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static bool IsAdmin(HttpRequest request, IreiOptions options)
    {
        var provided = request.Headers["X-Admin-Key"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(options.AdminKey) &&
               string.Equals(provided, options.AdminKey, StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '-');
        return value.Trim().Replace(' ', '-');
    }
}
