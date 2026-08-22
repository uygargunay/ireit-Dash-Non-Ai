"use strict";

const state = {
  submission: null,
  page: location.hash.replace("#", "") || "overview",
  selectedProperty: null
};

const pages = {
  overview: ["Portfolio Overview", "Executive command centre — condition, changes, priorities and required decisions"],
  governance: ["Organization & Governance", "Governance capacity, management structure, controls, obligations and accountability"],
  properties: ["Properties / Portfolio", "Persistent property register, classifications, unit mix, risk and drill-through"],
  financial: ["Financial Resilience", "Controlled metrics, trends, change drivers and forward debt/capital events"],
  mission: ["Mission & Public Value", "Affordability, mission commitments, restrictions and evidence-backed outcomes"],
  actions: ["Decisions, Risks & Actions", "Issues, causes, impacts, responses, owners and decision history"],
  evidence: ["Evidence & Data Quality", "Source tracking, mapping, verification, exceptions and lineage"],
  reports: ["Reports & Authorized Use", "Versioned reports, approval gates, recipients, scope and controlled sharing"]
};

const content = document.getElementById("content");
const nav = document.getElementById("page-nav");
const uploadDialog = document.getElementById("upload-dialog");
const formDialog = document.getElementById("form-dialog");
const uploadForm = document.getElementById("upload-form");
const fileInput = document.getElementById("file-input");
const approveButton = document.getElementById("approve-version");
const versionSelect = document.getElementById("version-select");

document.getElementById("open-upload").addEventListener("click", openUpload);
document.getElementById("close-upload").addEventListener("click", () => uploadDialog.close());
document.getElementById("cancel-upload").addEventListener("click", () => uploadDialog.close());
approveButton.addEventListener("click", approveVersion);
versionSelect.addEventListener("change", async () => {
  if (!versionSelect.value || versionSelect.value === state.submission?.id) return;
  try {
    const submission = await api(`/api/submissions/${encodeURIComponent(versionSelect.value)}`);
    setSubmission(submission);
  } catch (error) {
    toast(error.message, true);
  }
});
nav.addEventListener("click", event => {
  const button = event.target.closest("button[data-page]");
  if (!button) return;
  state.page = button.dataset.page;
  location.hash = state.page;
  render();
});
window.addEventListener("hashchange", () => {
  const next = location.hash.replace("#", "");
  if (pages[next]) {
    state.page = next;
    render();
  }
});

content.addEventListener("click", handleContentAction);
configureUpload();
restoreLastSubmission();

async function restoreLastSubmission() {
  const id = localStorage.getItem("irei:lastSubmission");
  if (!id) {
    render();
    return;
  }
  try {
    const submission = await api(`/api/submissions/${encodeURIComponent(id)}`);
    setSubmission(submission, false);
  } catch {
    localStorage.removeItem("irei:lastSubmission");
    render();
  }
}

function configureUpload() {
  const dropZone = document.getElementById("drop-zone");
  fileInput.addEventListener("change", () => {
    document.getElementById("selected-file").textContent = fileInput.files[0]?.name || "Maximum 50 MB";
  });
  ["dragenter", "dragover"].forEach(type => dropZone.addEventListener(type, event => {
    event.preventDefault();
    dropZone.classList.add("dragover");
  }));
  ["dragleave", "drop"].forEach(type => dropZone.addEventListener(type, event => {
    event.preventDefault();
    dropZone.classList.remove("dragover");
  }));
  dropZone.addEventListener("drop", event => {
    if (!event.dataTransfer.files.length) return;
    const transfer = new DataTransfer();
    transfer.items.add(event.dataTransfer.files[0]);
    fileInput.files = transfer.files;
    document.getElementById("selected-file").textContent = fileInput.files[0].name;
  });
  uploadForm.addEventListener("submit", submitUpload);
}

function openUpload() {
  document.getElementById("upload-error").classList.add("hidden");
  if (state.submission) {
    document.getElementById("organization-name").value = state.submission.organizationName;
  }
  uploadDialog.showModal();
}

async function submitUpload(event) {
  event.preventDefault();
  const error = document.getElementById("upload-error");
  const submit = document.getElementById("submit-upload");
  error.classList.add("hidden");
  if (!fileInput.files.length || !fileInput.files[0].name.toLowerCase().endsWith(".xlsx")) {
    error.textContent = "Select a valid .xlsx workbook.";
    error.classList.remove("hidden");
    return;
  }
  const body = new FormData(uploadForm);
  submit.disabled = true;
  submit.textContent = "Processing…";
  try {
    const submission = await api("/api/submissions", { method: "POST", body });
    uploadDialog.close();
    uploadForm.reset();
    document.getElementById("selected-file").textContent = "Maximum 50 MB";
    state.page = "overview";
    location.hash = "overview";
    setSubmission(submission);
    toast(`Working version ${submission.versionId} created.`);
  } catch (requestError) {
    error.textContent = requestError.message;
    error.classList.remove("hidden");
  } finally {
    submit.disabled = false;
    submit.textContent = "Process workbook";
  }
}

function setSubmission(submission, remember = true) {
  state.submission = submission;
  state.selectedProperty = null;
  if (remember) localStorage.setItem("irei:lastSubmission", submission.id);
  render();
}

function render() {
  const page = pages[state.page] ? state.page : "overview";
  const [title, subtitle] = pages[page];
  document.getElementById("page-title").textContent = title;
  document.getElementById("page-subtitle").textContent = subtitle;
  nav.querySelectorAll("button").forEach(button => button.classList.toggle("active", button.dataset.page === page));

  if (!state.submission) {
    document.getElementById("organization-chip").textContent = "No assessment loaded";
    document.getElementById("last-verified").textContent = "Not verified";
    document.getElementById("reporting-period").textContent = "Not assessed";
    versionSelect.innerHTML = "<option>—</option>";
    versionSelect.disabled = true;
    approveButton.classList.add("hidden");
    renderWelcome();
    return;
  }

  const s = state.submission;
  document.getElementById("organization-chip").textContent = s.organizationName;
  document.getElementById("last-verified").textContent = s.lastVerifiedUtc ? formatDate(s.lastVerifiedUtc) : "Not verified";
  document.getElementById("reporting-period").textContent = text(s.reportingPeriod);
  versionSelect.innerHTML = (s.versions || []).map(version => `<option value="${escapeHtml(version.submissionId)}" ${version.submissionId === s.id ? "selected" : ""}>${escapeHtml(version.versionId)} · ${escapeHtml(version.versionStatus)}</option>`).join("") || `<option value="${escapeHtml(s.id)}">${escapeHtml(s.versionId)} · ${escapeHtml(s.versionStatus)}</option>`;
  versionSelect.disabled = (s.versions || []).length < 2;
  approveButton.classList.toggle("hidden", s.versionStatus === "Approved");

  const renderers = {
    overview: renderOverview,
    governance: renderGovernance,
    properties: renderProperties,
    financial: renderFinancial,
    mission: renderMission,
    actions: renderActions,
    evidence: renderEvidence,
    reports: renderReports
  };
  renderers[page](s);
}

