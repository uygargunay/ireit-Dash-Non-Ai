const state = {
  submission: null,
  page: "upload"
};

const titles = {
  upload: "Excel Intake & Standardization",
  summary: "Executive / Portfolio Summary",
  portfolio: "Portfolio Overview",
  financial: "Financial Performance & Capital Capacity",
  readiness: "Readiness & Risk Overview",
  mission: "Mission, Impact & Affordability"
};

const content = document.getElementById("content");
const pageTitle = document.getElementById("page-title");
const nav = document.getElementById("nav");

nav.addEventListener("click", event => {
  const button = event.target.closest("button[data-page]");
  if (!button) return;

  const page = button.dataset.page;
  if (page !== "upload" && !state.submission) {
    renderUpload("Process a workbook before opening the dashboard.");
    return;
  }

  setPage(page);
});

function setPage(page) {
  state.page = page;
  pageTitle.textContent = titles[page];

  nav.querySelectorAll("button").forEach(button => {
    button.classList.toggle("active", button.dataset.page === page);
  });

  const renderers = {
    upload: () => renderUpload(),
    summary: () => renderSummary(state.submission.dashboard),
    portfolio: () => renderPortfolio(state.submission.dashboard),
    financial: () => renderFinancial(state.submission.dashboard),
    readiness: () => renderReadiness(state.submission.dashboard),
    mission: () => renderMission(state.submission.dashboard)
  };

  renderers[page]();
}

function renderUpload(message = "") {
  const template = document.getElementById("upload-template");
  content.replaceChildren(template.content.cloneNode(true));

  const form = document.getElementById("upload-form");
  const fileInput = document.getElementById("file-input");
  const selectedFile = document.getElementById("selected-file");
  const dropZone = document.getElementById("drop-zone");

  if (message) showError(message);

  fileInput.addEventListener("change", () => {
    selectedFile.textContent = fileInput.files[0]?.name || "Maximum 50 MB";
  });

  ["dragenter", "dragover"].forEach(type => {
    dropZone.addEventListener(type, event => {
      event.preventDefault();
      dropZone.classList.add("dragover");
    });
  });

  ["dragleave", "drop"].forEach(type => {
    dropZone.addEventListener(type, event => {
      event.preventDefault();
      dropZone.classList.remove("dragover");
    });
  });

  dropZone.addEventListener("drop", event => {
    if (event.dataTransfer.files.length) {
      const transfer = new DataTransfer();
      transfer.items.add(event.dataTransfer.files[0]);
      fileInput.files = transfer.files;
      selectedFile.textContent = fileInput.files[0].name;
    }
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    hideError();

    if (!fileInput.files.length) {
      showError("Select an .xlsx workbook.");
      return;
    }

    if (!fileInput.files[0].name.toLowerCase().endsWith(".xlsx")) {
      showError("The MVP accepts .xlsx workbooks only.");
      return;
    }

    setProcessing(true);

    try {
      const formData = new FormData();
      formData.append("file", fileInput.files[0]);
      formData.append(
        "organizationName",
        document.getElementById("organization-name").value.trim()
      );

      const response = await fetch("/api/submissions", {
        method: "POST",
        body: formData
      });

      state.submission = await parseResponse(response);
      setPage("summary");
    } catch (error) {
      showError(error.message);
    } finally {
      setProcessing(false);
    }
  });
}

