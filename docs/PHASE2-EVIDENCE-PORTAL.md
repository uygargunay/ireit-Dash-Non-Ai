# Phase 2 evidence portal

Implements the supplied **IREI Stage 1 Phase 2 Consolidated Business Rules v2** (30 October 2026), with sections 11–18 controlling conflicts. The new entry point is `/evidence.html`; the existing V4 calculation engine and workbooks retain their calculation behavior. This is a bounded, single-instance pilot implementation. It is disabled by default and is not an approval of any hosting environment for confidential records.

## Delivered workflow

Client invitation → organization/department-scoped uploads → screening and receipts → analyst source/context review → proposed spreadsheet facts or manually indexed facts → verification (including all eligible filtered records) → working V4 dashboard assessment or optional CSV evidence export → frozen assessment → client factual confirmation → separately authorized IREI publication.

| Requirement | Implementation and boundary |
|---|---|
| Client access | Expiring one-time invitations; individual accounts; contributor, approver, analyst and administrator roles; backend tenant and department checks. No assumed access between departments. |
| Categories and completeness | Ten categories; multiple original files and category/property relations; partial/full portfolio descriptions; missing/not applicable/later responses; returning users retain upload receipts. |
| Mixed original documents | PDF, CSV, XLSX, DOCX and legacy XLS/DOC with file-signature checks. Spreadsheet/CSV and embedded-text PDF candidates; scanned PDFs need OCR and Word files receive manual source indexing. Legacy formats do not get automatic extraction. |
| Transfers and versions | Preflight capacity reservations, per-file progress, retry, persisted failed receipts, tenant-local binary deduplication, immutable version/hash, period, change reason and metadata history. Retry is whole-file; resumable transfers are deferred. |
| Security | TLS required outside explicitly enabled loopback development; email/password sign-in; administrator TOTP optional and disabled for beta; PBKDF2 passwords; encrypted authenticated sessions and CSRF checks; AES-GCM originals/metadata/snapshots; scanner failure quarantines bytes. No automatic factual acceptance. |
| Data workbench | Source-linked property inventory, common metric dictionary, source page/cell, units/currency/period/basis, original values and corrections, missing/contradictory data issues, explicit review. Unavailable is null, never inferred as zero. |
| Internal handoff | Direct reviewed-evidence V4 assessments and optional reviewed CSV for internal Excel; filtered bounded streaming ZIP archives with manifests and single-use authorized downloads. Clients cannot obtain internal exports, assumptions or archives. No live Excel synchronization. |
| Snapshots and reports | Frozen facts, source/version manifest, model/assumption/exception context; factual confirmation and publication permission separate from staff release. Withdrawing permission removes report visibility. Corrections append records. |
| Custody and retention | Temporary-copy Mode B is explicit. Client-hosted reference metadata and revocation are supported; no connector is implemented. Manual source deletion requires agreement and backup/retrieval checks, and verified facts must first be frozen. Snapshots survive source deletion; missing originals remain visibly unavailable. |
| Spending and scale | No paid services or AI dependencies added. Hard capacity limits; live intake requires operator confirmation that additional infrastructure spending is blocked. Existing hosting must independently support the required controls. |

## Beta sign-in update

At the project owner’s request, the beta uses email/password sign-in for all roles. `Evidence:RequireAdminMfa` defaults to `false`, independently of prototype/live mode. This intentionally supersedes the earlier mandatory administrator MFA requirement for the beta. Authenticator login/enrollment fields are hidden when disabled. Password checks, lockout, invitations, sessions, CSRF, tenant/department authorization and administrator permissions still apply.

For an existing SmarterASP.NET installation, deploy this update and optionally add this inside the existing `web.config` `<environmentVariables>` element:

```xml
<environmentVariable name="Evidence__RequireAdminMfa" value="false" />
```

Sign in with the existing email and password. No evidence-store reset, password reset or encryption-key change is needed. Existing saved authenticator secrets are ignored while the requirement is off; new administrators can activate without one. `Evidence__BootstrapTotpSecret` is no longer required for a fresh beta setup and may be removed from hosting configuration. Previously locked accounts retain their lockout until it expires.

Optional future MFA: set `Evidence__RequireAdminMfa=true` only after every administrator has a valid enrolled secret. A fresh MFA-enabled bootstrap also needs `Evidence__BootstrapTotpSecret` (at least 32 Base32 characters). Existing administrator accounts created without MFA have no self-service enrollment/reset screen in this pilot; do not switch the requirement on for them without an account-provisioning plan. Configuration changes do not reset stored accounts or force existing authenticated sessions to sign in again.

## Setup and live-data gates

Set configuration through the deployment secret/environment facility, never committed JSON. Required initial configuration:

