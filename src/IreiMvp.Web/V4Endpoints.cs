using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public static class V4Endpoints
{
    public static void MapV4(this WebApplication app)
    {
        var group = app.MapGroup("/api/v4");
        group.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<IreiOptions>>().Value;
            var key = context.HttpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(options.AdminKey) || !string.Equals(key, options.AdminKey, StringComparison.Ordinal)) return Results.Unauthorized();
            try { return await next(context); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        group.MapPost("/assessments", async (HttpRequest request, V4Store store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Upload a multipart .xlsx file." });
            var file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
            if (file is null || file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Choose a nonempty V4.2 .xlsx workbook." });
            await using var stream = file.OpenReadStream();
            var a = await store.ImportAsync(stream, file.FileName, ct);
            return Results.Ok(new { a.Id, a.AssessmentId, a.Version });
        });
        group.MapGet("/assessments/{id}", async (string id, V4Store store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound());
        group.MapGet("/assessments/{id}/pages/{page}", async (string id, string page, V4Store store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } a ? Results.Ok(V4ViewService.Page(a, page)) : Results.NotFound());
        group.MapGet("/assessments/{id}/metrics/{metricId}/evidence", async (string id, string metricId, V4Store store, CancellationToken ct) =>
        {
            var a = await store.GetAsync(id, ct); var metric = a?.Metrics.FirstOrDefault(m => m.Id == metricId);
            if (a is null || metric is null) return Results.NotFound();
            var facts = a.Facts.Where(f => metric.InputIds.Contains(f.Id)).ToArray();
            return Results.Ok(new { Result = metric, MappedFacts = facts,
                Mappings = a.Rows("20_FIELD_MAPPING").Where(r => a.Rows("32_FINANCIAL").Any(f => metric.InputIds.Contains(f.Id) && f.Text("Mapping_ID") == r.Id)),
                SourceNotes = a.Rows("12_SOURCE_NOTES").Where(n => facts.Any(f => f.SourceId == n.Text("Source_ID"))),
                RelatedExceptions = a.Rows("51_EXCEPTIONS").Where(r => facts.Any(f => r.Text("Source_ID") == f.SourceId)),
                SourceChecksum = a.Sha256, AssessmentVersion = a.Version });
        });
        group.MapPut("/assessments/{id}/configuration", async (string id, V4ConfigRequest r, V4Store store, CancellationToken ct) =>
        { var a = await store.ConfigureAsync(id, r, ct); return Results.Ok(new { a.Id, a.Version }); });
        group.MapPut("/assessments/{id}/records", async (string id, V4RecordRequest r, V4Store store, CancellationToken ct) =>
        { var a = await store.EditAsync(id, r, ct); return Results.Ok(new { a.Id, a.Version }); });
        group.MapPost("/assessments/{id}/options/test", async (string id, V4OptionRequest r, V4Store store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } a ? Results.Ok(V4CalculationEngine.TestOption(a, r)) : Results.NotFound());
        group.MapPost("/assessments/{id}/outcomes", async (string id, V4OutcomeRequest r, V4Store store, CancellationToken ct) =>
        { var a = await store.ObserveAsync(id, r, ct); return Results.Ok(new { a.Id, a.Version }); });
        group.MapPost("/assessments/{id}/approve", () => Results.BadRequest(new { error =
            "Production approval is blocked: V4.2 pilot methods and source evidence require authorized methodology approval. Report authorization is a separate state." }));
        group.MapPost("/assessments/{id}/reports/release", () => Results.BadRequest(new { error =
            "External release and automated report generation are deferred. Draft pathway review does not authorize disclosure." }));
    }
}
