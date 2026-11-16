namespace IreiMvp.Web;

public static class BoardEndpoints
{
    public static void MapBoard(this WebApplication app)
    {
        var group = app.MapGroup("/api/submissions/board");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        group.MapPost("/assessments", async (HttpRequest request, BoardStore store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Select Excel files to upload." });
            var form = await request.ReadFormAsync(ct);
            var result = await store.Import(form.Files, form["organizationName"].FirstOrDefault() ?? "", form["currentYear"].FirstOrDefault() ?? "", ct);
            return Results.Ok(new { result.Assessment.Id, result.Assessment.Version });
        });
        group.MapGet("/assessments/{id}", async (string id, BoardStore store, CancellationToken ct) =>
            await store.Get(id, ct) is { } record ? Results.Ok(BoardView.Build(record)) : Results.NotFound());
        group.MapPut("/assessments/{id}/configuration", async (string id, V4ConfigRequest request, BoardStore store, CancellationToken ct) =>
            Results.Ok(BoardView.Build(await store.Configure(id, request, ct))));
        group.MapPost("/assessments/{id}/actions", async (string id, BoardActionRequest request, BoardStore store, CancellationToken ct) =>
            Results.Ok(BoardView.Build(await store.AddAction(id, request, ct))));
        group.MapPost("/assessments/{id}/options/test", async (string id, V4OptionRequest request, BoardStore store, CancellationToken ct) =>
            await store.Get(id, ct) is { } record ? Results.Ok(V4CalculationEngine.TestOption(record.Assessment, request)) : Results.NotFound());
        group.MapGet("/assessments/{id}/metrics/{metricId}", async (string id, string metricId, BoardStore store, CancellationToken ct) =>
        {
            var record = await store.Get(id, ct); var a = record?.Assessment; var metric = a?.Metrics.FirstOrDefault(m => m.Id == metricId);
            return a is null || metric is null ? Results.NotFound() : Results.Ok(new { Metric = metric,
                Facts = a.Facts.Where(f => metric.InputIds.Contains(f.Id)), Sources = record!.Sources.Select(s => new { s.Name, s.Sha256, s.Kind }),
                Explanation = "Server-calculated result. Source-plan, review, conflict and missing states are preserved. Source numeric observations are not independently verified actuals." });
        });
    }
}
