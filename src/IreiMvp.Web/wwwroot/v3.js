const labels = ["Portfolio Overview", "Properties & Mission", "Financial Resilience & Debt", "Risk", "Strategies", "Evidence & Exceptions", "Reports"];
const nav = document.querySelector('#v3-nav');
const body = document.querySelector('#body');
let assessment = null;
let page = 2;
let selectedYear = '';
let selectedScenario = '';
let selectedProperty = '';
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const qs = name => document.querySelector(name);
const unique = values => [...new Set(values)].sort();
const show = value => value == null ? 'Missing' : new Intl.NumberFormat('en-CA', {maximumFractionDigits:2}).format(value);
const displayMetric = m => {
  if (m.state !== 'Available') return m.state;
  if (m.metricCode === 'DSCR') return `${show(m.value)}×`;
  if (['NOI_MARGIN','DEBT_BURDEN'].includes(m.metricCode)) return `${show(m.value * 100)}%`;
  try { return new Intl.NumberFormat('en-CA', {style:'currency',currency:assessment.currency || 'CAD',maximumFractionDigits:0}).format(m.value); }
  catch { return `${show(m.value)} ${assessment.currency || ''}`; }
};
const emptyCard = (title, detail) => `<article class="panel panel-pad v3-skeleton"><span class="eyebrow">AWAITING REVIEWED DATA</span><h3>${escapeHtml(title)}</h3><p class="muted">${escapeHtml(detail)}</p><span class="badge amber">Not Assessed</span></article>`;
const pageLayouts = {
  0: {intro:'A whole-portfolio view of stewardship, financial resilience, mission and priorities.', cards:[['Portfolio snapshot','Property and unit counts need confirmed portfolio records.'],['Financial position','Page 3 contains working financial calculations; portfolio conclusions need reconciliation.'],['Mission outcomes','Affordability and service definitions need approval.'],['Priority decisions','Cross-domain decision rules have not been approved.']]},
  1: {intro:'Property-level context and mission commitments across the portfolio.', cards:[['Property inventory','Property names, location, tenure and unit counts need reviewed records.'],['Affordability','Rent and market comparison definitions need approval.'],['Mission and services','Service outcomes and commitments need reviewed evidence.'],['Asset condition','Condition and capital-needs records are not connected yet.']]},
  3: {intro:'Financial, physical and mission risks will be reviewed together here.', cards:[['Financial exposure','Page 3 coverage is a working calculation; risk conclusions need approved rules.'],['Capital and condition','Capital needs and condition evidence are not connected yet.'],['Mission protection','Restrictions, covenants and affordability commitments need review.'],['Data confidence','Open source and mapping exceptions are listed under Evidence & Exceptions.']]},
  4: {intro:'Compare feasible responses after the evidence and decision rules are approved.', cards:[['Maintain and repair','Requires capital plan, reserve and operating scenarios.'],['Refinance or restructure','Requires debt terms, constraints and sensitivity testing.'],['Mission-aligned capital','Requires legal, governance and mission protection review.'],['Implementation plan','Requires assigned actions, owners and review dates.']]},
  6: {intro:'Authorized reports will draw from reviewed evidence and approved conclusions.', cards:[['Portfolio report','Narrative and disclosures await methodology approval.'],['Property schedules','Property facts and scope need confirmation.'],['Financial appendix','Working Page 3 values are not an approved external report.'],['Authorized use','Approval and recipient permissions are not implemented.']]}
};
function renderVisualPage(index) {
  const layout = pageLayouts[index];
  body.innerHTML = `<section class="panel v3-intro"><div><span class="eyebrow">PAGE ${index + 1} · V3 WORKSPACE</span><h2>${escapeHtml(labels[index])}</h2><p class="muted">${escapeHtml(layout.intro)}</p></div><span class="badge amber">Visual structure</span></section><div class="v3-placeholder-grid">${layout.cards.map(([title,detail])=>emptyCard(title,detail)).join('')}</div><section class="panel panel-pad"><h2>What happens next</h2><p class="muted">This page will use reviewed records and approved rules as each domain is built. No values or recommendations are inferred from missing evidence.</p></section>`;
}
function exceptionHelp(message) {
  if (message.startsWith('Debt row ')) return 'The Debt sheet gives a current annual amount without a confirmed fiscal year and scenario. It is excluded from year-specific DSCR; a reviewed schedule is needed.';
  if (message.includes('source-reported debt service is negative')) return 'The source model records debt payments with a negative sign for this property. Check whether this is an outflow convention. The value does not replace the missing reviewed property debt schedule.';
  if (message.includes('confirmed debt schedule has duplicate or negative')) return 'The reviewed annual debt schedule has duplicate or negative service for this period. Debt service and DSCR are withheld pending review.';
  if (message.includes('unresolved property, source, or currency')) return 'This row did not match a registered property, source ID, or assessment currency, so its amount was excluded.';
  if (message.includes('amount is Missing')) return 'A required amount is blank or unreadable. Calculations that depend on it may be missing.';
  if (message.startsWith('Existing-document route:')) return 'This upload needs an analyst to confirm how the original documents map to the template. Its Page 3 figures remain unassessed.';
  return 'Review the source record and mapping before using this result.';
}
function renderExceptions() {
  const exceptions = assessment.exceptions || [];
  const groups = new Map();
  exceptions.forEach(e => { const key = exceptionHelp(e); if (!groups.has(key)) groups.set(key, []); groups.get(key).push(e); });
  body.innerHTML = `<section class="panel v3-intro"><div><span class="eyebrow">PAGE 6 · SOURCE REVIEW</span><h2>Evidence & Exceptions</h2><p class="muted">${exceptions.length} review items from this upload. Re-upload a corrected workbook to create a new working assessment.</p></div><span class="badge ${exceptions.length ? 'amber' : 'blue'}">${exceptions.length ? 'Review needed' : 'No import exceptions'}</span></section>${exceptions.length ? [...groups].map(([help,items])=>`<section class="panel panel-pad v3-exception-group"><h2>${items.length} item${items.length===1?'':'s'}</h2><p>${escapeHtml(help)}</p><details><summary>Show affected records</summary><ul>${items.map(e=>`<li>${escapeHtml(e)}</li>`).join('')}</ul></details></section>`).join('') : '<section class="panel panel-pad"><p>No importer exceptions. Source and methodology review is still required.</p></section>'}<p class="v3-footnote">Source SHA-256: ${escapeHtml(assessment.sha256)}</p>`;
}
function render() {
  nav.innerHTML = labels.map((name,i) => `<button type="button" data-page="${i}" class="${page === i ? 'active':''}" ${page === i ? 'aria-current="page"':''}><span>${i+1}</span>${escapeHtml(name)}</button>`).join('');
  qs('#title').textContent = labels[page];
  qs('#context').textContent = assessment ? `${assessment.organization} · ${assessment.status} · ${assessment.methodologyVersion}` : 'No assessment loaded';
  qs('#assessment-status').textContent = assessment?.status || 'Not loaded';
  qs('#methodology').textContent = assessment?.methodologyVersion || 'V3.1 pilot';
  if (!assessment) { qs('#filters').hidden = true; body.innerHTML = '<div class="welcome"><div class="panel welcome-card"><div class="welcome-mark">IREI</div><h2>Stage 1 V3.1</h2><p>Upload the current customer or internal template to start a working financial assessment.</p><button class="button primary" type="button" id="welcome-upload">Upload / Update</button></div></div>'; qs('#scope').textContent='Financial pilot · Working assessment'; return; }
  const metrics = assessment.metrics || [];
  const years = unique(metrics.map(m => m.fiscalYear));
  if (!years.includes(selectedYear)) selectedYear = years.includes('2025-2026') ? '2025-2026' : years[0] || '';
  const scenarios = unique(metrics.filter(m => m.fiscalYear === selectedYear).map(m => m.scenario));
  if (!scenarios.includes(selectedScenario)) selectedScenario = scenarios[0] || '';
  const properties = unique(metrics.filter(m => m.fiscalYear === selectedYear && m.scenario === selectedScenario).map(m => m.propertyId));
  if (!properties.includes(selectedProperty)) selectedProperty = properties.find(p => /portfolio/i.test(p)) || properties[0] || '';
  qs('#filters').hidden = page !== 2;
  for (const [id, options, current] of [['year', years, selectedYear],['scenario', scenarios, selectedScenario],['property', properties, selectedProperty]]) {
    qs('#'+id).innerHTML = options.map(v => `<option ${v === current ? 'selected':''}>${escapeHtml(v)}</option>`).join('');
  }
  qs('#scope').textContent = `${assessment.organization} · ${assessment.currency} · Working assessment`;
  if (page === 5) { renderExceptions(); return; }
  if (page !== 2) { renderVisualPage(page); return; }
  const rows = metrics.filter(m => m.fiscalYear === selectedYear && m.scenario === selectedScenario && m.propertyId === selectedProperty);
  const names = {OPEX:'Operating expenses',NOI:'NOI / cash before debt',DSCR:'Debt service coverage',CASH_AFTER_DEBT:'Cash after debt',CASH_AFTER_RESERVE:'Cash after planned reserve',NOI_MARGIN:'NOI margin',DEBT_BURDEN:'Debt burden',REQUIRED_NOI:'Required NOI at 1.20×',COVERAGE_GAP:'Coverage gap (+) or cushion (−)'};
  const byCode = Object.fromEntries(rows.map(m => [m.metricCode,m]));
  const card = code => { const m=byCode[code]; return m ? `<article class="kpi v3-card"><span class="label">${escapeHtml(names[code] || code)}</span><strong class="${m.state === 'Available' && m.value < 0 ? 'negative' : ''}">${escapeHtml(displayMetric(m))}</strong><small>${escapeHtml(m.state === 'Available' ? 'Working calculation' : m.state)}</small><details><summary>Calculation & evidence</summary><p>${escapeHtml(m.calcId)} v${escapeHtml(m.calcVersion)}</p><p>Records: ${escapeHtml(m.inputRecordIds.join(', ') || 'No approved input')}</p>${m.sources.map(s=>`<p>${escapeHtml(s)}</p>`).join('') || '<p>Source evidence incomplete.</p>'}</details></article>` : ''};
  const issues = (assessment.issues || []).filter(i=>i.propertyId === selectedProperty && i.fiscalYear === selectedYear && i.scenario === selectedScenario);
  body.innerHTML = `${assessment.status === 'Mapping Required' ? '<div class="v3-alert">Source mapping requires analyst confirmation. Financial results are not assessed.</div>' : ''}${assessment.exceptions.length ? `<div class="v3-alert">${assessment.exceptions.length} review exceptions. See Evidence & Exceptions.</div>`:''}<section class="panel v3-intro"><div><span class="eyebrow">PAGE 3 · FINANCIAL RESILIENCE</span><h2>Operating performance & debt</h2><p class="muted">${escapeHtml(selectedProperty)} · ${escapeHtml(selectedYear)} · ${escapeHtml(selectedScenario)} · ${escapeHtml(assessment.currency)}</p></div><span class="badge amber">Working assessment</span></section>${rows.length ? `<div class="v3-primary">${['REVENUE','OPEX','NOI','DEBT_SERVICE','DSCR'].map(card).join('')}</div><div class="grid-financial v3-detail"><section class="panel panel-pad"><h2>Cash flow & capacity</h2><div class="v3-secondary">${['CASH_AFTER_DEBT','CASH_AFTER_RESERVE','REQUIRED_NOI','COVERAGE_GAP'].map(card).join('')}</div></section><section class="panel panel-pad"><h2>Operating ratios & review</h2><div class="v3-secondary">${['NOI_MARGIN','DEBT_BURDEN'].map(card).join('')}</div><div class="v3-rule"><span class="badge ${issues.some(i=>i.triggered)?'red':'blue'}">${issues.length ? escapeHtml(issues[0].severity) : 'Not Assessed'}</span><p>${issues.length ? escapeHtml(issues.map(i=>`${i.triggered?'Coverage trigger met':'Coverage trigger not met'} · ${i.ruleId} v${i.ruleVersion}`).join(' · ')) : 'Coverage cannot be determined from the reviewed inputs.'}</p></div></section></div>` : '<section class="panel panel-pad v3-empty"><span class="badge amber">Not Assessed</span><h2>Financial controls need review</h2><p class="muted">Confirm source mapping and financial records before displaying Page 3 calculations.</p></section>'}<p class="v3-footnote">Pilot rule: 1.20× coverage target; below 1.00× is high severity. Values are working calculations, pending methodology and source approval.</p>`;
}
const uploadDialog = qs('#upload-dialog');
qs('#open-upload').addEventListener('click',()=>uploadDialog.showModal());
qs('#close-upload').addEventListener('click',()=>uploadDialog.close());
qs('#cancel-upload').addEventListener('click',()=>uploadDialog.close());
body.addEventListener('click',e=>{if(e.target.closest('#welcome-upload'))uploadDialog.showModal();});
nav.addEventListener('click', e => { const button=e.target.closest('[data-page]'); if(button){ page=Number(button.dataset.page);render(); } });
for (const id of ['year','scenario','property']) qs('#'+id).addEventListener('change',e=>{ if(id==='year')selectedYear=e.target.value;if(id==='scenario')selectedScenario=e.target.value;if(id==='property')selectedProperty=e.target.value;render(); });
qs('#upload').addEventListener('submit',async e=>{e.preventDefault();qs('#upload-status').textContent='Importing…';try{const response=await fetch('/api/v3/assessments',{method:'POST',body:new FormData(e.target)});const data=await response.json();if(!response.ok)throw Error(data.error || 'Import failed');assessment=data;history.replaceState({},'',`?assessment=${encodeURIComponent(data.id)}`);qs('#upload-status').textContent='Working assessment saved.';uploadDialog.close();page=2;render();}catch(error){qs('#upload-status').textContent=error.message;}});
(async()=>{const id=new URLSearchParams(location.search).get('assessment');if(id){try{const response=await fetch(`/api/v3/assessments/${encodeURIComponent(id)}`);if(response.ok)assessment=await response.json();}catch{}}render();})();