function renderSummary(d) {
  content.innerHTML = `
    ${dashboardHead(d)}
    <div class="kpi-grid">
      ${kpi("⌂", "Properties", formatNumber(d.totalProperties), `${formatNumber(d.includedProperties)} included`)}
      ${kpi("▦", "Modeled units", formatNumber(d.modeledUnits), "Residential / project units")}
      ${kpi("$", "NOI", money(d.noi), "Simplified snapshot")}
      ${kpi("◇", "DSCR", multiple(d.dscr), "NOI / debt service")}
      ${kpi("◎", "AFFO", money(d.affo), "After debt service")}
      ${kpi("✓", "Overall readiness", `${overallScore(d)}/100`, d.overallStatus)}
    </div>

    <div class="grid-2">
      <section class="panel section-panel">
        <h3>Portfolio highlights</h3>
        <ul class="highlight-list">
          <li>${formatNumber(d.totalProperties)} property or project records were identified.</li>
          <li>${formatNumber(d.existingAssets)} existing assets and ${formatNumber(d.newProjects)} new or future projects are represented.</li>
          <li>Revenue of ${money(d.revenue)} and operating expenses of ${money(d.operatingExpenses)} appear in the first-pass financial snapshot.</li>
          <li>Mission and affordability evidence remain distinct from, but interpreted beside, financial capacity.</li>
          <li>The generated Excel workbook is retained for authorized IREI administration and review.</li>
        </ul>
      </section>

      <section class="panel section-panel">
        <h3>IREI readiness position</h3>
        <div class="readiness-wrap">
          ${readinessRing(overallScore(d))}
          <div>
            <div class="section-kicker">CURRENT CLASSIFICATION</div>
            <h2>${escapeHtml(d.overallStatus)}</h2>
            <p class="lead">First-pass Stage 1 output. Material conclusions require organizational and professional review.</p>
          </div>
        </div>
      </section>
    </div>

    <div class="grid-2">
      <section class="panel section-panel">
        <h3>Stage routing</h3>
        <div class="pathway">
          <div class="pathway-step active"><b>Stage 1</b><span>Understand, standardize and resolve evidence gaps</span></div>
          <div class="pathway-step"><b>Stage 2</b><span>Address a defined funding or capital gap</span></div>
          <div class="pathway-step"><b>Stage 3</b><span>Scale selected, evidence-gated pathways</span></div>
        </div>
      </section>

      <section class="panel section-panel">
        <h3>Open review items</h3>
        ${warningList(d.warnings)}
      </section>
    </div>
  `;
}

function renderPortfolio(d) {
  const rows = (d.properties || []).map(property => `
    <tr>
      <td><strong>${escapeHtml(property.name)}</strong><br><small>${escapeHtml(property.propertyId)}</small></td>
      <td>${escapeHtml(property.city || "—")}</td>
      <td>${escapeHtml(property.status || "—")}</td>
      <td>${formatNumber(property.units)}</td>
      <td>${property.included ? "Yes" : "No"}</td>
      <td>${escapeHtml(property.classification || "Review")}</td>
    </tr>
  `).join("");

  const existingPercent = d.totalProperties ? d.existingAssets / d.totalProperties * 100 : 0;
  const newPercent = d.totalProperties ? d.newProjects / d.totalProperties * 100 : 0;
  const otherCount = Math.max(0, d.totalProperties - d.existingAssets - d.newProjects);
  const otherPercent = d.totalProperties ? otherCount / d.totalProperties * 100 : 0;

  content.innerHTML = `
    ${dashboardHead(d)}
    <div class="kpi-grid">
      ${kpi("⌂", "Properties", formatNumber(d.totalProperties), "Portfolio / project records")}
      ${kpi("▦", "Modeled units", formatNumber(d.modeledUnits), "Source-based where available")}
      ${kpi("✓", "Included", formatNumber(d.includedProperties), "Current analysis scope")}
      ${kpi("●", "Existing assets", formatNumber(d.existingAssets), "Active / operating")}
      ${kpi("↑", "New projects", formatNumber(d.newProjects), "Development / future")}
      ${kpi("!", "Open flags", formatNumber(d.warnings?.length || 0), "Review before external use")}
    </div>

    <div class="grid-3">
      <section class="panel section-panel">
        <h3>Portfolio composition</h3>
        <div class="bar-stack">
          ${barRow("Existing / active", existingPercent, d.existingAssets)}
          ${barRow("New / future", newPercent, d.newProjects)}
          ${barRow("Other / unclassified", otherPercent, otherCount)}
        </div>
      </section>

      <section class="panel section-panel">
        <h3>Scope interpretation</h3>
        <ul class="highlight-list">
          <li>Properties are read as one connected organization and portfolio.</li>
          <li>Included and excluded assets remain explicit.</li>
          <li>Unit conflicts and classification gaps are not silently resolved.</li>
        </ul>
      </section>

      <section class="panel section-panel">
        <h3>Mapping profile</h3>
        <p><strong>${escapeHtml(d.profileName || "Smart mapping")}</strong></p>
        <p class="lead">The profile determines how source fields are placed into the canonical IREI template.</p>
      </section>
    </div>

    <section class="panel section-panel">
      <h3>Property summary</h3>
      <div class="table-scroll">
        <table class="data-table">
          <thead><tr><th>Property</th><th>City</th><th>Status</th><th>Units</th><th>Included</th><th>Classification</th></tr></thead>
          <tbody>${rows || `<tr><td colspan="6">No property rows were confidently mapped.</td></tr>`}</tbody>
        </table>
      </div>
    </section>
  `;
}

