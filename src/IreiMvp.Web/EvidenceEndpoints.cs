using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace IreiMvp.Web;
public static class EvidenceEndpoints
{
    public static void AddEvidencePortal(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<EvidenceOptions>(builder.Configuration.GetSection("Evidence"));
        builder.Services.AddSingleton<EvidenceSecurity>(); builder.Services.AddSingleton<EvidenceFiles>(); builder.Services.AddSingleton<EvidenceStore>();
        builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("evidence-auth", c => RateLimitPartition.GetFixedWindowLimiter(
            c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
    }
    public static void UseEvidenceProtection(this WebApplication app)
    {
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<IOptions<EvidenceOptions>>().Value; var path = context.Request.Path;
            if (path.StartsWithSegments("/api/evidence") || path.Value?.StartsWith("/evidence", StringComparison.Ordinal) == true)
            {
                context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            }
            // The legacy analysis APIs contain internal workbooks. Require their separate staff key whenever the client portal is enabled.
            if (options.Enabled && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/evidence"))
            { var key = context.RequestServices.GetRequiredService<IOptions<IreiOptions>>().Value.AdminKey;
                if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, context.Request.Headers["X-Admin-Key"].FirstOrDefault(), StringComparison.Ordinal)) { context.Response.StatusCode = 403; return; } }
            await next(context);
        });
    }
    private static EvidenceUser User(HttpContext context) => context.Items["EvidenceUser"] as EvidenceUser ?? throw new EvidenceDeniedException();
    public static void MapEvidencePortal(this WebApplication app)
    {
        var g = app.MapGroup("/api/evidence");
        g.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; var security = http.RequestServices.GetRequiredService<EvidenceSecurity>();
            if (!security.Options.Enabled) return Results.NotFound();
            if (!http.Request.IsHttps && !security.LocalHttp(http)) return Results.Json(new { error = "Use HTTPS. HTTP is allowed only for an explicitly configured local synthetic prototype." }, statusCode: 400);
            if (!http.Request.Path.StartsWithSegments("/api/evidence/uploads") && http.Request.ContentLength > 256 * 1024) return Results.Json(new { error = "Request exceeds the metadata limit." }, statusCode: 413);
            try
            {
                if (http.Request.Method is not ("GET" or "HEAD") && !security.ValidCsrf(http)) return Results.Json(new { error = "Session confirmation expired. Refresh and retry." }, statusCode: 403);
                var path = http.Request.Path.Value ?? "";
                var publicRoute = path is "/api/evidence/session" or "/api/evidence/login" or "/api/evidence/accept-invitation";
                var store = http.RequestServices.GetRequiredService<EvidenceStore>(); var user = await store.Current(security.Session(http), http.RequestAborted);
                if (!publicRoute && user is null) return Results.Unauthorized(); http.Items["EvidenceUser"] = user; return await next(context);
            }
            catch (EvidenceDeniedException) { return Results.NotFound(new { error = "Resource unavailable in your authorized scope." }); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        g.MapGet("/session", async (HttpContext c, EvidenceStore store, EvidenceSecurity security, CancellationToken ct) =>
        { var user = c.Items["EvidenceUser"] as EvidenceUser; return Results.Ok(new { Authenticated = user is not null, Csrf = security.Session(c)?.Csrf ?? security.Preauth(c), Profile = user is null ? null : await store.SessionView(user, ct), security.Options.Prototype, security.Options.RequireAdminMfa, Limits = new { security.Options.FileLimitBytes, security.Options.BatchLimitBytes, security.Options.BatchFileLimit } }); });
        g.MapPost("/login", async (EvidenceLogin input, HttpContext c, EvidenceStore store, EvidenceSecurity security, CancellationToken ct) =>
        { var u = await store.Login(input, ct); if (u is null) return Results.Json(new { error = security.Options.RequireAdminMfa ? "Sign-in failed. Check credentials/authenticator or wait if temporarily locked." : "Sign-in failed. Check your email/password or wait if temporarily locked." }, statusCode: 401); security.SignIn(c, u); return Results.Ok(new { SignedIn = true }); }).RequireRateLimiting("evidence-auth");
        g.MapPost("/accept-invitation", async (EvidenceAcceptInvite input, HttpContext c, EvidenceStore store, EvidenceSecurity security, CancellationToken ct) =>
        { var u = await store.Accept(input, ct); if (u is null) return Results.BadRequest(new { error = "Invitation expired, already used or unavailable." }); security.SignIn(c, u); return Results.Ok(new { SignedIn = true }); }).RequireRateLimiting("evidence-auth");
        g.MapPost("/logout", async (HttpContext c, EvidenceStore store, CancellationToken ct) => { await store.SignOut(User(c), ct); c.Response.Cookies.Delete("irei-evidence", new CookieOptions { Path = "/", Secure = c.Request.IsHttps, SameSite = SameSiteMode.Strict }); return Results.Ok(new { SignedOut = true }); });
        g.MapPost("/tenants", async (EvidenceTenant input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.CreateTenant(User(c), input, ct)));
        g.MapGet("/tenants/{tenant}", async (string tenant, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Workspace(User(c), tenant, ct)));
        g.MapPut("/tenants/{tenant}/policy", async (string tenant, EvidencePolicy input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.SetPolicy(User(c), tenant, input, ct)));
        g.MapPost("/invitations", async (EvidenceInviteRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Invite(User(c), input, ct)));
        g.MapGet("/tenants/{tenant}/users", async (string tenant, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Users(User(c), tenant, ct)));
        g.MapDelete("/tenants/{tenant}/users/{userId}", async (string tenant, string userId, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Revoke(User(c), tenant, userId, ct)));
        g.MapPost("/tenants/{tenant}/properties", async (string tenant, EvidenceProperty input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.AddProperty(User(c), tenant, input, ct)));
        g.MapPost("/uploads/prepare", async (EvidenceReservation input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Prepare(User(c), input, ct)));
        g.MapPost("/uploads/{reservation}/{index:int}", async (string reservation, int index, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Receive(User(c), reservation, index, c.Request.Body, c.Request.ContentLength, ct)));
        g.MapPost("/uploads/{reservation}/cancel", async (string reservation, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.CancelReservation(User(c), reservation, ct)));
        g.MapPut("/documents/{document}", async (string document, EvidenceMetadata input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Metadata(User(c), document, input, ct)));
        g.MapPost("/tenants/{tenant}/categories/{category}/{scope}", async (string tenant, string category, string scope, EvidenceStatusRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.CategoryStatus(User(c), tenant, category, scope, input, ct)));
        g.MapPost("/tenants/{tenant}/ready/{scope}", async (string tenant, string scope, EvidenceStatusRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Ready(User(c), tenant, scope, input.Note, ct)));
        g.MapPost("/tenants/{tenant}/references", async (string tenant, EvidenceReferenceInput input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.RegisterReference(User(c), tenant, input.Scope, input.Category, input.Reference, input.Label, ct)));
        g.MapPost("/versions/{version}/unavailable", async (string version, EvidenceStatusRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.MarkUnavailable(User(c), version, input.Note, ct)));
        g.MapPost("/versions/{version}/rescan", async (string version, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Rescan(User(c), version, ct)));
        g.MapPost("/versions/{version}/validation", async (string version, EvidenceStatusRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.ReviewVersion(User(c), version, input, ct)));
        g.MapPost("/versions/{version}/delete", async (string version, EvidenceDeleteRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.DeleteSource(User(c), version, input, ct)));
        g.MapGet("/metrics", () => Results.Ok(EvidenceMetrics.All));
        g.MapPost("/tenants/{tenant}/facts", async (string tenant, EvidenceFact input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.AddFact(User(c), tenant, input, ct)));
        g.MapPost("/facts/{fact}/review", async (string fact, EvidenceFactReview input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.ReviewFact(User(c), fact, input, ct)));
        g.MapPost("/versions/{version}/import", async (string version, HttpContext c, EvidenceStore store, IntakeExtractor extractor, CancellationToken ct) => Results.Ok(await store.ImportSpreadsheet(User(c), version, extractor, ct)));
        g.MapPost("/tenants/{tenant}/issues", async (string tenant, EvidenceIssue input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.AddIssue(User(c), tenant, input, ct)));
        g.MapPost("/issues/{issue}/response", async (string issue, EvidenceIssueResponse input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.RespondIssue(User(c), issue, input.Response, input.Resolve, ct, input.Availability)));
        g.MapPost("/tenants/{tenant}/snapshots", async (string tenant, EvidenceSnapshot input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Freeze(User(c), tenant, input, ct)));
        g.MapGet("/snapshots/{snapshot}", async (string snapshot, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Snapshot(User(c), snapshot, ct)));
        g.MapPost("/snapshots/validate", async (EvidenceValidationRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.ValidateSnapshot(User(c), input, ct)));
        g.MapPost("/snapshots/{snapshot}/publish", async (string snapshot, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.Publish(User(c), snapshot, ct)));
        g.MapGet("/tenants/{tenant}/facts.csv", async (string tenant, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.File(await store.ExportFacts(User(c), tenant, ct), "text/csv", "validated-stage1-data.csv"));
        g.MapGet("/tenants/{tenant}/register.csv", async (string tenant, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.File(await store.RegisterCsv(User(c), tenant, ct), "text/csv", "evidence-register.csv"));
        g.MapGet("/tenants/{tenant}/audit", async (string tenant, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.AuditLog(User(c), tenant, ct)));
        g.MapPost("/downloads", async (EvidenceDownloadRequest input, HttpContext c, EvidenceStore store, CancellationToken ct) => Results.Ok(await store.PrepareDownload(User(c), input, ct)));
        g.MapGet("/downloads/{token}", async (string token, HttpContext c, EvidenceStore store, CancellationToken ct) =>
        { var payload = await store.ConsumeDownload(User(c), token, ct); if (c.Features.Get<IHttpBodyControlFeature>() is { } control) control.AllowSynchronousIO = true;
            return Results.Stream(stream => store.WriteDownload(payload, stream, ct), payload.Archive ? "application/zip" : "application/octet-stream", payload.Archive ? "evidence-part.zip" : payload.Versions.Single().Name); });
    }
    public sealed record EvidenceReferenceInput(string Scope, string Category, string Reference, string Label);
    public sealed record EvidenceIssueResponse(string Response, bool Resolve, string Availability = "Not stated");
}
