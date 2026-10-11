'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const labels = ['Executive Summary', 'Portfolio & Mission', 'Financial & Debt', 'Evidence', 'Risk & Options', 'Updates & Actions', 'Reports'];
  const titles = ['Executive portfolio summary', 'Portfolio and mission', 'Financial performance and capital capacity', 'Evidence', 'Risks and options', 'Updates and actions', 'Reports and authorized outputs'];
  const icon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="M12 3 4 6v6c0 5 8 9 8 9s8-4 8-9V6z"/><path d="m8 12 3 3 5-6"/></svg>';
  let id = new URLSearchParams(location.search).get('assessment') || '';
  let page = /^#P[1-7]$/.test(location.hash) ? location.hash.slice(1) : 'P1';
  let key = ''; let data = null;
  // Formatting only: all financial values, counts, option results and chart dimensions are server-owned.
  const money = v => v == null ? 'No data available' : v === 0 ? '$0' : `${v < 0 ? '-' : ''}$${(Math.abs(v) / 1000000).toFixed(2)}M`;
  const ratio = v => v == null ? 'No data available' : `${v.toFixed(v >= .99 && v < 1 ? 3 : 2)}x`;
  const count = v => v == null ? 'No data available' : Number(v).toLocaleString('en-CA', { maximumFractionDigits: 0 });
  const format = m => !m || m.value == null ? (m?.valueState === 'Not Applicable' ? 'Not applicable' : 'No data available') :
    m.code.includes('DSCR') ? ratio(m.value) : m.code === 'HOMES_TOTAL' ? count(m.value) : money(m.value);
  const metric = (code, full = false) => (full ? data.fullOperationMetrics : data.currentMetrics).find(m => m.code === code);
  const metricButton = m => `<button type="button" class="detail-link" data-metric="${esc(m?.id || '')}">${esc(format(m))}</button>`;
  const field = (r, key) => r.fields?.[key] || '';
  const rows = sheet => data.registers[sheet] || [];
  const empty = (text = 'No data available.') => `<p class="empty">${esc(text)}</p>`;
  const table = (headers, values) => values.length ? `<div class="table-wrap"><table><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${values.map(r => `<tr>${r.map(c => `<td>${c}</td>`).join('')}</tr>`).join('')}</tbody></table></div>` : empty();
  const callout = text => `<div class="callout"><b>What it means</b>${esc(text)}</div>`;
  const panel = (title, body, note = '', cls = '') => `<section class="panel ${cls}"><h2>${icon}${esc(title)}</h2>${body}${note ? callout(note) : ''}</section>`;
  const chart = (points, unit) => `<p class="chart-title">${esc(unit)} · Source plan</p><div class="chart">${points.map(p => `<div class="bar-point ${p.negative ? 'negative' : ''}"><div class="bar-frame"><span class="bar-label ${p.value == null ? 'missing' : ''}" style="top:${Number(p.labelTop)}px">${esc(p.label)}</span>${p.value == null ? '' : `<button type="button" class="bar ${p.negative ? 'negative' : ''}" style="height:${Number(p.height)}px;top:${Number(p.top)}px" data-metric="${esc(p.id)}" aria-label="${esc(`${p.year}: ${p.label}; ${p.displayState}`)}"></button>`}</div><span class="bar-year">FY${esc(p.year.slice(-2))}</span></div>`).join('')}</div>`;
  const tiles = () => `<div class="tiles">${data.tiles.map(t => `<${t.metricId ? 'button type="button"' : 'article'} class="tile tone-${esc(t.tone?.toLowerCase() || 'teal')}" ${t.metricId ? `data-metric="${esc(t.metricId)}"` : ''}><span class="icon">${icon}</span><span class="label">${esc(t.label)}</span><span class="value ${t.value.includes('No data') ? 'missing' : ''}">${esc(t.value)}</span><span class="caption">${esc(t.caption)}</span><span class="state">${esc(t.state)}</span></${t.metricId ? 'button' : 'article'}>`).join('')}</div>`;
  function stewardship() {
    const s = data.portfolioSnapshot;
    return `<div class="stewardship"><article class="mini unknown"><h3>Affordability</h3><strong>No data available</strong><p>Actual rent rolls, household incomes and resident protections are needed.</p></article><article class="mini"><h3>Mission</h3><strong>${esc(s.existingHomes)} existing homes<br>${esc(s.plannedHomes)} planned</strong><p>Source-plan growth; occupancy and protections remain unconfirmed.</p></article><article class="mini unknown"><h3>Governance</h3><strong>${rows('39_GOVERNANCE').length ? 'Records require review' : 'No data available'}</strong><p>Reserve permissions, financing authority and control limits are needed.</p></article><article class="mini"><h3>Assets</h3><strong>${esc(format(metric('RESERVE_CONTRIBUTION')))}/year</strong><p>Projected repair set-aside. Condition and capital needs require review.</p></article></div>`;
  }
  function actions() {
    const list = rows('48_ACTIONS');
    return list.length ? `<div class="actions">${list.map(r => `<article><b>${esc(field(r, 'Short_Title') || field(r, 'Action_Text'))}</b><p>${esc(field(r, 'Detail_Text'))}</p><small>${esc(field(r, 'Owner_Name') || 'Unassigned')} · ${esc(field(r, 'Owner_Role') || 'Role to confirm')}</small><p class="review-state">${esc(field(r, 'Status'))}</p><button type="button" class="detail-link" data-record="${esc(r.id)}" data-sheet="48_ACTIONS">View / edit record</button></article>`).join('')}</div>` : empty('No management actions recorded. Proposed tests remain separate from assigned actions.');
  }
  function baseline() {
    return panel('Baseline → Decisions → Future outcomes', `<div class="baseline"><article><strong>Dated baseline</strong><p>${esc(data.baseline?.period)} · ${esc(data.baseline?.date?.slice(0, 10))}</p><p>Original metric values, evidence and methods are preserved.</p></article><article><strong>Client decisions</strong><p>${rows('49_DECISIONS').length ? 'Recorded decisions linked to shared actions.' : 'No decisions recorded.'}</p><p>Proposals become decisions only when the organization records them.</p></article><article><strong>${esc(data.outcomeState)}</strong><p>Organizational, asset and community outcomes will be tracked in future updates.</p><p>Observed contribution does not imply causation.</p></article></div>`);
  }
  function solutions() { return panel('We have solutions to test', `<div class="facts">${data.solutions.map(t => `<p>${esc(t)}</p>`).join('')}</div>`, 'Choose a funded test pathway. Approval follows source and methodology review.'); }
  function outlook() {
    return table(['Year', 'Income after costs', 'Loan payments', 'Cash after', 'Coverage', 'Figure state'], data.financialOutlook.map(p => {
      const m = c => p.metrics.find(v => v.code === c);
      return [esc(p.year), metricButton(m('NOI')), metricButton(m('DEBT_SERVICE')), metricButton(m('CASH_AFTER_DEBT')), metricButton(m('DSCR')), esc(m('NOI')?.displayState || 'No data available')];
    }));
  }
  function incomeDebtChart() {
    return `<p class="chart-title">Income after costs and annual loan payments ($M)</p><div class="chart grouped">${data.incomeDebtChart.map(p => `<div class="bar-point"><div class="bar-frame">${[p.noi, p.debt].map((v, i) => `<button type="button" class="bar ${i ? 'debt' : 'noi'}" style="height:${Number(v.height)}px;top:${Number(v.top)}px" data-metric="${esc(v.id || '')}" aria-label="${esc(`${p.year}: ${i ? 'loan payments' : 'income after costs'} ${v.label} million`)}"><span>${esc(v.label)}</span></button>`).join('')}</div><span class="bar-year">FY${esc(p.year.slice(-2))}</span></div>`).join('')}</div><p class="muted">Teal: income after costs · Pale blue: loan payments · —: no data available</p>`;
  }
  function sensitivity() {
    return panel(`${data.context.fullOperationYear} subsidy sensitivity`, table(['Case', 'Income after costs', 'Payments', 'Cash shortfall', 'Coverage', 'Target cushion gap'], [
      ['Source plan', metricButton(metric('NOI', true)), metricButton(metric('DEBT_SERVICE', true)), metricButton(metric('CASH_SHORTFALL', true)), metricButton(metric('DSCR', true)), metricButton(metric('COVERAGE_GAP', true))],
      ['Without placeholder subsidy', metricButton(metric('NOI_EX_PLACEHOLDER', true)), metricButton(metric('DEBT_SERVICE', true)), metricButton(metric('SHORTFALL_EX_PLACEHOLDER', true)), metricButton(metric('DSCR_EX_PLACEHOLDER', true)), metricButton(metric('COVERAGE_GAP_EX_PLACEHOLDER', true))]
    ]), 'Only uniquely flagged project subsidy is removed. Running costs and annual payments are held unchanged. Cash shortfall and the target cushion are separate.');
  }
  function buildingDetails() {
    return panel('Building detail — behind View details', `<p class="muted">Source building rows retain their own grain. Missing or unresolved payments produce no coverage ratio.</p><details id="building-details"><summary>View details · ${count(data.properties.length)} buildings</summary>${table(['Building', 'Revenue', 'Running costs', 'NOI', 'Loan payments', 'Coverage'], data.properties.map(p => {
      const m = c => data.buildingMetrics.find(v => v.scope === p.id && v.code === c);
      return [esc(field(p, 'Property_Name')), metricButton(m('REVENUE')), metricButton(m('OPEX')), metricButton(m('NOI')), metricButton(m('DEBT_SERVICE')), metricButton(m('DSCR'))];
    }))}</details>`);
  }
  function capacity() {
    return panel('Safe borrowing capacity', `<div class="facts"><p><b>PROPERTY SECURITY</b><br>Current appraisals, balances and security review are required.</p><p><b>ABILITY TO REPAY</b><br>${esc(format(metric('DSCR')))} this year; ${esc(format(metric('DSCR', true)))} at full operation and ${esc(format(metric('DSCR_EX_PLACEHOLDER', true)))} without placeholder subsidy.</p><p><b>PERMISSION AND CONTROL</b><br>Reserve use, consents and mission limits require evidence.</p></div><details><summary>View capacity tests</summary>${table(['Test', 'Draft screen'], [['Property security', esc(money(data.capacity.propertySecurity))], ['Ability to repay', esc(money(data.capacity.abilityToRepay))], ['Permission', data.capacity.permission == null ? 'No data available' : data.capacity.permission === 0 ? 'Restricted' : 'Permitted'], ['Liquidity above floor', esc(money(data.capacity.liquidity))]])}</details>`, `Preliminary safe borrowing: ${money(data.capacity.preliminarySafeBorrowing)}. Missing critical tests prevent an amount being stated.`);
  }
  function recordTable(sheet, fields, headers = fields) {
    return table([...headers, 'Record'], rows(sheet).map(r => [...fields.map(f => esc(field(r, f))), `<button type="button" class="detail-link" data-record="${esc(r.id)}" data-sheet="${esc(sheet)}">View details</button>`]));
  }
  function page1() {
    const s = data.portfolioSnapshot;
    return panel('Portfolio snapshot', `<div class="snapshot"><span><strong>${count(s.buildings)} buildings</strong>Included working scope</span><span><strong>${esc(s.existingHomes)} existing homes</strong>Source baseline</span><span><strong>${esc(format(metric('HOMES_TOTAL')))} projected</strong>Selected year; occupancy unconfirmed</span><span><strong>${esc(s.plannedHomes)} planned</strong>Horizon source plan</span></div>`) +
      `<div class="grid two">${panel(`What ${data.context.organization} has — and what it means`, `<div class="facts"><p>Current income after costs: <strong>${esc(format(metric('NOI')))}</strong>; loan payments: <strong>${esc(format(metric('DEBT_SERVICE')))}</strong>.</p><p>Current signed cash after payments: <strong>${esc(format(metric('CASH_AFTER_DEBT')))}</strong>.</p><p>Full-operation coverage: <strong>${esc(format(metric('DSCR', true)))}</strong>; cash shortfall: <strong>${esc(format(metric('CASH_SHORTFALL', true)))}</strong>.</p><p>Without placeholder support: <strong>${esc(format(metric('SHORTFALL_EX_PLACEHOLDER', true)))}</strong> shortfall and <strong>${esc(format(metric('DSCR_EX_PLACEHOLDER', true)))}</strong> coverage.</p><p class="muted">${esc(data.maturityState)}</p></div>`, 'Current results do not settle future subsidy, loan or permission questions.')} ${panel('Cash after loan payments through the plan', chart(data.cashChart, '($M)'), `Peak annual payments: ${format(data.peak)} in ${data.peak?.year || 'an unconfirmed year'}. These are payments, not balances.`)}</div>` +
      `<div class="grid three">${capacity()}${panel('Mission and stewardship', stewardship())}${solutions()}</div>` + baseline() + panel('Decisions and next actions', actions());
  }
  function page2() {
    const s = data.portfolioSnapshot;
    const cities = [...new Set(data.properties.map(p => field(p, 'City')).filter(Boolean))];
    return `<div class="grid three">${panel('Portfolio mix', `<div class="snapshot"><span><strong>${count(s.buildings)}</strong>Buildings</span></div><p>${count(s.operating)} operating / ${count(s.plannedBuildings)} planned</p>`, 'Source classification; current occupancy remains unconfirmed.')}${panel('Where the organization operates', cities.length ? cities.map(c => `<p>${esc(c)}</p>`).join('') : empty('Property addresses and location evidence are not supplied in the canonical register.'))}${panel('Homes by year', chart(data.homesChart, 'Homes · integer count'))}</div>` +
      panel('Property register — source dates retained', table(['Property', 'Source class', 'Units', 'Source location', 'Review'], data.properties.map(p => [esc(field(p, 'Property_Name')), esc(field(p, 'Operating_Status')), field(p, 'Total_Units') ? esc(field(p, 'Total_Units')) : 'Not supplied', esc(field(p, 'Source_Location')), esc(field(p, 'Review_Status'))]))) +
      `<div class="grid equal">${panel('Affordability, mission and stewardship', stewardship())}${panel('Resident and service evidence', recordTable('39_MISSION_SERVICES', ['Record_Type', 'Category', 'Description']), 'Homes in a plan do not establish resident outcomes or actual affordability.')}</div>` + panel('What to provide next', recordTable('54_INFO_REQUESTS', ['Provide_Text', 'We_Can_Tell_You', 'Owner_Role']));
  }
  function revenueMix() {
    return table(['Revenue source', 'Selected year', 'Full operation'], ['REV_RENT_EXISTING', 'REV_RENT_NEW', 'REV_SUBSIDY', 'REV_OTHER'].map((c, i) => [['Existing-home rent', 'New-home rent', 'Subsidy', 'Other income'][i], metricButton(metric(c)), metricButton(metric(c, true))]));
  }
  function page3() {
    return `<div class="grid two">${panel('Income against loan payments, each year', incomeDebtChart() + outlook())}${panel('Where the money comes from', revenueMix(), 'Components describe the selected revenue control; they are not added to that control again.')}</div>` +
      `<div class="grid equal">${solutions()}${panel('Mission and stewardship', stewardship())}</div>` +
      `<div class="grid three">${capacity()}${panel('Loans and near-term maturities', `<p>${esc(data.maturityState)}</p><p><b>Peak annual payments</b><br>${esc(format(data.peak))} · ${esc(data.peak?.year)}</p><details><summary>View loan records</summary>${recordTable('33_DEBT', ['Property_ID', 'Debt_Type', 'Maturity_Date', 'Review_Status'])}</details>`)}${panel('Reserve balance by year', chart(data.reserveChart, 'Closing balances ($M)'), 'Closing is not opening. Reserve permission remains unknown; source discontinuities stay open.')}</div>` +
      panel('Reserve continuity review', table(['Year', 'Staged opening', 'Previous close', 'Difference', 'State'], data.reserveContinuity.map(r => [esc(r.year), esc(money(r.openingStaged)), esc(money(r.previousClose)), esc(money(r.difference)), esc(r.state)])), 'Opening evidence is retained independently; permitted use remains unknown.') + buildingDetails() + sensitivity() + panel('Registered sensitivity scenarios', table(['Scenario', 'Year', 'Income after costs', 'Payments', 'Coverage', 'Cash shortfall', 'Review'], data.sensitivities.map(s => [esc(s.name), esc(s.year), esc(money(s.noi)), esc(money(s.debtService)), esc(ratio(s.dscr)), esc(money(s.cashShortfall)), esc(s.reviewStatus)]))) + optionTester() + panel('Shared finance actions', actions());
  }
  function page4() {
    return `<div class="grid equal">${panel('Evidence by area', table(['Area', 'Coverage state', 'What we have', 'Needed'], data.evidence.map(e => [esc(e.name), esc(e.state), esc(e.summary), esc(e.needs)])), 'Coverage completeness is separate from figure certainty, analyst approval and external release.')}${panel('Questions for the organization', `<p><strong>${count(data.openQuestions.length)} open questions</strong></p><p>Counts reflect the eligible register records shown below.</p><p class="muted">Original checksum: <span class="break-word">${esc(data.context.sha256)}</span></p>`)}</div>` +
      panel('Open questions', table(['Question', 'Why it matters', 'Status', 'Review'], data.openQuestions.map(r => [esc(field(r, 'Description')), esc(field(r, 'Why_It_Matters')), esc(field(r, 'Status')), `<button type="button" class="detail-link" data-record="${esc(r.id)}" data-sheet="51_EXCEPTIONS">Review exception</button>`]))) +
      panel('Provide this, and we can tell you that', recordTable('54_INFO_REQUESTS', ['Provide_Text', 'We_Can_Tell_You', 'Owner_Role'])) +
      panel('Import validation findings', data.importFindings.length ? `<details><summary>View ${count(data.importFindings.length)} field and schema findings</summary><ul>${data.importFindings.map(f => `<li>${esc(f)}</li>`).join('')}</ul></details>` : '<p>No import validation findings.</p>') +
      panel('Mapping review', `<details><summary>Review proposed source mappings</summary>${recordTable('20_FIELD_MAPPING', ['Canonical_Entity', 'Canonical_Field', 'Mapping_Status', 'Reviewer'])}</details>`);
  }
  function page5() {
    return `<div class="grid equal">${panel('Near-term mortgages', `<p>${esc(data.maturityState)}</p><p>Confirm current lender statements and renewal outcomes. A historical date does not establish an overdue obligation.</p>`, '', 'stage')}${panel('Current year', `<p>Income after costs: ${esc(format(metric('NOI')))}</p><p>Loan payments: ${esc(format(metric('DEBT_SERVICE')))}</p><p>Cash after loans: ${esc(format(metric('CASH_AFTER_DEBT')))}</p>`, 'Close the source review and permission gaps.', 'stage')}${panel('Build-up', '<p>Delivery timing, costs and support assumptions remain subject to project evidence.</p><p>Test funding and payment effects before committing the portfolio.</p>', '', 'stage')}${panel('Full operations', `<p>${esc(data.context.fullOperationYear)} plan coverage: ${esc(format(metric('DSCR', true)))}</p><p>Without placeholder subsidy: ${esc(format(metric('DSCR_EX_PLACEHOLDER', true)))}</p><p>Cash need: ${esc(format(metric('SHORTFALL_EX_PLACEHOLDER', true)))}</p>`, 'Test annual support, payment relief and one-year cash separately.', 'stage')}</div>` +
      sensitivity() + panel('Risks compared', recordTable('46_RISKS', ['Risk_Title', 'Domain', 'When_Text', 'What_Would_Settle_It', 'Review_Status'])) +
      `<div class="grid equal">${panel('Mission and stewardship', stewardship())}${solutions()}</div>` +
      panel('Options compared — sized to cash need', table(['Option', 'Test year', 'Available benefit', 'Cash funded', 'Residual', 'Needs first', 'Risk'], data.optionAnalysis.map(o => [esc(o.name), esc(o.year), esc(money(o.amount)), esc(money(o.cashShortfallFunded)), esc(money(o.residualCashShortfall)), esc(o.needsFirst), esc(o.risks)]))) + optionTester() + panel('Decisions for the board', actions());
  }
  function page6() {
    return baseline() + panel('Action register', actions()) +
      panel('Decisions on record', recordTable('49_DECISIONS', ['Decision_Text', 'Decision_Date', 'Decided_By', 'Decision_Status', 'Implementation_Status'])) +
      panel('Dates we are watching', recordTable('13_WATCH_DATES', ['Event_Type', 'Event_Period', 'Event_Text', 'Display_Status'])) +
      panel('Change history', table(['Version', 'Date', 'Reviewer', 'Reason'], data.events.map(e => [esc(e.version), esc(e.date), esc(e.reviewer), esc(e.reason)]))) +
      panel('Outcome observations', '<button type="button" id="outcome-open">Record outcome observation</button>' + (data.outcomes.length ? table(['Measure', 'Period', 'Value', 'Evidence', 'Comparison', 'Contribution'], data.outcomes.map(o => [esc(o.metricDefinition), esc(o.period), o.currentValue == null ? 'Not yet measured' : esc(o.currentValue), esc(o.evidenceId), esc(o.comparisonState), esc(o.contributionState)])) : empty('Not yet measured. Future updates will record evidence-backed organizational, asset and community outcomes.'))) +
      panel('What will change the picture', recordTable('54_INFO_REQUESTS', ['Provide_Text', 'Conclusion_May_Change', 'Pages_Updated']));
  }
  function page7() {
    return panel('Reports — four audience pathways from the same record', `<div class="reports">${data.reports.map(r => `<article><h3>${esc(r.title.toUpperCase())}</h3><strong>${esc(r.state)}</strong><p>Audience: ${esc(r.audience)}</p><p class="muted">Same calculations, source evidence and review states. External authorization: not recorded.</p></article>`).join('')}</div>`, 'A report format does not convert projections into actuals or proposals into approvals.') +
      panel('Who can see what', table(['Pathway', 'Audience', 'Readiness', 'Release'], data.reports.map(r => [esc(r.title), esc(r.audience), esc(r.state), 'Not authorized / not released']))) +
      panel('Authorization rule', `<h2>Nothing leaves ${esc(data.context.organization)} without its approval.</h2><p>Report readiness, narrative review, assessment approval and external release authorization are separate states.</p><p>No report has been generated, sent or released by these pathway cards.</p>`);
  }
  function optionTester() {
    return panel('Test a sized option', `<form id="option-form" class="option-test"><label>Test year<select name="year">${data.financialOutlook.map(y => `<option ${y.year === data.context.fullOperationYear ? 'selected' : ''}>${esc(y.year)}</option>`).join('')}</select></label><label>Case<select name="case"><option>Base</option><option>Without placeholder</option></select></label><label>Option<select name="kind"><option>Bridge</option><option>Recurring income</option><option>Payment relief</option><option>Confirm subsidy</option></select></label><label>Amount ($)<input name="amount" type="number" min="0" max="1000000000" step="0.01" value="0" required></label><label>Added annual payments ($)<input name="additionalDebtService" type="number" min="0" max="1000000000" step="0.01" value="0" required></label><label>Subsidy confirmation<select name="subsidyConfirmed"><option value="false">Not confirmed</option><option value="true">Test confirmed case</option></select></label><button class="primary">Calculate test</button></form><div id="option-result" aria-live="polite"></div>`, 'Draft screening only. No best strategy is selected. Confirm permitted use, timing, mission effects and repayment.');
  }
  function storedInterpretation() {
    const sections = data.sections.filter(s => s.records.length && s.records.some(r => ['45_NARRATIVE', '46_RISKS', '47_OPTIONS'].includes(r.sheet)));
    return sections.map(s => panel(s.title, `<div class="record-list">${s.records.map(r => `<article><span class="review-state">${esc(field(r, 'Review_Status'))}</span><h3>${esc(field(r, 'Heading_Text') || field(r, 'Risk_Title') || field(r, 'Option_Name'))}</h3><p>${esc(field(r, 'Narrative_Text') || field(r, 'What_It_Does') || field(r, 'What_Would_Settle_It'))}</p><button type="button" class="detail-link" data-record="${esc(r.id)}" data-sheet="${esc(r.sheet)}">Review record</button></article>`).join('')}</div>`)).join('');
  }
  function render() {
    $('organization').textContent = data.context.organization.toUpperCase();
    $('title').textContent = field(data.pages.find(r => r.id === page), 'Page_Title') || `Page ${page.slice(1)}: ${titles[Number(page.slice(1)) - 1]}`;
    $('subtitle').textContent = `${data.context.organization} Portfolio (Pilot)`;
    $('context').textContent = `FY${data.context.currentYear} · ${data.context.scenario} · ${data.context.sourceVersion || 'Source version to confirm'} financial controls · ${data.context.methodologyVersion} · Working version ${data.context.version}`;
    $('year-control').hidden = false; $('review').hidden = false;
    $('year').innerHTML = data.financialOutlook.map(y => `<option ${y.year === data.context.currentYear ? 'selected' : ''}>${esc(y.year)}</option>`).join('');
    $('page').innerHTML = tiles() + ({ P1: page1, P2: page2, P3: page3, P4: page4, P5: page5, P6: page6, P7: page7 }[page])() + storedInterpretation();
    nav();
  }
  function nav() {
    $('navigation').innerHTML = labels.map((label, i) => `<a href="#P${i + 1}" ${page === `P${i + 1}` ? 'class="active" aria-current="page"' : ''}>${icon}<span>${esc(data ? field(data.pages.find(r => r.id === `P${i + 1}`), 'Sidebar_Label') || label : label)}</span></a>`).join('');
  }
  async function api(path, options = {}) {
    const response = await fetch(`/api/v4/${path}`, { ...options, headers: { 'X-Admin-Key': key, ...options.headers } });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(response.status === 401 ? 'Authorized pilot access is required.' : body.error || `Request failed (${response.status}).`);
    return body;
  }
  async function load() {
    if (!id || !key) return;
    $('notice').textContent = '';
    try { data = await api(`assessments/${id}/pages/${page}`); render(); }
    catch (e) { $('notice').textContent = e.message; }
  }
  const jsonOptions = body => ({ method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  document.addEventListener('click', async e => {
    if (e.target.closest('#outcome-open')) {
      const decisions = rows('49_DECISIONS').filter(r => field(r, 'Decision_Status') === 'Approved');
      if (!decisions.length) { $('notice').textContent = 'Record an approved client decision before linking an outcome observation.'; return; }
      $('outcome-decision').innerHTML = decisions.map(r => `<option value="${esc(r.id)}">${esc(field(r, 'Decision_Text'))}</option>`).join('');
      $('outcome-action').value = field(decisions[0], 'Linked_Action_ID');
      $('outcome-evidence').innerHTML = rows('10_SOURCE_REGISTER').map(r => `<option value="${esc(r.id)}">${esc(field(r, 'File_Name'))}</option>`).join('');
      $('outcome-status').textContent = ''; $('outcome-dialog').showModal(); return;
    }
    const close = e.target.closest('[data-close]'); if (close) { close.closest('dialog').close(); return; }
    const m = e.target.closest('[data-metric]');
    if (m && m.dataset.metric) {
      $('evidence-body').innerHTML = '<p>Loading evidence…</p>'; $('evidence-dialog').showModal();
      try {
        const evidence = await api(`assessments/${id}/metrics/${encodeURIComponent(m.dataset.metric)}/evidence`);
        const result = evidence.result;
        $('evidence-body').innerHTML = panel(`${result.code} · ${result.year}`, `<h2>${esc(format(result))}</h2><p>${esc(result.displayState)} · ${esc(result.reviewState)} · ${esc(result.valueState)}</p><p>Method: ${esc(result.calcId)} / ${esc(result.calcVersion)} · ${esc(result.methodologyVersion)}</p><p>Unrounded value: ${esc(result.value ?? result.valueState)}</p>`) +
          panel('Source → mapped facts → calculation → result', table(['Fact', 'Source', 'Value', 'Mapping / review', 'Formula and input cells'], evidence.mappedFacts.map(f => [esc(f.id), esc(`${f.sourceId}: ${f.sourceLocation}`), f.value == null ? esc(f.valueState) : esc(f.value), esc(`${f.mappingStatus} / ${f.reviewStatus}`), `<span class="break-word">${esc(f.formula)}<br>${f.inputCells.map(esc).join('<br>')}</span>`]))) +
          panel('Related exceptions', table(['Description', 'Status'], evidence.relatedExceptions.map(r => [esc(field(r, 'Description')), esc(field(r, 'Status'))]))) + `<p class="muted break-word">Source SHA-256: ${esc(evidence.sourceChecksum)}</p>`;
      } catch (err) { $('evidence-body').textContent = err.message; }
      return;
    }
    const r = e.target.closest('[data-record]'); if (r) openRecord(r.dataset.sheet, r.dataset.record);
  });
  window.addEventListener('hashchange', () => { page = /^#P[1-7]$/.test(location.hash) ? location.hash.slice(1) : 'P1'; nav(); load(); });
  $('access').onclick = () => $('access-dialog').showModal();
  $('access-form').onsubmit = async e => { e.preventDefault(); key = $('access-key').value; $('access-key').value = ''; $('access-dialog').close(); await load(); };
  $('upload').onclick = () => { if (!key) { $('notice').textContent = 'Enter authorized pilot access before uploading.'; $('access-dialog').showModal(); return; } $('upload-dialog').showModal(); };
  $('upload-form').onsubmit = async e => {
    e.preventDefault(); const button = e.submitter; button.disabled = true; $('upload-status').textContent = 'Importing source records and calculating…';
    try { const a = await api('assessments', { method: 'POST', body: new FormData(e.target) }); id = a.id; history.replaceState(null, '', `?assessment=${encodeURIComponent(id)}#${page}`); $('upload-dialog').close(); await load(); }
    catch (err) { $('upload-status').textContent = err.message; } finally { button.disabled = false; }
  };
  $('year').onchange = async e => { try { await api(`assessments/${id}/configuration`, jsonOptions({ currentYear: e.target.value, expectedVersion: data.context.version })); await load(); } catch (err) { $('notice').textContent = err.message; } };
  document.addEventListener('submit', async e => {
    if (e.target.id !== 'option-form') return; e.preventDefault(); const form = new FormData(e.target);
    try {
      const r = await api(`assessments/${id}/options/test`, { ...jsonOptions({ year: form.get('year'), case: form.get('case'), kind: form.get('kind'), amount: Number(form.get('amount')), additionalDebtService: Number(form.get('additionalDebtService')), subsidyConfirmed: form.get('subsidyConfirmed') === 'true' }), method: 'POST' });
      $('option-result').innerHTML = table(['Case', 'Cash need', 'Cash funded', 'Residual cash need', 'Coverage after', 'Review'], [[esc(r.case), esc(money(r.cashShortfall)), esc(money(r.cashShortfallFunded)), esc(money(r.residualCashShortfall)), esc(ratio(r.coverageAfter)), esc(r.reviewStatus)]]) + callout(r.note);
    } catch (err) { $('option-result').textContent = err.message; }
  });
  const editable = {
    '48_ACTIONS': ['Action_Text', 'Short_Title', 'Owner_Role', 'Owner_Name', 'Due_Date', 'Timing_Trigger', 'Status', 'Board_Decision', 'Linked_Risk_IDs', 'Linked_Exception_IDs', 'Option_ID', 'Finding_ID'],
    '49_DECISIONS': ['Decision_Text', 'Decision_Date', 'Decided_By', 'Linked_Action_ID', 'Decision_Status', 'Implementation_Status', 'Evidence_Record_IDs'],
    '45_NARRATIVE': ['Page_ID', 'Section_ID', 'Heading_Text', 'Narrative_Text', 'Basis_Record_IDs', 'Review_Status'],
    '46_RISKS': ['Risk_Title', 'Short_Name', 'Domain', 'Size_Metric_ID', 'When_Text', 'What_Would_Settle_It', 'Show_On_Pages', 'Review_Status'],
    '47_OPTIONS': ['Option_Name', 'Option_Group', 'Amount_Metric_ID', 'Needs_First', 'What_It_Does', 'Effect_On_Homes', 'What_It_Risks', 'Show_On_Pages', 'Management_Proposed', 'Review_Status'],
    '20_FIELD_MAPPING': ['Mapping_Status'], '51_EXCEPTIONS': ['Status', 'Resolution', 'Acceptance_Reason']
  };
  function drawRecord() {
    const sheet = $('record-sheet').value; const r = rows(sheet).find(r => r.id === $('record-choice').value);
    $('record-id').value = r?.id || ''; $('record-id').readOnly = Boolean(r);
    $('record-fields').innerHTML = editable[sheet].map(f => `<label>${esc(f.replaceAll('_', ' '))}<textarea data-field="${esc(f)}">${esc(r ? field(r, f) : f === 'Status' ? 'Proposed' : f === 'Review_Status' ? 'Draft' : '')}</textarea></label>`).join('');
  }
  function recordChoices() {
    const sheet = $('record-sheet').value;
    $('record-choice').innerHTML = (['20_FIELD_MAPPING', '51_EXCEPTIONS'].includes(sheet) ? '' : '<option value="">New record</option>') + rows(sheet).map(r => `<option value="${esc(r.id)}">${esc(r.id)}</option>`).join(''); drawRecord();
  }
  function openRecord(sheet = '48_ACTIONS', record = '') {
    if (!editable[sheet]) { $('evidence-body').innerHTML = panel('Source record', table(['Field', 'Recorded value'], Object.entries(rows(sheet).find(r => r.id === record)?.fields || {}).map(([k, v]) => [esc(k.replaceAll('_', ' ')), esc(v)]))); $('evidence-dialog').showModal(); return; }
    $('record-sheet').value = sheet; recordChoices(); if (record) { $('record-choice').value = record; drawRecord(); }
    $('record-status').textContent = ''; $('record-dialog').showModal();
  }
  $('review').onclick = () => openRecord(); $('record-sheet').onchange = recordChoices; $('record-choice').onchange = drawRecord;
  $('record-form').onsubmit = async e => {
    e.preventDefault(); const fields = Object.fromEntries([...$('record-fields').querySelectorAll('[data-field]')].map(i => [i.dataset.field, i.value]));
    try { await api(`assessments/${id}/records`, jsonOptions({ sheet: $('record-sheet').value, id: $('record-id').value, fields, reviewer: $('reviewer').value, reason: $('review-reason').value, expectedVersion: data.context.version })); $('record-dialog').close(); await load(); }
    catch (err) { $('record-status').textContent = err.message; }
  };
  $('outcome-decision').onchange = e => { $('outcome-action').value = field(rows('49_DECISIONS').find(r => r.id === e.target.value), 'Linked_Action_ID'); };
  $('outcome-form').onsubmit = async e => {
    e.preventDefault(); const body = Object.fromEntries(new FormData(e.target));
    body.baselineValue = body.baselineValue === '' ? null : Number(body.baselineValue);
    body.currentValue = body.currentValue === '' ? null : Number(body.currentValue);
    body.expectedVersion = data.context.version;
    try { await api(`assessments/${id}/outcomes`, { ...jsonOptions(body), method: 'POST' }); $('outcome-dialog').close(); await load(); }
    catch (err) { $('outcome-status').textContent = err.message; }
  };
  nav(); if (id) $('access-dialog').showModal();
})();