function renderWelcome() {
  content.innerHTML = `
    <div class="welcome">
      <section class="panel welcome-card">
        <div class="welcome-mark">V2</div>
        <h2>Build a traceable Stage 1 assessment</h2>
        <p>Upload an organization workbook to create the eight-page stewardship view. The import is deterministic, source-linked and review-gated.</p>
        <div class="welcome-points">
          <div><b>Versioned</b><span>Approved baselines remain immutable.</span></div>
          <div><b>Traceable</b><span>Metrics link back to source evidence.</span></div>
          <div><b>Controlled</b><span>Reports require explicit approval.</span></div>
        </div>
        <button class="button primary" type="button" data-action="upload">Upload workbook</button>
      </section>
    </div>`;
}

function renderOverview(s) {
  const d = s.dashboard;
  const changes = s.changes || [];
  const openActions = (s.actions || []).filter(item => !isClosed(item.status));
  const riskCounts = propertyRiskCounts(d.properties || []);
  const overdue = openActions.filter(item => item.dueDate && new Date(item.dueDate) < new Date()).length;
  const boardDecisions = openActions.filter(item => normalized(item.decisionBody).includes("board")).length;
  const evidenceReview = (s.evidence || []).filter(item => normalized(item.verificationStatus) !== "verified").length;
  const dueObligations = (s.obligations || []).filter(item => dueWithin(item.dueDate, 60) && !isClosed(item.status)).length;

  content.innerHTML = `
    <div class="kpi-grid six">
      ${kpi("Overall status", text(d.overallStatus), s.versionStatus === "Approved" ? "Approved assessment" : "Working assessment", statusTone(d.overallStatus))}
      ${kpi("Properties", number(d.totalProperties), `${number(d.includedProperties)} included`, "info")}
      ${kpi("Total units", number(d.modeledUnits), `${number(d.unclassifiedUnits)} unclassified`, d.unclassifiedUnits ? "warning" : "positive")}
      ${kpi("Portfolio NOI", money(d.noi), periodLabel(s), d.noi < 0 ? "negative" : "positive")}
      ${kpi("Portfolio DSCR", multiple(d.dscr), "NOI / debt service", d.dscr == null ? "" : d.dscr < 1.2 ? "negative" : "positive")}
      ${kpi("Total debt", money(d.totalDebt), "Included instruments", "")}
    </div>

    <div class="grid-overview">
      <section class="panel panel-pad">
        <h2>What's Changed Since Last Review</h2>
        <div class="change-list">${changes.length ? changes.slice(0, 6).map(changeRow).join("") : empty("This is the initial baseline; no prior approved version is available.")}</div>
      </section>
      <section class="panel panel-pad">
        <div class="panel-head"><h2>Top Priorities</h2><button class="link-button" data-page-link="actions">View actions</button></div>
        <div class="priority-list">${openActions.length ? openActions.slice(0, 5).map(priorityRow).join("") : empty("No open actions.")}</div>
      </section>
      <section class="panel panel-pad">
        <h2>Alerts Requiring Attention</h2>
        <div class="status-list">
          ${statusItem("Board decisions required", boardDecisions, boardDecisions ? "red" : "green")}
          ${statusItem("Overdue actions", overdue, overdue ? "red" : "green")}
          ${statusItem("Evidence requiring review", evidenceReview, evidenceReview ? "amber" : "green")}
          ${statusItem("Upcoming obligations (60 days)", dueObligations, dueObligations ? "blue" : "green")}
          ${statusItem("High risks", riskCounts.high, riskCounts.high ? "red" : "green")}
        </div>
      </section>
    </div>

    <div class="grid-overview">
      <section class="panel panel-pad span-2">
        <h2>Assessment Areas</h2>
        <div class="metric-list">${(d.assessmentAreas || []).map(areaRow).join("") || empty("Assessment areas are not available.")}</div>
      </section>
      <section class="panel panel-pad">
        <h2>Property Risk</h2>
        ${donut([
          ["Stable", riskCounts.stable, "green"],
          ["Watch", riskCounts.watch, "amber"],
          ["High", riskCounts.high, "red"],
          ["Not assessed", riskCounts.notAssessed, "violet"]
        ], number(d.totalProperties), "Properties")}
      </section>
      <section class="panel panel-pad span-all">
        <div class="panel-head"><h2>Forward Calendar</h2><span class="panel-note">Events derived from debt, agreements and obligations</span></div>
        ${eventTable(s.futureEvents || [], s.obligations || [])}
      </section>
    </div>`;
}

function renderGovernance(s) {
  const d = s.dashboard;
  const obligations = s.obligations || [];
  content.innerHTML = `
    <div class="grid-3">
      <section class="panel panel-pad">
        <h2>Organization Profile</h2>
        <div class="status-list">
          ${labelValue("Organization", s.organizationName)}
          ${labelValue("Type", d.organizationType)}
          ${labelValue("Incorporated", d.incorporatedYear)}
          ${labelValue("Head office", d.headOffice)}
          ${labelValue("Portfolio", `${number(d.totalProperties)} properties / ${number(d.modeledUnits)} units`)}
          ${labelValue("Mission", d.missionFocus || "Not assessed")}
        </div>
      </section>
      <section class="panel panel-pad">
        <h2>Governance & Board</h2>
        <div class="status-list">${(d.governanceIndicators || []).map(item => statusItem(item.indicator, item.status, badgeTone(item.status))).join("") || empty("Governance evidence was not supplied.")}</div>
      </section>
      <section class="panel panel-pad">
        <div class="panel-head"><h2>Upcoming Obligations</h2><button class="button small secondary" data-action="new-obligation">Add</button></div>
        ${compactObligationTable(obligations.slice(0, 5))}
      </section>

      <section class="panel panel-pad span-2">
        <h2>Organizational Capacity</h2>
        <div class="metric-list">${(d.capacityIndicators || []).map(capacityRow).join("")}</div>
      </section>
      <section class="panel panel-pad">
        <h2>Decision Rights & Controls</h2>
        ${decisionRightsTable(d.decisionRights || [])}
      </section>

      <section class="panel panel-pad span-all">
        <div class="panel-head"><h2>Governance & Obligations Register</h2><button class="button small primary" data-action="new-obligation">New obligation</button></div>
        ${obligationsTable(obligations)}
      </section>
    </div>`;
}