function renderFinancial(d) {
  const horizon = d.debtHorizon || [];
  const maximum = Math.max(...horizon.map(point => Number(point.amount)), 1);

  const chart = horizon.length
    ? horizon.map(point => `
        <div class="debt-column">
          <b>${moneyCompact(point.amount)}</b>
          <div class="debt-bar" style="height:${Math.max(5, Number(point.amount) / maximum * 210)}px"></div>
          <small>${point.year}</small>
        </div>
      `).join("")
    : `<div class="empty-state">A debt term horizon could not be derived from the uploaded workbook.</div>`;

  content.innerHTML = `
    ${dashboardHead(d)}
    <div class="kpi-grid">
      ${kpi("$", "Operating revenue", money(d.revenue), "Annual / selected period")}
      ${kpi("−", "Operating expenses", money(d.operatingExpenses), "Annual / selected period")}
      ${kpi("↑", "NOI", money(d.noi), "Revenue less expenses")}
      ${kpi("▤", "Debt service", money(d.debtService), "Source / formula based")}
      ${kpi("◎", "AFFO", money(d.affo), "Simplified cash flow")}
      ${kpi("◇", "DSCR", multiple(d.dscr), "NOI / debt service")}
    </div>

    <div class="grid-2">
      <section class="panel section-panel">
        <h3>Debt term horizon</h3>
        <p class="lead">Derived from source principal and term fields when available. It is not a confirmed maturity schedule unless verified.</p>
        <div class="debt-chart">${chart}</div>
      </section>

      <section class="panel section-panel">
        <h3>Key financial ratios</h3>
        <table class="data-table">
          <tbody>
            <tr><td>NOI margin</td><td><strong>${percent(d.revenue ? d.noi / d.revenue : 0)}</strong></td></tr>
            <tr><td>Debt coverage ratio</td><td><strong>${multiple(d.dscr)}</strong></td></tr>
            <tr><td>Cash flow after debt</td><td><strong>${money(d.affo)}</strong></td></tr>
            <tr><td>Financial readiness</td><td><strong>${d.financialReadinessScore}/100</strong></td></tr>
          </tbody>
        </table>
      </section>
    </div>

    <section class="panel section-panel">
      <h3>Interpretation</h3>
      <div class="mission-banner">
        <strong>Mission-aligned capital must fit the evidence</strong>
        <p>Capital is not recommended solely because it improves a ratio. Timing, repayment source, restrictions, competing obligations and mission protections require review.</p>
      </div>
      ${warningList(d.warnings)}
    </section>
  `;
}

