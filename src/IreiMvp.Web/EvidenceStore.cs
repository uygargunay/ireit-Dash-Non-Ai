using System.Globalization;
using System.IO.Compression;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class EvidenceDeniedException : Exception;
public sealed partial class EvidenceStore(EvidenceSecurity security, EvidenceFiles files, IOptions<EvidenceOptions> options, IWebHostEnvironment env)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly EvidenceOptions config = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string DummyPassword = EvidenceSecurity.Password(Guid.NewGuid().ToString("N"));
    private string Root
    {
        get
        {
            var root = Path.GetFullPath(config.StoragePath, env.ContentRootPath);
            var web = Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"));
            if ((root + Path.DirectorySeparatorChar).StartsWith(web + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Evidence storage must be outside the public web root.");
            return root;
        }
    }
    public static bool Staff(EvidenceUser u) => u.Role is "IreiAnalyst" or "IreiAdmin";
    private static bool Admin(EvidenceUser u) => u.Role == "IreiAdmin";
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private async Task<EvidenceState> Load(CancellationToken ct)
    {
        var path = Path.Combine(Root, "state.enc");
        if (File.Exists(path)) return JsonSerializer.Deserialize<EvidenceState>(security.Decrypt(await File.ReadAllBytesAsync(path, ct), "state"), Json) ?? throw new InvalidDataException("Evidence state unavailable.");
        if (Convert.FromBase64String(config.EncryptionKey).Length != 32 || string.IsNullOrWhiteSpace(config.BootstrapEmail) || (config.RequireAdminMfa && config.BootstrapTotpSecret.Length < 32))
            throw new InvalidOperationException("Configure a 32-byte encryption key and bootstrap email/password before enabling the portal. A TOTP secret is also required when RequireAdminMfa is enabled.");
        if (config.FileLimitBytes <= 0 || config.FileLimitBytes > 20 * 1024 * 1024 || config.BatchLimitBytes <= 0 || config.BatchFileLimit is < 1 or > 50 || config.TenantQuotaBytes <= 0 || config.GlobalQuotaBytes <= 0)
            throw new InvalidOperationException("Evidence quota configuration is invalid.");
        Directory.CreateDirectory(Root);
        return new EvidenceState { Users = [new() { Email = Email(config.BootstrapEmail), PasswordHash = EvidenceSecurity.Password(config.BootstrapPassword),
            TotpSecret = config.BootstrapTotpSecret, Role = "IreiAdmin" }] };
    }
    private async Task Save(EvidenceState s, CancellationToken ct)
    {
        Directory.CreateDirectory(Root); var temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(s, Json);
        var otherBytes = Directory.EnumerateFiles(Root, "*.enc", SearchOption.AllDirectories).Where(p => p != Path.Combine(Root, "state.enc")).Sum(p => new FileInfo(p).Length);
        if (bytes.Length > 64 * 1024 * 1024 || bytes.Length + 29 + otherBytes > config.GlobalQuotaBytes) throw new InvalidDataException("Evidence metadata/storage capacity reached; arrange a reviewed capacity extension.");
        try { await File.WriteAllBytesAsync(temp, security.Encrypt(bytes, "state"), ct); File.Move(temp, Path.Combine(Root, "state.enc"), true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private async Task<T> Transaction<T>(Func<EvidenceState, Task<T>> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var s = await Load(ct); var result = await action(s); await Save(s, ct); return result; }
        finally { gate.Release(); }
    }
    private Task<T> Transaction<T>(Func<EvidenceState, T> action, CancellationToken ct) => Transaction(s => Task.FromResult(action(s)), ct);
    private static string Email(string email)
    { try { var parsed = new MailAddress(email.Trim()); if (parsed.Address != email.Trim()) throw new FormatException(); return parsed.Address.ToLowerInvariant(); } catch { throw new InvalidDataException("Enter a valid email address."); } }
    private static EvidenceUser Actor(EvidenceState s, EvidenceUser actor)
    { var u = s.Users.SingleOrDefault(u => u.Id == actor.Id && u.Active && u.SecurityVersion == actor.SecurityVersion); return u ?? throw new EvidenceDeniedException(); }
    private static bool Scope(EvidenceUser u, string tenant, string scope) => u.Memberships.Any(m => m.TenantId == tenant && (m.Scopes.Contains("*") || m.Scopes.Contains(scope)));
    private static EvidenceTenant Tenant(EvidenceState s, EvidenceUser u, string id)
    { return s.Tenants.SingleOrDefault(t => t.Id == id && u.Memberships.Any(m => m.TenantId == id)) ?? throw new EvidenceDeniedException(); }
    private static EvidenceDocument Document(EvidenceState s, EvidenceUser u, string id)
    { return s.Documents.SingleOrDefault(d => d.Id == id && Scope(u, d.TenantId, d.ScopeId)) ?? throw new EvidenceDeniedException(); }
    private static EvidenceVersion Version(EvidenceState s, EvidenceUser u, string id)
    { var v = s.Versions.SingleOrDefault(v => v.Id == id) ?? throw new EvidenceDeniedException(); Document(s, u, v.DocumentId); return v; }
    private static void NeedStaff(EvidenceUser u) { if (!Staff(u)) throw new EvidenceDeniedException(); }
    private static void NeedAdmin(EvidenceUser u) { if (!Admin(u)) throw new EvidenceDeniedException(); }
    private static void NeedTenantAdmin(EvidenceUser u, string tenant)
    { NeedAdmin(u); if (!u.Memberships.Any(m => m.TenantId == tenant && m.Scopes.Contains("*"))) throw new EvidenceDeniedException(); }
    private static void Audit(EvidenceState s, EvidenceUser u, string tenant, string action, string id, string detail = "") =>
        s.Audit.Add(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, u.Id, tenant, action, id, detail));
    private static void Categories(string[] ids)
    { if (ids.Length is < 1 or > 10 || ids.Any(id => !EvidenceCategories.All.Any(c => c.Id == id))) throw new InvalidDataException("Choose valid evidence categories."); }
    private static void Properties(EvidenceState s, EvidenceUser u, string tenant, string[] ids)
    { if (ids.Distinct().Count() != ids.Length || ids.Any(id => !s.Properties.Any(p => p.Id == id && p.TenantId == tenant && Scope(u, tenant, p.ScopeId)))) throw new EvidenceDeniedException(); }
    private static void ScopeAllowed(EvidenceTenant t, EvidenceUser u, string scope)
    { if (!t.ScopeIds.Contains(scope) || !Scope(u, t.Id, scope)) throw new EvidenceDeniedException(); }
    private static long Stored(EvidenceState s, string? tenant = null) => s.Versions.Where(v => v.DeletedAt is null && v.Hash.Length > 0 && (tenant is null || s.Documents.Any(d => d.Id == v.DocumentId && d.TenantId == tenant)))
        .GroupBy(v => (s.Documents.Single(d => d.Id == v.DocumentId).TenantId, v.Hash)).Sum(g => g.First().Size + 29);
    private static long Reserved(EvidenceState s, string? tenant = null) => s.Reservations.Where(r => r.Expires > DateTimeOffset.UtcNow && (tenant is null || r.TenantId == tenant))
        .SelectMany(r => r.Files).Where(f => f.State is "Queued" or "Failed").Sum(f => f.Size + 29);
    private long PhysicalBytes(string? tenant = null)
    { var folder = tenant is null ? Root : Path.Combine(Root, "sources", tenant); return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) : 0; }
    private string BlobPath(string tenant, string hash) => Path.Combine(Root, "sources", tenant, hash + ".enc");
    public async Task Initialize(CancellationToken ct) { await Transaction(s => true, ct); }
    public Task<EvidenceUser?> Current(EvidenceSession? session, CancellationToken ct)
    {
        if (session is null) return Task.FromResult<EvidenceUser?>(null);
        return Transaction(s => s.Users.SingleOrDefault(u => u.Id == session.UserId && u.Active && u.SecurityVersion == session.SecurityVersion), ct);
    }
    public Task<EvidenceUser?> Login(EvidenceLogin login, CancellationToken ct) => Transaction(s =>
    {
        var u = s.Users.FirstOrDefault(u => string.Equals(u.Email, login.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        var matches = EvidenceSecurity.PasswordMatches(login.Password, u?.PasswordHash ?? DummyPassword);
        if (u is null) return null;
        var requireMfa = config.RequireAdminMfa && Admin(u);
        var otp = requireMfa ? EvidenceSecurity.TotpCounter(u.TotpSecret, login.Totp) : 0;
        if (!u.Active || u.LockedUntil > DateTimeOffset.UtcNow || !matches || (requireMfa && (otp < 0 || otp <= u.LastTotpCounter)))
        { u.FailedLogins++; if (u.FailedLogins >= 5) u.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15); Audit(s, u, "", "Sign-in failed", u.Id); return null; }
        u.FailedLogins = 0; u.LockedUntil = null; if (requireMfa) u.LastTotpCounter = otp;
        Audit(s, u, "", "Signed in", u.Id); return Clone(u);
    }, ct);
    public Task<object> SessionView(EvidenceUser actor, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); return (object)new { u.Id, u.Email, u.Role, Tenants = s.Tenants.Where(t => u.Memberships.Any(m => m.TenantId == t.Id)).Select(t => new { t.Id, t.Name, t.PilotScope, t.RepresentativeSample, Scopes = t.ScopeIds.Where(sc => Scope(u, t.Id, sc)) }).ToArray() }; }, ct);
    public Task<EvidenceTenant> CreateTenant(EvidenceUser actor, EvidenceTenant input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedAdmin(u);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.PilotScope) || !DateOnly.TryParseExact(input.ReportingCutoff, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new InvalidDataException("Organization, pilot scope and ISO reporting cut-off required.");
        if (input.ScopeIds.Length is < 1 or > 50 || input.ScopeIds.Any(sc => string.IsNullOrWhiteSpace(sc) || sc.Length > 100 || sc == "*")) throw new InvalidDataException("Provide one or more named submission scopes.");
        var t = new EvidenceTenant { Name = input.Name, PilotScope = input.PilotScope, ReportingCutoff = input.ReportingCutoff, RepresentativeSample = input.RepresentativeSample,
            ScopeIds = input.ScopeIds.Distinct().ToArray(), FiscalYearEnd = input.FiscalYearEnd, Contact = input.Contact };
        s.Tenants.Add(t); u.Memberships.Add(new(t.Id, ["*"])); Audit(s, u, t.Id, "Organization created", t.Id); return Clone(t);
    }, ct);
    public Task<EvidencePolicy> SetPolicy(EvidenceUser actor, string tenant, EvidencePolicy policy, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedTenantAdmin(u, tenant); var t = Tenant(s, u, tenant);
        if (policy.SourceRetentionDays is < 1 or > 3650 || policy.DerivedRetentionDays is < 1 or > 36500 || string.IsNullOrWhiteSpace(policy.AgreementReference)) throw new InvalidDataException("Agreement reference and bounded retention periods required.");
        s.PolicyHistory.Add(new(t.Id, Clone(t.Policy), u.Id, DateTimeOffset.UtcNow));
        policy.ApprovedBy = u.Id; policy.ApprovedAt = DateTimeOffset.UtcNow; t.Policy = Clone(policy); Audit(s, u, tenant, "Policy recorded", t.Id); return Clone(t.Policy);
    }, ct);
    public Task<object> Invite(EvidenceUser actor, EvidenceInviteRequest input, CancellationToken ct) => Transaction(s =>
    {
        var u = Actor(s, actor); NeedTenantAdmin(u, input.TenantId); var t = Tenant(s, u, input.TenantId);
        if (input.Role is not ("ClientContributor" or "ClientApprover" or "IreiAnalyst" or "IreiAdmin") || input.Scopes.Length == 0 || input.Scopes.Any(sc => sc == "*" ? !u.Memberships.Any(m => m.TenantId == t.Id && m.Scopes.Contains("*")) : !t.ScopeIds.Contains(sc) || !Scope(u, t.Id, sc))) throw new EvidenceDeniedException();
        var email = Email(input.Email);
        var token = EvidenceSecurity.Token(); var invitation = new EvidenceInvitation { TokenHash = EvidenceSecurity.Hash(token), Email = email, TenantId = t.Id, Role = input.Role, Scopes = input.Scopes };
        s.Invitations.Add(invitation); Audit(s, u, t.Id, "Invitation created", invitation.Id);
        return (object)new { invitation.Id, Token = token, invitation.Expires, Instruction = "Deliver this one-time invitation through your approved channel. It is not a public upload link." };
    }, ct);
    public Task<EvidenceUser?> Accept(EvidenceAcceptInvite input, CancellationToken ct) => Transaction(s =>
    {
        var invite = s.Invitations.SingleOrDefault(i => i.TokenHash == EvidenceSecurity.Hash(input.Token) && !i.Used && i.Expires > DateTimeOffset.UtcNow);
        if (invite is null) return null;
        var existing = s.Users.SingleOrDefault(u => u.Email == invite.Email);
        if (existing is not null)
        {
            if (!existing.Active || existing.Role != invite.Role || !EvidenceSecurity.PasswordMatches(input.Password, existing.PasswordHash)) return null;
            if (config.RequireAdminMfa && Admin(existing)) { var current = EvidenceSecurity.TotpCounter(existing.TotpSecret, input.Totp); if (current < 0 || current <= existing.LastTotpCounter) return null; existing.LastTotpCounter = current; }
            existing.Memberships.RemoveAll(m => m.TenantId == invite.TenantId); existing.Memberships.Add(new(invite.TenantId, invite.Scopes)); existing.SecurityVersion++;
            invite.Used = true; Audit(s, existing, invite.TenantId, "Additional organization invitation accepted", invite.Id); return Clone(existing);
        }
        var otp = config.RequireAdminMfa && invite.Role == "IreiAdmin" ? EvidenceSecurity.TotpCounter(input.TotpSecret, input.Totp) : 0;
        if (otp < 0) throw new InvalidDataException("Administrator enrolment requires a valid authenticator secret and current code.");
        var u = new EvidenceUser { Email = invite.Email, Role = invite.Role, PasswordHash = EvidenceSecurity.Password(input.Password), TotpSecret = invite.Role == "IreiAdmin" ? input.TotpSecret : "", LastTotpCounter = otp, Memberships = [new(invite.TenantId, invite.Scopes)] };
        s.Users.Add(u); invite.Used = true; Audit(s, u, invite.TenantId, "Invitation accepted", invite.Id); return Clone(u);
    }, ct);
    public Task<bool> SignOut(EvidenceUser actor, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); u.SecurityVersion++; Audit(s, u, "", "Signed out; prior sessions invalidated", u.Id); return true; }, ct);
    public Task<object> Users(EvidenceUser actor, string tenant, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedTenantAdmin(u, tenant); Tenant(s, u, tenant); return (object)s.Users.Where(x => x.Memberships.Any(m => m.TenantId == tenant)).Select(x => new { x.Id, x.Email, x.Role, x.Active, Memberships = x.Memberships.Where(m => m.TenantId == tenant) }).ToArray(); }, ct);
    public Task<bool> Revoke(EvidenceUser actor, string tenant, string userId, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedTenantAdmin(u, tenant); Tenant(s, u, tenant); var other = s.Users.SingleOrDefault(x => x.Id == userId && x.Memberships.Any(m => m.TenantId == tenant)) ?? throw new EvidenceDeniedException();
        if (other.Id == u.Id) throw new InvalidDataException("Use a second administrator to revoke your own access.");
        other.Memberships.RemoveAll(m => m.TenantId == tenant); other.SecurityVersion++; Audit(s, u, tenant, "Organization access revoked", other.Id); return true;
    }, ct);
    public Task<EvidenceProperty> AddProperty(EvidenceUser actor, string tenant, EvidenceProperty input, CancellationToken ct) => Transaction(s =>
    { var u = Actor(s, actor); NeedStaff(u); var t = Tenant(s, u, tenant); ScopeAllowed(t, u, input.ScopeId);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.ClientPropertyId)) throw new InvalidDataException("Property name and client property ID required.");
        var source = Version(s, u, input.EvidenceVersionId); var document = Document(s, u, source.DocumentId);
        if (document.TenantId != tenant || document.ScopeId != input.ScopeId) throw new EvidenceDeniedException();
        if (string.IsNullOrWhiteSpace(input.Locator) || string.IsNullOrWhiteSpace(input.ReportingPeriod)) throw new InvalidDataException("Property source location and reporting period required.");
        if (s.Properties.Any(p => p.TenantId == tenant && p.ScopeId == input.ScopeId && p.ClientPropertyId == input.ClientPropertyId)) throw new InvalidDataException("Client property ID already exists in this scope; use its stable ID.");
        var p = new EvidenceProperty { TenantId = tenant, ScopeId = input.ScopeId, ClientPropertyId = input.ClientPropertyId, Name = input.Name, Address = input.Address, AssetType = input.AssetType, OwnershipInterest = input.OwnershipInterest, Program = input.Program, InScope = input.InScope,
            EvidenceVersionId = source.Id, Locator = input.Locator, ReportingPeriod = input.ReportingPeriod };
        s.Properties.Add(p); Audit(s, u, tenant, "Property created", p.Id); return Clone(p);
    }, ct);
}