function renderProperties(s) {
  const d = s.dashboard;
  const properties = d.properties || [];
  const atRisk = properties.filter(item => ["watch", "high"].includes(normalized(item.riskLevel))).length;
  const mix = [
    ["Affordable", d.affordableUnits || 0, "blue"],
    ["Market", d.marketUnits || 0, "green"],
    ["Supportive", d.supportiveUnits || 0, "violet"],
    ["Unclassified", d.unclassifiedUnits || 0, "amber"]
  ];
  const risks = propertyRiskCounts(properties);
  const exceptions = properties.filter(item => item.unitConflict || item.dataGaps > 0);

  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Total properties", number(d.totalProperties), "Portfolio record", "info")}
      ${kpi("Included", number(d.includedProperties), "Current analysis", "positive")}
      ${kpi("Total units", number(d.modeledUnits), "Included modeled units", "")}
      ${kpi("Affordable units", number(d.affordableUnits), percentOf(d.affordableUnits, d.modeledUnits), d.affordableUnits ? "positive" : "warning")}
      ${kpi("At risk / watch", number(atRisk), percentOf(atRisk, d.totalProperties), atRisk ? "negative" : "positive")}
    </div>
    <section class="panel panel-pad">
      <div class="panel-head"><h2>Property Register — click any row to drill through</h2><span class="panel-note">${properties.length} records</span></div>
      ${propertiesTable(properties)}
      <div id="property-detail">${state.selectedProperty ? propertyDetail(properties.find(item => item.propertyId === state.selectedProperty)) : ""}</div>
    </section>
    <div class="grid-3">
      <section class="panel panel-pad"><h2>Portfolio Mix</h2>${donut(mix, number(d.modeledUnits), "Units")}</section>
      <section class="panel panel-pad"><h2>Risk Distribution</h2>${donut([["Stable", risks.stable, "green"], ["Watch", risks.watch, "amber"], ["High", risks.high, "red"], ["Not assessed", risks.notAssessed, "violet"]], number(properties.length), "Properties")}</section>
      <section class="panel panel-pad"><h2>Data / Unit Exceptions</h2>${exceptionTable(exceptions)}</section>
    </div>`;
}

function renderFinancial(s) {
  const d = s.dashboard;
  const financialChanges = (s.changes || []).filter(item => normalized(item.category).includes("financial"));
  const debtEvents = (s.futureEvents || []).filter(item => normalized(item.eventType).includes("debt"));
  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Portfolio DSCR", multiple(d.dscr), periodLabel(s), d.dscr == null ? "" : d.dscr < 1.2 ? "negative" : "positive")}
      ${kpi("Unrestricted liquidity", nullableMoney(d.unrestrictedLiquidity), "Source-classified cash only", d.unrestrictedLiquidity == null ? "warning" : "positive")}
      ${kpi("Interest coverage", multiple(d.interestCoverage), "NOI / interest", d.interestCoverage == null ? "" : d.interestCoverage < 1.5 ? "negative" : "positive")}
      ${kpi("Unfunded capital needs", nullableMoney(d.unfundedCapitalNeeds), "Verified capital plan", d.unfundedCapitalNeeds == null ? "warning" : d.unfundedCapitalNeeds > 0 ? "negative" : "positive")}
      ${kpi("Total debt", money(d.totalDebt), "Included instruments", "")}
    </div>
    <div class="grid-financial">
      <section class="panel panel-pad"><h2>DSCR Trend</h2>${trendChart(d.financialTrend || [])}</section>
      <section class="panel panel-pad">
        <h2>Change Drivers vs Last Review</h2>
        <div class="change-list">${financialChanges.length ? financialChanges.slice(0, 7).map(changeRow).join("") : empty("No prior-version financial changes are available.")}</div>
      </section>
    </div>
    <div class="grid-financial">
      <section class="panel panel-pad"><h2>Debt Maturity Profile</h2>${debtBarChart(d.debtHorizon || [])}</section>
      <section class="panel panel-pad"><h2>Forward Events</h2>${futureEventsTable(debtEvents.length ? debtEvents : s.futureEvents || [])}</section>
    </div>`;
}