function renderReadiness(d) {
  const scores = [
    ["Data readiness", d.dataReadinessScore],
    ["Financial readiness", d.financialReadinessScore],
    ["Governance readiness", d.governanceReadinessScore],
    ["Impact readiness", d.impactReadinessScore],
    ["Risk management", d.riskManagementScore]
  ];

  const riskRows = (d.riskItems || []).map(item => `
    <div class="risk-row">
      <strong>${escapeHtml(item.name)}</strong>
      <span class="risk-level ${String(item.level).toLowerCase()}">${escapeHtml(item.level)}</span>
      <p>${escapeHtml(item.note)}</p>
    </div>
  `).join("");

  content.innerHTML = `
    ${dashboardHead(d)}
    <div class="grid-2">
      <section class="panel section-panel">
        <h3>Readiness scorecard</h3>
        <div class="score-list">
          ${scores.map(([name, score]) => scoreRow(name, score)).join("")}
        </div>
      </section>

      <section class="panel section-panel">
        <h3>Overall readiness</h3>
        <div class="readiness-wrap">
          ${readinessRing(overallScore(d))}
          <div>
            <div class="section-kicker">STAGE 1 RESULT</div>
            <h2>${escapeHtml(d.overallStatus)}</h2>
            <p class="lead">Evidence gaps remain visible rather than being converted into a false positive score.</p>
          </div>
        </div>
      </section>
    </div>

    <div class="grid-2">
      <section class="panel section-panel">
        <h3>Risk overview</h3>
        <div class="risk-list">${riskRows || `<div class="empty-state">No risk indicators were returned.</div>`}</div>
      </section>

      <section class="panel section-panel">
        <h3>Risk mitigation and protections</h3>
        <div class="protection-grid">
          ${protection("Mission control", "Organization review and disclosure control are retained.")}
          ${protection("Affordability evidence", "Affordability remains source-backed and separately visible.")}
          ${protection("Conservative routing", "No automatic progression to a funding or capital product.")}
          ${protection("Audit trail", "Source files, mappings, assumptions and flags remain reviewable.")}
          ${protection("Professional review", "Material conclusions require human validation.")}
          ${protection("Selective reuse", "External evidence is released only through authorization.")}
        </div>
      </section>
    </div>
  `;
}

function renderMission(d) {
  const affordabilityBase = Math.max(d.modeledUnits, d.affordableUnits + d.marketUnits, 1);
  const affordabilityPercent = Math.min(100, d.affordableUnits / affordabilityBase * 100);

  content.innerHTML = `
    ${dashboardHead(d)}
    <div class="kpi-grid">
      ${kpi("⌂", "Modeled units", formatNumber(d.modeledUnits), "Portfolio / project scope")}
      ${kpi("♥", "Affordable units", formatNumber(d.affordableUnits), "Source-backed where available")}
      ${kpi("○", "Market / other units", formatNumber(d.marketUnits), "Source-backed where available")}
      ${kpi("✓", "Impact readiness", `${d.impactReadinessScore}/100`, "Evidence maturity")}
      ${kpi("◇", "Mission status", escapeHtml(d.overallStatus), "Review conclusion")}
      ${kpi("!", "Open items", formatNumber(d.warnings?.length || 0), "Do not suppress")}
    </div>

    <div class="mission-banner">
      <strong>Mission focus</strong>
      <p>${escapeHtml(d.missionFocus)}</p>
    </div>

    <div class="grid-3">
      <section class="panel section-panel">
        <h3>Social and community impact</h3>
        <ul class="highlight-list">
          <li>Preserve safe, stable and affordable homes.</li>
          <li>Interpret resident and community value beside financial capacity.</li>
          <li>Support long-term stewardship rather than transaction-only analysis.</li>
          <li>Keep Nation, community and organizational authority visible where applicable.</li>
        </ul>
      </section>

      <section class="panel section-panel">
        <h3>Affordability profile</h3>
        <div class="donut" style="--percent:${affordabilityPercent}">
          <div><strong>${Math.round(affordabilityPercent)}%</strong><small>of mapped units</small></div>
        </div>
        <p class="lead">This percentage is limited to mapped affordability evidence and must not be treated as complete where unit mix is partial.</p>
      </section>

      <section class="panel section-panel">
        <h3>Mission protections</h3>
        <div class="protection-grid">
          ${protection("Long-term affordability", "Record covenants, restrictions and affordability terms.")}
          ${protection("Responsible stewardship", "Interpret asset care and resident outcomes together.")}
          ${protection("Public benefit", "Keep measurable community benefit separate from financial return.")}
          ${protection("Transparency", "Retain sources, assumptions, conflicts and recorded disagreement.")}
        </div>
      </section>
    </div>
  `;
}

