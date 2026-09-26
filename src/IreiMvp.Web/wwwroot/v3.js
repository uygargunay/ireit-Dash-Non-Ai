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
  if (page === 5) { body.innerHTML = `<section class="panel panel-pad"><h2>Evidence & Exceptions</h2><p class="muted">Source SHA-256: ${escapeHtml(assessment.sha256)}</p>${assessment.exceptions.length ? assessment.exceptions.map(e=>`<p class="v3-alert">${escapeHtml(e)}</p>`).join(''):'<p>No importer exceptions. Source and methodology review is still required.</p>'}</section>`; return; }
  if (page !== 2) { body.innerHTML = `<section class="panel panel-pad v3-empty"><span class="badge amber">Not Assessed</span><h2>${escapeHtml(labels[page])}</h2><p class="muted">Methodology for this page has not been approved.</p></section>`; return; }
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