function renderMission(s) {
  const d = s.dashboard;
  const mix = [
    ["Affordable", d.affordableUnits || 0, "blue"],
    ["Market", d.marketUnits || 0, "green"],
    ["Supportive", d.supportiveUnits || 0, "violet"],
    ["Unclassified", d.unclassifiedUnits || 0, "amber"]
  ];
  const reviewed = (d.agreements || []).filter(item => !normalized(item.verificationStatus).includes("review")).length;
  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Affordable units", number(d.affordableUnits), percentOf(d.affordableUnits, d.modeledUnits), d.affordableUnits ? "positive" : "warning")}
      ${kpi("Households served", number(d.householdsServed), "Modeled unit proxy", "info")}
      ${kpi("Avg. depth of affordability", d.averageAffordabilityDepth == null ? "Not assessed" : percent(d.averageAffordabilityDepth), "Below market rent", d.averageAffordabilityDepth == null ? "warning" : "info")}
      ${kpi("Supportive units", number(d.supportiveUnits), percentOf(d.supportiveUnits, d.modeledUnits), "")}
      ${kpi("Agreements reviewed", `${reviewed} / ${(d.agreements || []).length}`, `${Math.max(0, (d.agreements || []).length - reviewed)} require review`, reviewed === (d.agreements || []).length && reviewed > 0 ? "positive" : "warning")}
    </div>
    <div class="grid-mission">
      <section class="panel panel-pad">
        <h2>Mission Alignment</h2>
        <div class="status-list">${missionAlignment(d)}</div>
      </section>
      <section class="panel panel-pad"><h2>Affordability by Classification</h2>${donut(mix, number(d.modeledUnits), "Units")}</section>
      <section class="panel panel-pad"><h2>Agreement Expiries</h2>${agreementsTable(d.agreements || [])}</section>
    </div>
    <section class="panel panel-pad">
      <h2>Public Value & Outcome Evidence</h2>
      ${outcomesTable(d.outcomes || [])}
      <p class="disclosure">Monetized community-benefit values are not foregrounded until the methodology and source trail are production-ready.</p>
    </section>`;
}

function renderActions(s) {
  const actions = s.actions || [];
  const open = actions.filter(item => !isClosed(item.status));
  const completed = actions.filter(item => isClosed(item.status));
  const high = open.filter(item => ["high", "critical"].includes(normalized(item.riskLevel))).length;
  const decisions = open.filter(item => normalized(item.decisionBody).includes("board")).length;
  const completed90 = completed.filter(item => item.closedUtc && new Date(item.closedUtc) >= daysAgo(90)).length;
  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Open risks", number((s.dashboard.riskItems || []).length), "Source flags", (s.dashboard.riskItems || []).length ? "negative" : "positive")}
      ${kpi("High / critical", number(high), "Open workflow items", high ? "negative" : "positive")}
      ${kpi("Open actions", number(open.length), "Persistent register", open.length ? "warning" : "positive")}
      ${kpi("Board decisions", number(decisions), "Required", decisions ? "negative" : "positive")}
      ${kpi("Completed / 90 days", number(completed90), "Closed", "positive")}
    </div>
    <section class="panel panel-pad">
      <div class="panel-head"><h2>Active Decisions, Risks & Actions</h2><button class="button small primary" data-action="new-action">New action</button></div>
      ${actionsTable(open, true)}
    </section>
    <div class="grid-2">
      <section class="panel panel-pad"><h2>Recently Completed / Closed</h2>${actionsTable(completed.slice(0, 5), false)}</section>
      <section class="panel panel-pad">
        <h2>Workflow</h2>
        <div class="workflow">
          ${["Identify issue", "Link evidence", "Assign owner", "Decision / action", "Track outcome"].map((label, index) => `<div class="workflow-step"><b>${index + 1}</b><span>${label}</span></div>`).join("")}
        </div>
      </section>
    </div>`;
}

