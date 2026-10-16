using Microsoft.Extensions.Options;

namespace IreiMvp.Web;
public static class IntakeEndpoints
{
    public static void MapIntake(this WebApplication app)
    {
        var g = app.MapGroup("/api/v4/intake");
        g.AddEndpointFilter(async (context, next) =>
        {
            // Explicit opt-in: this is a trusted-staff single-instance demo, not a customer multi-tenant portal.
            if (!app.Configuration.GetValue<bool>("Irei:IntakeEnabled")) return Results.NotFound();
            var key = context.HttpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<IreiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.AdminKey) || key != options.AdminKey) return Results.Unauthorized();
            try { return await next(context); }
            catch (Exception e) when (e is InvalidDataException or FileNotFoundException or DirectoryNotFoundException) { return Results.BadRequest(new { error = e.Message }); }
        });
        g.MapPost("/batches", async (IntakeBatch b, IntakeStore store, CancellationToken ct) => Results.Ok(await store.Create(b, ct)));
        g.MapGet("/batches/{id}", async (string id, IntakeStore store, CancellationToken ct) => Results.Ok(await store.Get(id, ct)));
        g.MapPost("/batches/{id}/files", async (string id, HttpRequest request, IntakeStore store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Upload multipart XLSX/CSV files." });
            var files = (await request.ReadFormAsync(ct)).Files;
            if (files.Count is < 1 or > 10) return Results.BadRequest(new { error = "Choose one to ten files." });
            IntakeBatch? b = null;
            foreach (var file in files) { await using var stream = file.OpenReadStream(); b = await store.Upload(id, stream, file.FileName, ct); }
            return Results.Ok(b);
        });
        g.MapPost("/batches/{id}/candidates/{candidateId}/review", async (string id, string candidateId, IntakeReview r, IntakeStore store, CancellationToken ct) => Results.Ok(await store.Review(id, candidateId, r, ct)));
        g.MapGet("/batches/{id}/sources/{sourceId}/preview", async (string id, string sourceId, string sheet, int row, int column, IntakeStore store, CancellationToken ct) => Results.Ok(await store.Preview(id, sourceId, sheet, row, column, ct)));
        g.MapPost("/batches/{id}/effort", async (string id, IntakeEffort entry, IntakeStore store, CancellationToken ct) => Results.Ok(await store.AddEffort(id, entry, ct)));
        g.MapPost("/batches/{id}/commit", async (string id, IntakeCommit request, IntakeStore store, CancellationToken ct) => Results.Ok(await store.Commit(id, request.ExpectedVersion, ct)));
        g.MapGet("/batches/{id}/export", async (string id, IntakeStore store, CancellationToken ct) =>
        {
            var b = await store.Get(id, ct);
            if (b.ExportedVersion != b.Version) return Results.BadRequest(new { error = "Commit the current reviewed selection first." });
            return Results.File(store.ExportPath(id), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "reviewed-intake.xlsx");
        });
        g.MapGet("/batches/{id}/manifest", async (string id, IntakeStore store, CancellationToken ct) =>
        { var b = await store.Get(id, ct); return b.ExportedVersion == b.Version ? Results.File(store.ExportPath(id) + ".manifest.json", "application/json", "intake-manifest.json") : Results.BadRequest(new { error = "Commit current selection first." }); });
    }
    public sealed record IntakeCommit(int ExpectedVersion);
}