```text
Evidence__Enabled=true
Evidence__EncryptionKey=<base64-encoded cryptographically random 32-byte key>
Evidence__StoragePath=<private persistent directory outside wwwroot>
Evidence__BootstrapEmail=<initial administrator email>
Evidence__BootstrapPassword=<unique 14–256 character password>
Evidence__RequireAdminMfa=false
Irei__AdminKey=<separate strong legacy staff API key>
```

The bootstrap administrator is created only for an empty store. Keep the encryption key in the approved secret store and back it up separately: losing it makes both source copies and retained snapshots unrecoverable. Remove bootstrap credentials from runtime configuration after provisioning. Invitation tokens are displayed once for manual delivery through the agreed secure channel; the application sends no invitations or email.

For synthetic demonstrations only, keep `Evidence__Prototype=true`. Plain HTTP is accepted only when **all** of Development environment, prototype mode, `Evidence__AllowLocalHttp=true` and a loopback request are present. Use HTTPS for other testing and deployment. Production cookies require TLS; configure the application’s HTTPS termination correctly. Do not bypass this by enabling local HTTP on a public service.

Before receiving actual client documents:

1. Approve the actual hosting location, processors, backups, access controls, incident process, privacy notices and agreements. The app does not certify these.
2. Run one application instance with a private persistent storage volume. The encrypted JSON state has process-level serialization, not distributed locking; do not run multiple replicas against it. Back up originals, metadata, frozen snapshots and keys; test exact-version recovery.
3. Install and maintain **existing approved local ClamAV** on loopback, default port 3310. Set `Evidence__ScannerPort` if needed. No scanner is bundled or purchased. Production streams capped source bytes to its INSTREAM protocol; unavailable/failed scans quarantine uploads. Complete actual malware-effectiveness testing with the deployed scanner.
4. Set `Evidence__Prototype=false` and `Evidence__InfrastructureSpendBlocked=true` only after confirming hosting capacity and spend controls. Record per-organization written authority, hosting/security approval, retention periods, processors/backups, exact-version client preservation and retrieval arrangements in the administrator policy screen. Missing approvals block live intake.
5. Create each organization and its authorized department memberships, invite named users, and review the scoped views before onboarding. Record whether the portfolio inventory is a sample or full population.

Default limits: 10 MiB/file, 25 files and 40 MiB/batch, 250 MiB/organization, 1 GiB total. Metadata, snapshots, physical sources and pending reservations count toward capacity. All uploads are capped in memory; archives decrypt one bounded file at a time. Reserve capacity for backups and operational logs independently. The implementation cannot prevent a hosting provider from charging; provider billing controls remain an operator dependency.

The encrypted store contains normalized related records but is not a relational database. Its 64 MiB metadata ceiling and whole-state transaction cost deliberately bound this pilot. A larger deployment needs a reviewed database/object-store migration and tested identity operations. No schema changes to existing V4 data are required.

## Review, retention and recovery

Upload means receipt, not verified content. Sensitive-data marker checks are conservative screening aids, not complete personal-information detection. Analysts must inspect permitted content, resolve material conflicts and minimize resident-level information. Quarantined bytes cannot be downloaded; administrators can record remediation and request rescreening. Never place actual originals or unredacted evidence into this public repository.

Use the Data tab for spreadsheet import or source-indexed entry; unsupported layouts remain review proposals. Verify only with correct units, basis and period. Use Create assessment to feed selected verified records directly into the existing V4 server engine. CSV exports are optional evidence extracts, not assessment upload files. Analysts enter the internal model/version and assumptions when freezing a client-signoff snapshot; proprietary workbooks and dashboards remain staff-only.

Freeze and check the assessment before client sign-off. Sign-off confirms facts and explicitly grants or withholds publication permission; an administrator separately publishes the client-facing report text. Internal snapshot access remains staff-only. Later changes create new facts, source versions and snapshots.

Retention dates are recorded, not a background deletion scheduler. Review agreements and exact-version retrieval before each manual source deletion. A hash verifies identity; it does not preserve a file. Audit records and retained analytical snapshots remain encrypted after deletion. Follow the separately agreed derived-record retention process; no general snapshot-delete endpoint is provided.

## Validation and rollback

`tests/smoke_evidence.py` runs synthetic organization/department isolation, invitations/password sign-in/optional MFA, CSRF, mixed uploads and retries, screening, property lineage, unavailable values, contradictions, spreadsheet proposals, controlled exports, source archives, snapshots, sign-off/publication, deletion, quotas, encryption and revocation checks. Optional `EVIDENCE_BROWSER_TEST=true` runs `tests/browser_evidence.cjs` with Playwright for desktop/mobile upload and role views. `tests/smoke_evidence_production.py` checks HTTPS, secure cookies, readiness and fail-closed scanning using a local protocol stub; it does not prove malware detection or hosting compliance. CI runs the main suite with MFA off and explicitly on, and the production gates alongside existing V4 and intake regressions.

Build with .NET 8. Enable the portal only after provisioning secrets. When enabled, legacy `/api/*` routes also require their separate staff `X-Admin-Key`; client sessions do not grant access to them. Existing deployments that used the former checked-in example key must configure their own key.