function dashboardHead(d) {
  return `
    <div class="dashboard-head">
      <div>
        <div class="section-kicker">${escapeHtml(d.profileName || "IREI SMART MAPPING")}</div>
        <h2>${escapeHtml(d.organizationName || "Organization")}</h2>
        <p>Reporting period ${escapeHtml(d.reportingPeriod || "not confirmed")} · Submission ${escapeHtml(state.submission?.id || "")}</p>
      </div>
      <span class="status-pill">${escapeHtml(d.overallStatus || "Conditional")}</span>
    </div>
  `;
}

function kpi(icon, label, value, note) {
  return `
    <div class="kpi">
      <div class="kpi-icon">${icon}</div>
      <div class="kpi-label">${label}</div>
      <div class="kpi-value">${value}</div>
      <div class="kpi-note">${note}</div>
    </div>
  `;
}

function readinessRing(score) {
  return `
    <div class="readiness-ring" style="--score:${score}">
      <div><strong>${score}</strong><small>/100</small></div>
    </div>
  `;
}

function scoreRow(name, score) {
  return `
    <div class="score-row">
      <strong>${name}</strong><strong>${score}/100</strong>
      <div class="bar-track"><div class="bar-fill" style="width:${score}%"></div></div>
    </div>
  `;
}

function barRow(name, percentValue, count) {
  return `
    <div class="bar-row">
      <span>${name}</span>
      <div class="bar-track"><div class="bar-fill" style="width:${Math.max(0, Math.min(100, percentValue))}%"></div></div>
      <strong>${count}</strong>
    </div>
  `;
}

function warningList(warnings) {
  if (!warnings || !warnings.length) {
    return `<div class="alert warning">No open items were generated. A reviewer must still confirm the source package and mappings.</div>`;
  }

  return `<ul class="highlight-list">${warnings.map(item => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`;
}

function protection(title, text) {
  return `<div class="protection-card"><strong>${title}</strong><span>${text}</span></div>`;
}

function overallScore(d) {
  const values = [
    d.dataReadinessScore,
    d.financialReadinessScore,
    d.governanceReadinessScore,
    d.impactReadinessScore,
    d.riskManagementScore
  ].map(Number);

  return Math.round(values.reduce((sum, value) => sum + value, 0) / values.length);
}

function setProcessing(active) {
  document.getElementById("processing")?.classList.toggle("hidden", !active);
  document.querySelectorAll("#upload-form button").forEach(button => button.disabled = active);
}

function showError(message) {
  const box = document.getElementById("error-box");
  if (!box) return;
  box.textContent = message;
  box.classList.remove("hidden");
}

function hideError() {
  document.getElementById("error-box")?.classList.add("hidden");
}

async function parseResponse(response) {
  const contentType = response.headers.get("content-type") || "";
  const body = contentType.includes("application/json")
    ? await response.json()
    : { error: await response.text() };

  if (!response.ok) {
    throw new Error(body.error || body.detail || body.title || "The request failed.");
  }

  return body;
}

function money(value) {
  const number = Number(value || 0);
  const absolute = Math.abs(number);
  const formatted = new Intl.NumberFormat("en-CA", {
    style: "currency",
    currency: "CAD",
    maximumFractionDigits: absolute >= 1000000 ? 1 : 0,
    notation: absolute >= 1000000 ? "compact" : "standard"
  }).format(absolute);

  return number < 0 ? `(${formatted})` : formatted;
}

function moneyCompact(value) {
  return new Intl.NumberFormat("en-CA", {
    style: "currency",
    currency: "CAD",
    notation: "compact",
    maximumFractionDigits: 1
  }).format(Number(value || 0));
}

function multiple(value) {
  return `${Number(value || 0).toFixed(2)}x`;
}

function percent(value) {
  return `${(Number(value || 0) * 100).toFixed(1)}%`;
}

function formatNumber(value) {
  return new Intl.NumberFormat("en-CA").format(Number(value || 0));
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

setPage("upload");
