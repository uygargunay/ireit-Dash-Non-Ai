namespace IreiMvp.Web;

public sealed class EvidenceOptions
{
    public bool Enabled { get; set; }
    public bool RequireAdminMfa { get; set; }
    public bool Prototype { get; set; } = true;
    public bool AllowLocalHttp { get; set; }
    public bool InfrastructureSpendBlocked { get; set; }
    public string StoragePath { get; set; } = "App_Data/EvidencePortal";
    public string EncryptionKey { get; set; } = "";
    public string BootstrapEmail { get; set; } = "";
    public string BootstrapPassword { get; set; } = "";
    public string BootstrapTotpSecret { get; set; } = "";
    public int ScannerPort { get; set; } = 3310;
    public long FileLimitBytes { get; set; } = 10 * 1024 * 1024;
    public long BatchLimitBytes { get; set; } = 40 * 1024 * 1024;
    public long TenantQuotaBytes { get; set; } = 250 * 1024 * 1024;
    public long GlobalQuotaBytes { get; set; } = 1024 * 1024 * 1024;
    public int BatchFileLimit { get; set; } = 25;
}

public sealed class EvidenceState
{
    public List<EvidenceUser> Users { get; set; } = [];
    public List<EvidenceInvitation> Invitations { get; set; } = [];
    public List<EvidenceTenant> Tenants { get; set; } = [];
    public List<EvidenceDocument> Documents { get; set; } = [];
    public List<EvidenceVersion> Versions { get; set; } = [];
    public List<EvidenceProperty> Properties { get; set; } = [];
    public List<EvidenceFact> Facts { get; set; } = [];
    public List<EvidenceIssue> Issues { get; set; } = [];
    public List<EvidenceSnapshot> Snapshots { get; set; } = [];
    public List<EvidenceAudit> Audit { get; set; } = [];
    public List<EvidenceDownload> Downloads { get; set; } = [];
    public List<EvidenceReservation> Reservations { get; set; } = [];
    public List<EvidenceSignoff> Signoffs { get; set; } = [];
    public List<EvidencePublication> Publications { get; set; } = [];
    public List<EvidenceDocumentRevision> DocumentHistory { get; set; } = [];
    public List<EvidencePolicyRevision> PolicyHistory { get; set; } = [];
}
public sealed class EvidenceUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    public long LastTotpCounter { get; set; } = -1;
    public string Role { get; set; } = "ClientContributor";
    public bool Active { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public int SecurityVersion { get; set; } = 1;
    public List<EvidenceMembership> Memberships { get; set; } = [];
}
public sealed record EvidenceMembership(string TenantId, string[] Scopes);
public sealed class EvidenceInvitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TokenHash { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddDays(2);
    public bool Used { get; set; }
}
public sealed class EvidenceTenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string PilotScope { get; set; } = "";
    public string ReportingCutoff { get; set; } = "";
    public string FiscalYearEnd { get; set; } = "";
    public string Contact { get; set; } = "";
    public bool RepresentativeSample { get; set; }
    public string[] ScopeIds { get; set; } = ["organization"];
    public EvidencePolicy Policy { get; set; } = new();
    public List<EvidenceCategoryState> CategoryStates { get; set; } = [];
    public List<EvidenceSubmission> Submissions { get; set; } = [];
}
public sealed class EvidencePolicy
{
    public string AgreementReference { get; set; } = "";
    public string HostingLocation { get; set; } = "";
    public string ProcessorsAndBackups { get; set; } = "";
    public string ClientSourceLocation { get; set; } = "";
    public string SecondaryUses { get; set; } = "None authorized";
    public int SourceRetentionDays { get; set; } = 30;
    public int DerivedRetentionDays { get; set; } = 730;
    public bool WrittenAuthority { get; set; }
    public bool SecurityApproved { get; set; }
    public bool BackupReviewed { get; set; }
    public bool ClientPreservesExactVersions { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
}
public sealed record EvidenceCategory(string Id, string Title, string Description, string Examples, string Priority);
public static class EvidenceCategories
{
    public static readonly EvidenceCategory[] All = [
        new("01", "Organization and governance", "Purpose, mission, governance and strategic direction.", "Strategic plan, organization chart, annual report and policies", "P1"),
        new("02", "Financial statements and budgets", "Establish the financial baseline and historical trends.", "Three years of audited statements and notes, YTD reports, approved budget and exports", "P1"),
        new("03", "Portfolio and property inventory", "Identify properties and build consistent property IDs.", "Addresses, asset IDs, ownership, units or beds and portfolio register", "P1"),
        new("04", "Operating performance and revenue", "Understand property performance and portfolio sustainability.", "Income and expenses, rent summaries, vacancy, utility and maintenance trends", "P1"),
        new("05", "Affordability mission and services", "Interpret performance against mission and program obligations.", "Rent bands, affordability restrictions and aggregate non-identifying service metrics", "P1"),
        new("06", "Debt financing and guarantees", "Identify payment, maturity and covenant exposures.", "Mortgage schedules, loan statements, rates, maturities and covenant summaries", "P1"),
        new("07", "Reserves and restricted funds", "Distinguish available and restricted cash and future obligations.", "Reserve balances, policies, contributions, draws and restrictions", "P1"),
        new("08", "Building condition and capital needs", "Assess physical needs and long-term reinvestment.", "Building condition assessments, backlog, capital plans and renewal budgets", "P1"),
        new("09", "Agreements and legal restrictions", "Identify restrictions on ownership, operations and financing.", "Agreement inventory and expiries first, then relevant leases and covenants", "P2"),
        new("10", "Development acquisition and future plans", "Capture pipeline and proposed capital commitments.", "Feasibility studies, project pipeline, capital budgets and redevelopment plans", "P1")
    ];
}
public sealed record EvidenceCategoryState(string CategoryId, string ScopeId, string State, string Note, string Owner, DateTimeOffset UpdatedAt);
public sealed record EvidenceSubmission(string ScopeId, string UserId, DateTimeOffset Date, string Note);
public sealed class EvidenceDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "organization";
    public string Label { get; set; } = "";
    public string[] CategoryIds { get; set; } = [];
    public string[] PropertyIds { get; set; } = [];
    public string ReportingPeriod { get; set; } = "";
    public string[] ReportingPeriods { get; set; } = [];
    public string Coverage { get; set; } = "Partial";
    public string Notes { get; set; } = "";
    public string SourceMode { get; set; } = "TemporaryUpload";
    public string SourceReference { get; set; } = "";
}
public sealed class EvidenceVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Extension { get; set; } = "";
    public string Mime { get; set; } = "";
    public string Hash { get; set; } = "";
    public string? SupersedesId { get; set; }
    public int Number { get; set; } = 1;
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DocumentDate { get; set; } = "";
    public string ReportingPeriod { get; set; } = "";
    public string RevisionReason { get; set; } = "";
    public string AgreementReference { get; set; } = "";
    public string Processing { get; set; } = "Scan pending";
    public string Validation { get; set; } = "Unreviewed";
    public string Retention { get; set; } = "Temporary copy";
    public string Message { get; set; } = "";
    public DateTimeOffset? RetentionDue { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
public sealed class EvidenceProperty
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "organization";
    public string ClientPropertyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string AssetType { get; set; } = "";
    public string OwnershipInterest { get; set; } = "";
    public string Program { get; set; } = "";
    public bool InScope { get; set; } = true;
    public string EvidenceVersionId { get; set; } = "";
    public string Locator { get; set; } = "";
    public string ReportingPeriod { get; set; } = "";
}
public sealed class EvidenceFact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string DocumentVersionId { get; set; } = "";
    public string PropertyId { get; set; } = "";
    public string MetricCode { get; set; } = "";
    public decimal? Value { get; set; }
    public string TextValue { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Period { get; set; } = "";
    public string AsOfDate { get; set; } = "";
    public string Basis { get; set; } = "";
    public string Locator { get; set; } = "";
    public string Method { get; set; } = "Manual";
    public string Status { get; set; } = "Unreviewed";
    public string Certainty { get; set; } = "Client-reported";
    public string Operator { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? SupersedesId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string OriginalValue { get; set; } = "";
}
public sealed class EvidenceIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "organization";
    public string VersionId { get; set; } = "";
    public string FactId { get; set; } = "";
    public string Kind { get; set; } = "Clarification required";
    public string Severity { get; set; } = "Material";
    public string Owner { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string Question { get; set; } = "";
    public string Status { get; set; } = "Open";
    public string Response { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Availability { get; set; } = "Not stated";
}
public sealed class EvidenceSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string Cutoff { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public string ReportVersion { get; set; } = "";
    public string ReportText { get; set; } = "";
    public string ScopeDescription { get; set; } = "";
    public bool RepresentativeSample { get; set; }
    public EvidenceFact[] Facts { get; set; } = [];
    public EvidenceIssue[] Exceptions { get; set; } = [];
    public EvidenceVersion[] Manifest { get; set; } = [];
    public EvidenceDocument[] Documents { get; set; } = [];
    public EvidencePolicy Policy { get; set; } = new();
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record EvidenceAudit(string Id, DateTimeOffset Date, string UserId, string TenantId, string Action, string ObjectId, string Detail);
public sealed record EvidenceDownload(string TokenHash, string UserId, string TenantId, string[] VersionIds, DateTimeOffset Expires, bool Archive);
public sealed record EvidenceSignoff(string SnapshotId, string UserId, DateTimeOffset Date, string Decision, string Note, bool AuthorizePublication);
public sealed record EvidencePublication(string SnapshotId, string UserId, DateTimeOffset Date);
public sealed record EvidenceDocumentRevision(EvidenceDocument Before, string UserId, DateTimeOffset Date);
public sealed record EvidencePolicyRevision(string TenantId, EvidencePolicy Before, string UserId, DateTimeOffset Date);
public sealed class EvidenceReservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScopeId { get; set; } = "organization";
    public string[] CategoryIds { get; set; } = [];
    public string[] PropertyIds { get; set; } = [];
    public string ReportingPeriod { get; set; } = "";
    public string DocumentDate { get; set; } = "";
    public string Coverage { get; set; } = "Partial";
    public string Notes { get; set; } = "";
    public string? ExistingDocumentId { get; set; }
    public bool DeliberateNewVersion { get; set; }
    public bool ConfirmedNoPersonalData { get; set; }
    public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
    public List<EvidenceTransfer> Files { get; set; } = [];
}
public sealed class EvidenceTransfer
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string State { get; set; } = "Queued";
    public string Message { get; set; } = "";
    public string? VersionId { get; set; }
    public string? DocumentId { get; set; }
}
public sealed record EvidenceSession(string UserId, int SecurityVersion, DateTimeOffset Expires, string Csrf);
public sealed record EvidenceLogin(string Email, string Password, string Totp = "");
public sealed record EvidenceInviteRequest(string TenantId, string Email, string Role, string[] Scopes);
public sealed record EvidenceAcceptInvite(string Token, string Password, string TotpSecret = "", string Totp = "");
public sealed record EvidenceMetadata(string[] CategoryIds, string[] PropertyIds, string ReportingPeriod, string Coverage, string Notes);
public sealed record EvidenceStatusRequest(string State, string Note);
public sealed record EvidenceFactReview(string Status, string Reason);
public sealed record EvidenceDeleteRequest(string Reason, bool Authorized, bool BackupAndRetrievalReviewed);
public sealed record EvidenceValidationRequest(string SnapshotId, string Decision, string Note, bool AuthorizePublication);
public sealed record EvidenceMetric(string Code, string Label, string Unit);
public static class EvidenceMetrics
{
    public static readonly EvidenceMetric[] All = [new("REVENUE", "Operating revenue", "Currency"), new("OPEX", "Operating expenses", "Currency"),
        new("NOI", "Net operating income", "Currency"), new("DEBT_SERVICE", "Annual debt service", "Currency"), new("CASH_BALANCE", "Cash balance", "Currency"),
        new("RESTRICTED_CASH", "Restricted cash", "Currency"), new("RESERVE_CLOSING", "Closing reserve", "Currency"), new("HOMES_TOTAL", "Homes or beds", "Count"),
        new("CAPITAL_NEEDS", "Capital needs", "Currency"), new("PROPERTY_NAME", "Property name", "Text"), new("AFFORDABILITY", "Affordability obligation", "Text")];
}