Rollback by disabling `Evidence__Enabled` and redeploying the previous version if required. Preserve the private evidence directory and encryption key. Do not enable the old public API configuration on an instance that still hosts confidential legacy data. The portal’s files and store are additive; no migration alters the existing calculation engine or workbooks.

Measure onboarding separately: upload/extraction elapsed time, analyst review minutes, issue-resolution time, verified facts per hour, and update time against the existing spreadsheet-first baseline. Upload audit timestamps and receipts support technical elapsed times; analyst time must be recorded during the pilot. No efficiency improvement is claimed before this comparison.

## Guided extraction and draft preview update

Documents now has a visible **Upload documents** button and **Extract all ready documents** for staff. Accepted XLSX/CSV and text PDFs can be extracted; staff uploads default to extracting supported content after receipt. Extraction failures do not roll back successful uploads. Existing uploads can use **Extract data / refresh preview**. Re-extraction retains reviewed facts and backfills proposed sheet/property labels on older unreviewed spreadsheet proposals.

The **Data preview and review** tab lets staff inspect unreviewed candidates immediately. Filter by source, source period, proposed property/sheet and review status; metric cards never add together files or competing scenarios. Rows are paginated at 200. Property names and units can be proposed from inventory tables; mortgage registers with multiline headers and annual payment matrices can propose rates, expiries, balances and payment components. These are proposed source matches, not verified legal/property identities. Worksheet totals and property components remain separate. Invalid years and stale/error formula caches still require review.

Use **Confirm selected context** for up to 200 candidates from one source, proposed property/sheet and period. Explicitly supply the true reporting period, basis, currency, monetary scale and existing property ID (or deliberately select organization-wide scope). This appends corrections and preserves originals. Then **Review selected facts** can apply a review decision with an explicit confirmation and rationale; verification still enforces individual validation, source screening, conflict and duplicate checks. Nothing is auto-verified. Material source issues must be resolved separately. A rejected batch leaves all selected records unchanged.

The draft CSV includes unreviewed evidence with a clear non-validation warning. The verified CSV now reports a useful error when no facts are eligible instead of returning headings only. Neither CSV is a V4.2 assessment workbook. The preview is a source-data view. The subsequent reviewed-evidence bridge now creates V4 working assessments directly; the legacy separate-key page still accepts prepared V4.2 workbooks. Basic preview reconciliation compares principal plus interest against debt service, and organization plus building expenses against a reported operating total, only when one comparable set with confirmed context is present. Arithmetic agreement is not evidence verification.

PDF parsing uses the managed Apache-2.0 PdfPig dependency (pinned 0.1.16) on the application server; no external AI service, executable, Python installation or paid API is required on SmarterASP.NET. Deploy the complete publish output including the new dependency assemblies. Publish application code while preserving the private evidence directory, encryption key and host configuration. File screening and permissions are unchanged.

PDF support is deliberately bounded: 150 pages, 500,000 extracted characters and 10,000 candidate numbers per import, plus the existing file limits. It extracts embedded text, recognizes supported financial labels, proposes column years when readable and retains page/line/x-location references. **Read extracted text** is staff-only and checks source availability again. PDF tables, columns, source scaling and source accuracy must be checked. Scanned/image-only pages are explicitly marked **OCR required**; no OCR engine is bundled, and mixed PDFs warn about low-text pages. Unsupported labels/layouts may yield readable text but no mapped facts. Encrypted, quarantined, deleted and unreadable sources cannot bypass screening or extraction gates. This is deterministic extraction, not generative AI or general-purpose PDF understanding.

## Reviewed evidence → V4 assessments

All-page context confirmation and review use server-side filters and preview tokens, not client-submitted thousands of IDs. Verification records a rationale and checks current source availability, required context, material issues and competing records. Ineligible records are not silently accepted. Context corrections keep originals and preserve each valid fiscal period unless explicitly overridden. Single-year labels require an explicit starting/ending-year convention. Already confirmed context is not scaled again.

Select source versions and a single reporting basis/currency to create a working assessment. The existing V4 engine computes results; duplicate totals and inconsistent complete component pairs are withheld. Missing portfolio controls are not inferred from property samples, and missing coverage targets or borrowing assumptions stay unavailable. The blank template supplies method/display definitions only. Generated assessments and baselines remain in the encrypted portal store, scoped to all source departments; no data is copied to the unscoped legacy V4 directory. Withdrawn sources immediately block dashboard access. Assessments are dated snapshots: correct original evidence and create another assessment for updates. Portal dashboards support year selection, evidence drilldown and option testing; legacy record editing, formal approval and release are not granted through this bridge. See the quick start for the exact workflow.

Publishing is additive and needs no new configuration. Preserve the evidence store and encryption key; deploy the complete application including Data/Templates. Existing portal credentials and optional MFA configuration are unchanged.