function renderEvidence(s) {
  const evidence = s.evidence || [];
  const verified = evidence.filter(item => normalized(item.verificationStatus) === "verified").length;
  const needsReview = evidence.length - verified;
  const missing = (s.warnings || []).filter(item => normalized(item).includes("not found") || normalized(item).includes("missing")).length;
  const conflicting = (s.dashboard.properties || []).filter(item => item.unitConflict).length;
  const categories = evidenceCategories(evidence);
  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Evidence items", number(evidence.length), `${number((s.sourceDocuments || []).length)} source documents`, "info")}
      ${kpi("Verified", number(verified), percentOf(verified, evidence.length), "positive")}
      ${kpi("Needs review", number(needsReview), percentOf(needsReview, evidence.length), needsReview ? "warning" : "positive")}
      ${kpi("Missing", number(missing), "Intake warnings", missing ? "negative" : "positive")}
      ${kpi("Conflicting", number(conflicting), "Unit conflicts", conflicting ? "negative" : "positive")}
    </div>
    <div class="grid-2">
      <section class="panel panel-pad"><h2>Evidence Status by Category</h2><div class="metric-list">${categories.map(evidenceCategoryRow).join("") || empty("No evidence categories are available.")}</div></section>
      <section class="panel panel-pad">
        <div class="panel-head"><h2>Recent Uploads / Updates</h2><button class="button small primary" data-action="upload-evidence">Upload evidence</button></div>
        ${sourceTable(s.sourceDocuments || [])}
      </section>
    </div>
    <section class="panel panel-pad">
      <h2>Source Traceability / Review Queue</h2>
      ${evidenceTable(evidence)}
      <p class="disclosure">Each metric can be traced from dashboard → field → entity → source → location → verification status.</p>
    </section>`;
}

function renderReports(s) {
  const reports = s.reports || [];
  const count = status => reports.filter(item => normalized(item.status) === normalized(status)).length;
  const superseded = count("Superseded");
  content.innerHTML = `
    <div class="kpi-grid">
      ${kpi("Reports created", number(reports.length), "All versions", "info")}
      ${kpi("Draft", number(count("Draft")), "Awaiting review", "warning")}
      ${kpi("Approved", number(count("Approved")), "Current", "positive")}
      ${kpi("Shared externally", number(count("Shared")), "Authorized", "info")}
      ${kpi("Superseded", number(superseded), "Prior reports retained", "")}
    </div>
    <section class="panel panel-pad">
      <div class="panel-head"><h2>Report Snapshot Registry</h2><button class="button small primary" data-action="new-report">New report</button></div>
      ${reportsTable(reports, s.versionStatus)}
    </section>
    <div class="grid-2">
      <section class="panel panel-pad">
        <h2>Authorized Use Rules</h2>
        ${simpleTable(["Recipient type", "Scope", "Gate"], [
          ["Internal management", "Full verified portfolio record", "Management"],
          ["Board", "Decisions, risks, capital and mission", "Board-approved"],
          ["Funder / government", "Purpose-specific evidence", "Authorized"],
          ["Lender / capital partner", "Purpose-specific finance package", "Authorized"],
          ["Public / partner summary", "Selected approved data only", "Approved"]
        ])}
      </section>
      <section class="panel panel-pad">
        <h2>Report Version Control</h2>
        ${simpleTable(["Requirement", "V2 behavior", "Priority"], [
          ["Assessment version", "Every report references Version_ID", "P0"],
          ["Approval status", "Draft → Approved → Shared", "P0"],
          ["Recipient / purpose", "Record who received what and why", "P0"],
          ["Supersedes", "New report links to prior report", "P1"],
          ["Artifact reference", "Frozen workbook artifact retained", "P1"]
        ])}
      </section>
    </div>`;
}

async function handleContentAction(event) {
  const pageLink = event.target.closest("[data-page-link]");
  if (pageLink) {
    state.page = pageLink.dataset.pageLink;
    location.hash = state.page;
    render();
    return;
  }
  const property = event.target.closest("tr[data-property-id]");
  if (property) {
    state.selectedProperty = property.dataset.propertyId;
    render();
    document.getElementById("property-detail")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    return;
  }
  const target = event.target.closest("[data-action]");
  if (!target) return;
  const action = target.dataset.action;
  if (action === "upload") return openUpload();
  if (action === "close-property") {
    state.selectedProperty = null;
    return render();
  }
  if (action === "new-action") return openActionForm();
  if (action === "new-obligation") return openObligationForm();
  if (action === "upload-evidence") return openEvidenceForm();
  if (action === "new-report") return openReportForm();
  if (action === "close-action") return closeAction(target.dataset.id);
  if (action === "approve-report") return approveReport(target.dataset.id);
  if (action === "share-report") return shareReport(target.dataset.id);
  if (action === "download-report") {
    const key = prompt("Authorized download key");
    if (key) location.href = `/api/submissions/${encodeURIComponent(state.submission.id)}/reports/${encodeURIComponent(target.dataset.id)}/download?key=${encodeURIComponent(key)}`;
  }
}

async function approveVersion() {
  const approvedBy = prompt("Reviewer name", "Authorized reviewer");
  if (!approvedBy) return;
  try {
    const submission = await api(`/api/submissions/${encodeURIComponent(state.submission.id)}/approve`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ approvedBy })
    });
    setSubmission(submission);
    toast(`Version ${submission.versionId} approved.`);
  } catch (error) {
    toast(error.message, true);
  }
}

function openActionForm() {
  openForm("New action / decision", [
    field("issue", "Issue / action", "textarea", true),
    field("owner", "Owner"),
    field("dueDate", "Due date", "date"),
    field("decisionBody", "Decision body", "select", false, ["Management", "Board", "CEO", "CFO"]),
    field("riskLevel", "Risk level", "select", false, ["Low", "Medium", "High", "Critical"]),
    field("response", "Response", "textarea")
  ], async values => {
    await jsonMutation(`/api/submissions/${state.submission.id}/actions`, "POST", values);
    toast("Action added.");
  });
}

function openObligationForm() {
  openForm("New obligation", [
    field("obligation", "Obligation", "textarea", true),
    field("source", "Source / agreement"),
    field("owner", "Owner"),
    field("dueDate", "Due date", "date"),
    field("recurrence", "Frequency"),
    field("notes", "Notes", "textarea")
  ], async values => {
    await jsonMutation(`/api/submissions/${state.submission.id}/obligations`, "POST", values);
    toast("Obligation added.");
  });
}

function openReportForm() {
  openForm("Create report snapshot", [
    field("reportName", "Report name", "text", true),
    field("purpose", "Purpose", "textarea", true),
    field("recipient", "Intended recipient", "text", true),
    field("scope", "Approved scope", "textarea", true)
  ], async values => {
    await jsonMutation(`/api/submissions/${state.submission.id}/reports`, "POST", values);
    toast("Frozen report snapshot created.");
  });
}

function openEvidenceForm() {
  openForm("Upload supporting evidence", [
    field("sourceType", "Evidence type", "text", true),
    field("file", "File", "file", true)
  ], async (_, form) => {
    const body = new FormData(form);
    await api(`/api/submissions/${state.submission.id}/evidence`, { method: "POST", body });
    await reloadSubmission();
    toast("Evidence uploaded for review.");
  });
}

async function closeAction(id) {
  try {
    await jsonMutation(`/api/submissions/${state.submission.id}/actions/${id}`, "PATCH", { status: "Completed" });
    toast("Action marked complete.");
  } catch (error) { toast(error.message, true); }
}

async function approveReport(id) {
  const approvedBy = prompt("Approver name", "Authorized reviewer");
  if (!approvedBy) return;
  try {
    await jsonMutation(`/api/submissions/${state.submission.id}/reports/${id}/approve`, "POST", { approvedBy });
    toast("Report approved.");
  } catch (error) { toast(error.message, true); }
}

async function shareReport(id) {
  const sharedBy = prompt("Authorized user", "Authorized user");
  if (!sharedBy) return;
  try {
    await jsonMutation(`/api/submissions/${state.submission.id}/reports/${id}/share`, "POST", { sharedBy });
    toast("Report marked as shared.");
  } catch (error) { toast(error.message, true); }
}

function openForm(title, fields, onSubmit) {
  const form = document.getElementById("dynamic-form");
  form.innerHTML = `
    <div class="modal-head"><div><span class="eyebrow">WORKFLOW</span><h2>${escapeHtml(title)}</h2></div><button class="icon-button" type="button" data-close-form>×</button></div>
    <div class="form-grid">${fields.map(formField).join("")}</div>
    <div data-form-error class="inline-alert hidden"></div>
    <div class="modal-actions"><button class="button secondary" type="button" data-close-form>Cancel</button><button class="button primary" type="submit">Save</button></div>`;
  form.querySelectorAll("[data-close-form]").forEach(button => button.addEventListener("click", () => formDialog.close()));
  form.onsubmit = async event => {
    event.preventDefault();
    const submit = form.querySelector("button[type=submit]");
    const errorBox = form.querySelector("[data-form-error]");
    submit.disabled = true;
    errorBox.classList.add("hidden");
    const formData = new FormData(form);
    const values = Object.fromEntries([...formData.entries()].filter(([, value]) => !(value instanceof File)));
    Object.keys(values).forEach(key => { if (values[key] === "") values[key] = null; });
    try {
      await onSubmit(values, form);
      formDialog.close();
    } catch (error) {
      errorBox.textContent = error.message;
      errorBox.classList.remove("hidden");
    } finally {
      submit.disabled = false;
    }
  };
  formDialog.showModal();
}

function field(name, label, type = "text", required = false, options = []) {
  return { name, label, type, required, options };
}

function formField(item) {
  const required = item.required ? " required" : "";
  if (item.type === "textarea") return `<label class="field span-2"><span>${escapeHtml(item.label)}</span><textarea name="${escapeHtml(item.name)}"${required}></textarea></label>`;
  if (item.type === "select") return `<label class="field"><span>${escapeHtml(item.label)}</span><select name="${escapeHtml(item.name)}"${required}>${item.options.map(value => `<option>${escapeHtml(value)}</option>`).join("")}</select></label>`;
  return `<label class="field"><span>${escapeHtml(item.label)}</span><input type="${escapeHtml(item.type)}" name="${escapeHtml(item.name)}"${required}></label>`;
}

async function jsonMutation(url, method, body) {
  await api(url, { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  await reloadSubmission();
}

async function reloadSubmission() {
  const submission = await api(`/api/submissions/${encodeURIComponent(state.submission.id)}`);
  setSubmission(submission);
}

async function api(url, options = {}) {
  const response = await fetch(url, options);
  const type = response.headers.get("content-type") || "";
  const payload = type.includes("json") ? await response.json() : await response.text();
  if (!response.ok) {
    throw new Error(payload?.error || payload?.detail || payload?.title || payload || `Request failed (${response.status}).`);
  }
  return payload;
}

function kpi(label, value, note, tone = "") {
  return `<div class="kpi"><span class="label">${escapeHtml(label)}</span><strong class="${tone}">${escapeHtml(String(value))}</strong><small class="${tone}">${escapeHtml(note || "")}</small></div>`;
}

function changeRow(item) {
  const tone = item.direction === "Increase" ? "blue" : item.direction === "Decrease" ? "red" : "amber";
  return `<div class="change-item"><span>${escapeHtml(item.fieldOrMetric)}</span><small>${escapeHtml(item.priorValue)} → ${escapeHtml(item.currentValue)}</small><span class="badge ${item.isMaterial ? tone : ""}">${item.isMaterial ? "Material" : escapeHtml(item.direction)}</span></div>`;
}

function priorityRow(item) {
  return `<div class="priority-item"><div><b>${escapeHtml(item.issue)}</b><small>${escapeHtml(text(item.owner))} · ${item.dueDate ? formatDate(item.dueDate) : "No due date"}</small></div>${badge(item.status)}</div>`;
}

function areaRow(item) {
  const width = statusWidth(item.status);
  const tone = width >= 80 ? "" : width >= 40 ? "amber" : "red";
  return `<div class="metric-row"><span>${escapeHtml(item.area)}</span><div class="bar-track"><div class="bar-fill ${tone}" style="width:${width}%"></div></div>${badge(item.status)}</div>`;
}

function capacityRow(item) {
  const width = statusWidth(item.status);
  return `<div class="metric-row"><span>${escapeHtml(item.area)}</span><div class="bar-track"><div class="bar-fill ${width >= 80 ? "" : "amber"}" style="width:${width}%"></div></div>${badge(item.status)}</div>`;
}

function evidenceCategoryRow(item) {
  const percentage = item.total ? Math.round(item.verified / item.total * 100) : 0;
  return `<div class="metric-row"><span>${escapeHtml(item.name)}</span><div class="bar-track"><div class="bar-fill ${percentage >= 80 ? "" : "amber"}" style="width:${percentage}%"></div></div><span class="value">${percentage}%</span></div>`;
}

function statusItem(label, value, tone = "") {
  return `<div class="status-item"><span>${escapeHtml(label)}</span>${badge(String(value), tone)}</div>`;
}

function labelValue(label, value) {
  return `<div class="status-item"><span class="value">${escapeHtml(label)}</span><strong>${escapeHtml(text(value))}</strong></div>`;
}

function badge(value, tone = badgeTone(value)) {
  return `<span class="badge ${tone}">${escapeHtml(text(value))}</span>`;
}

function donut(parts, center, centerLabel) {
  const clean = parts.filter(([, value]) => Number(value) > 0);
  const total = clean.reduce((sum, [, value]) => sum + Number(value), 0);
  if (!total) return empty("No classified values are available.");
  const colors = { green: "var(--green)", amber: "var(--amber)", red: "var(--red)", blue: "var(--blue)", violet: "var(--violet)" };
  let offset = 0;
  const stops = clean.map(([, value, tone]) => {
    const start = offset;
    offset += Number(value) / total * 100;
    return `${colors[tone] || colors.blue} ${start}% ${offset}%`;
  }).join(",");
  return `<div class="donut-layout"><div class="donut" style="background:conic-gradient(${stops})"><div class="donut-center">${escapeHtml(center)}<small>${escapeHtml(centerLabel)}</small></div></div><div class="legend">${clean.map(([label, value, tone]) => `<div class="legend-row"><i class="${tone}"></i><span>${escapeHtml(label)}</span><b>${number(value)}</b></div>`).join("")}</div></div>`;
}

function propertiesTable(items) {
  return table(["Property", "Location", "Units", "Status", "Risk", "DSCR", "YoY NOI", "Data gaps"], items.map(item => [
    `<b>${escapeHtml(item.name)}</b><br><small>${escapeHtml(item.propertyId)}</small>`,
    escapeHtml(text(item.city)), number(item.units), escapeHtml(text(item.status)), badge(item.riskLevel), multiple(item.dscr), nullablePercent(item.yoyNoiChange), number(item.dataGaps)
  ]), index => `data-property-id="${escapeHtml(items[index].propertyId)}" class="clickable"`);
}

function propertyDetail(item) {
  if (!item) return "";
  return `<div class="property-detail"><div class="detail-head"><div><h3>${escapeHtml(item.name)}</h3><span class="muted">${escapeHtml(item.propertyId)} · ${escapeHtml(text(item.city))}</span></div><button class="icon-button" data-action="close-property">×</button></div><div class="detail-grid">
    ${detail("Included", item.included ? "Yes" : "No")}${detail("Classification", item.classification)}${detail("Risk", item.riskLevel)}${detail("Data status", item.dataStatus)}
    ${detail("Modeled units", number(item.units))}${detail("Alternate units", item.alternateUnits == null ? "—" : number(item.alternateUnits))}${detail("NOI", nullableMoney(item.noi))}${detail("DSCR", multiple(item.dscr))}
    ${detail("Affordable", number(item.affordableUnits))}${detail("Market", number(item.marketUnits))}${detail("Supportive", number(item.supportiveUnits))}${detail("Unit conflict", item.unitConflict ? "Needs review" : "No")}
  </div></div>`;
}

function detail(label, value) { return `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(text(value))}</strong></div>`; }

function exceptionTable(items) {
  return table(["Property", "Current", "Alternate", "Status"], items.map(item => [escapeHtml(item.name), number(item.units), item.alternateUnits == null ? "—" : number(item.alternateUnits), badge(item.unitConflict ? "Conflict" : `${item.dataGaps} gaps`)]));
}

function compactObligationTable(items) {
  return table(["Obligation", "Due", "Status"], items.map(item => [escapeHtml(item.obligation), item.dueDate ? formatDate(item.dueDate) : "Not dated", badge(item.status)]));
}

function obligationsTable(items) {
  return table(["ID", "Source", "Obligation", "Owner", "Due / renewal", "Status", "Evidence"], items.map(item => [escapeHtml(item.obligationId), escapeHtml(text(item.source)), escapeHtml(item.obligation), escapeHtml(text(item.owner)), item.dueDate ? formatDate(item.dueDate) : "Not dated", badge(item.status), escapeHtml(text(item.evidenceReference))]));
}

function decisionRightsTable(items) {
  if (!items.length) return empty("Decision-right controls were not supplied; status is Not assessed.");
  return table(["Control", "Decision body", "Status"], items.map(item => [escapeHtml(item.control), escapeHtml(item.decisionBody), badge(item.status)]));
}

function agreementsTable(items) {
  return table(["Agreement", "Expiry", "Impact"], items.map(item => [escapeHtml(item.agreementType), item.expiryDate ? formatDate(item.expiryDate) : "Not dated", badge(item.impact)]));
}

function outcomesTable(items) {
  return table(["Outcome", "Current evidence", "Primary source", "Status"], items.map(item => [escapeHtml(item.outcome), escapeHtml(item.currentEvidence), escapeHtml(item.primarySource), badge(item.status)]));
}

function actionsTable(items, editable) {
  const headings = ["Issue", "Entity", "Owner", "Due", "Decision body", "Status", "Risk"];
  if (editable) headings.push("Action");
  return table(headings, items.map(item => {
    const row = [escapeHtml(item.issue), escapeHtml(item.entityId), escapeHtml(text(item.owner)), item.dueDate ? formatDate(item.dueDate) : "Not dated", escapeHtml(text(item.decisionBody)), badge(item.status), badge(item.riskLevel)];
    if (editable) row.push(`<button class="button small secondary" data-action="close-action" data-id="${escapeHtml(item.actionId)}">Complete</button>`);
    return row;
  }));
}

function sourceTable(items) {
  return table(["Source file", "Type", "Date", "Status"], items.slice(0, 7).map(item => [escapeHtml(item.fileName), escapeHtml(item.sourceType), formatDate(item.uploadedUtc), badge(item.reviewStatus)]));
}

function evidenceTable(items) {
  return table(["Field", "Value", "Entity", "Source", "Location", "Reviewer", "Status"], items.map(item => [escapeHtml(item.fieldName), escapeHtml(item.value), escapeHtml(item.entityId), escapeHtml(item.sourceFile), escapeHtml(item.sourceLocation), escapeHtml(text(item.reviewer)), badge(item.verificationStatus)]));
}

function reportsTable(items, versionStatus) {
  return table(["Report", "Purpose", "Period", "Version", "Based on", "Status", "Recipient", "Controls"], items.map(item => {
    const controls = [];
    if (item.status === "Draft") controls.push(`<button class="button small secondary" data-action="approve-report" data-id="${escapeHtml(item.reportId)}" ${versionStatus !== "Approved" ? "disabled title=\"Approve the assessment first\"" : ""}>Approve</button>`);
    if (item.status === "Approved") controls.push(`<button class="button small secondary" data-action="share-report" data-id="${escapeHtml(item.reportId)}">Share</button>`);
    if (item.artifactAvailable) controls.push(`<button class="button small secondary" data-action="download-report" data-id="${escapeHtml(item.reportId)}">Download</button>`);
    return [escapeHtml(item.reportName), escapeHtml(item.purpose), escapeHtml(item.period), `v${number(item.reportVersion)}`, escapeHtml(item.versionId), badge(item.status), escapeHtml(item.recipient), `<div class="toolbar">${controls.join("")}</div>`];
  }));
}

function eventTable(events, obligations) {
  const rows = events.map(item => [item.eventName, item.eventDate, item.priority, item.eventType])
    .concat(obligations.filter(item => item.dueDate).map(item => [item.obligation, item.dueDate, "Medium", "Obligation"]))
    .sort((a, b) => new Date(a[1]) - new Date(b[1])).slice(0, 8)
    .map(item => [escapeHtml(item[0]), formatDate(item[1]), badge(item[2]), escapeHtml(item[3])]);
  return table(["Event", "Date", "Priority", "Type"], rows);
}

function futureEventsTable(items) {
  return table(["Event", "Timing", "Priority", "Impact"], items.slice(0, 8).map(item => [escapeHtml(item.eventName), item.eventDate ? formatDate(item.eventDate) : "Not dated", badge(item.priority), escapeHtml(text(item.impact))]));
}

function simpleTable(headings, rows) {
  return table(headings, rows.map(row => row.map(value => escapeHtml(value))));
}

function table(headings, rows, rowAttributes) {
  if (!rows.length) return `<div class="table-wrap"><div class="empty-row">No records available.</div></div>`;
  return `<div class="table-wrap"><table><thead><tr>${headings.map(value => `<th>${escapeHtml(value)}</th>`).join("")}</tr></thead><tbody>${rows.map((row, index) => `<tr ${rowAttributes ? rowAttributes(index) : ""}>${row.map(value => `<td>${value}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`;
}

function trendChart(points) {
  const valid = points.filter(item => item.dscr != null && Number.isFinite(Number(item.dscr)));
  if (!valid.length) return empty("DSCR trend is not assessed.");
  const values = valid.map(item => Number(item.dscr));
  let min = Math.min(...values); let max = Math.max(...values);
  if (min === max) { min -= .2; max += .2; }
  const x = index => valid.length === 1 ? 380 : 50 + index * 660 / (valid.length - 1);
  const y = value => 185 - (value - min) * 135 / (max - min);
  const polyline = valid.map((item, index) => `${x(index)},${y(Number(item.dscr))}`).join(" ");
  return `<div class="trend-chart"><svg viewBox="0 0 760 230" role="img" aria-label="DSCR trend"><line class="gridline" x1="40" y1="50" x2="720" y2="50"/><line class="gridline" x1="40" y1="118" x2="720" y2="118"/><line class="gridline" x1="40" y1="185" x2="720" y2="185"/><polyline class="series" points="${polyline}"/>${valid.map((item, index) => `<circle class="point" cx="${x(index)}" cy="${y(Number(item.dscr))}" r="5"/><text x="${x(index)}" y="${Math.max(18, y(Number(item.dscr)) - 12)}" text-anchor="middle">${Number(item.dscr).toFixed(2)}x</text><text x="${x(index)}" y="215" text-anchor="middle">${escapeHtml(item.period)}</text>`).join("")}</svg></div>`;
}

function debtBarChart(items) {
  if (!items.length) return empty("Debt maturity dates are not assessed from the supplied evidence.");
  const max = Math.max(...items.map(item => Number(item.amount) || 0), 1);
  return `<div class="bar-chart">${items.slice(0, 7).map(item => `<div class="bar-column"><b>${moneyCompact(item.amount)}</b><div class="column" style="height:${Math.max(3, Number(item.amount) / max * 190)}px"></div><small>${escapeHtml(String(item.year || "—"))}</small></div>`).join("")}</div>`;
}

function missionAlignment(d) {
  const mission = (d.assessmentAreas || []).find(item => normalized(item.area).includes("mission"));
  const items = [
    ["Affordable housing evidence", d.affordableUnits > 0 ? "Evidence available" : "Not assessed"],
    ["Supportive housing evidence", d.supportiveUnits > 0 ? "Evidence available" : "Not assessed"],
    ["Agreement restrictions", (d.agreements || []).length ? "Needs review" : "Not assessed"],
    ["Unit classification completeness", d.unclassifiedUnits > 0 ? "Conditional" : "Evidence available"],
    ["Overall mission assessment", mission?.status || "Not assessed"]
  ];
  return items.map(([label, status]) => statusItem(label, status, badgeTone(status))).join("");
}

function evidenceCategories(items) {
  const map = new Map();
  items.forEach(item => {
    const key = item.entityType === "Property" ? "Property / units" : financialField(item.fieldName) ? "Financial" : "Portfolio / governance";
    const current = map.get(key) || { name: key, total: 0, verified: 0 };
    current.total += 1;
    if (normalized(item.verificationStatus) === "verified") current.verified += 1;
    map.set(key, current);
  });
  return [...map.values()];
}

function financialField(value) { return ["revenue", "noi", "debtservice", "dscr", "liquidity", "capital"].some(word => normalized(value).includes(word)); }
function propertyRiskCounts(items) {
  const counts = { stable: 0, watch: 0, high: 0, notAssessed: 0 };
  items.forEach(item => {
    const risk = normalized(item.riskLevel);
    if (!risk || risk === "notassessed") counts.notAssessed++;
    else if (risk === "high" || risk === "critical") counts.high++;
    else if (risk === "watch" || risk === "medium" || risk === "conditional") counts.watch++;
    else counts.stable++;
  });
  return counts;
}

function statusWidth(value) {
  const key = normalized(value);
  if (["evidenceavailable", "verified", "approved", "readyforreview", "good", "adequate"].includes(key)) return 90;
  if (["conditional", "needsreview", "moderate", "developing", "working"].includes(key)) return 62;
  return 12;
}

function badgeTone(value) {
  const key = normalized(value);
  if (["approved", "verified", "complete", "completed", "closed", "stable", "good", "adequate", "evidenceavailable", "readyforreview", "shared"].some(word => key.includes(word))) return "green";
  if (["high", "critical", "overdue", "conflict", "missing", "failed", "atrisk"].some(word => key.includes(word))) return "red";
  if (["conditional", "review", "moderate", "watch", "draft", "new", "open", "upcoming", "developing"].some(word => key.includes(word))) return "amber";
  if (["working", "progress", "active", "recorded"].some(word => key.includes(word))) return "blue";
  return "";
}

function statusTone(value) {
  const tone = badgeTone(value);
  return tone === "green" ? "positive" : tone === "red" ? "negative" : tone === "amber" ? "warning" : "info";
}

function text(value) { return value == null || value === "" ? "Not assessed" : String(value); }
function number(value) { return value == null || Number.isNaN(Number(value)) ? "—" : new Intl.NumberFormat("en-CA", { maximumFractionDigits: 0 }).format(Number(value)); }
function money(value) { return value == null ? "Not assessed" : new Intl.NumberFormat("en-CA", { style: "currency", currency: "CAD", maximumFractionDigits: Math.abs(Number(value)) >= 1e6 ? 1 : 0 }).format(Number(value)); }
function nullableMoney(value) { return value == null ? "Not assessed" : money(value); }
function moneyCompact(value) { return value == null ? "—" : new Intl.NumberFormat("en-CA", { style: "currency", currency: "CAD", notation: "compact", maximumFractionDigits: 1 }).format(Number(value)); }
function multiple(value) { return value == null ? "Not assessed" : `${Number(value).toFixed(2)}x`; }
function percent(value) { return value == null ? "Not assessed" : new Intl.NumberFormat("en-CA", { style: "percent", maximumFractionDigits: 1 }).format(Number(value)); }
function nullablePercent(value) { return value == null ? "Not assessed" : percent(value); }
function percentOf(value, total) { return Number(total) > 0 ? percent(Number(value) / Number(total)) : "Not assessed"; }
function periodLabel(s) { return s.reportingPeriod ? `Reporting period ${s.reportingPeriod}` : "Reporting period not assessed"; }
function normalized(value) { return String(value || "").toLowerCase().replace(/[^a-z0-9]/g, ""); }
function isClosed(value) { return ["closed", "complete", "completed", "resolved"].includes(normalized(value)); }
function formatDate(value) { if (!value) return "Not dated"; const date = new Date(value); return Number.isNaN(date.valueOf()) ? text(value) : new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" }).format(date); }
function daysAgo(days) { const date = new Date(); date.setDate(date.getDate() - days); return date; }
function dueWithin(value, days) { if (!value) return false; const date = new Date(value); const now = new Date(); return date >= now && date <= new Date(now.valueOf() + days * 86400000); }
function empty(message) { return `<div class="empty-row">${escapeHtml(message)}</div>`; }
function escapeHtml(value) { return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]); }
function toast(message, error = false) { const element = document.getElementById("toast"); element.textContent = message; element.classList.toggle("error", error); element.classList.remove("hidden"); clearTimeout(toast.timer); toast.timer = setTimeout(() => element.classList.add("hidden"), 4200); }
