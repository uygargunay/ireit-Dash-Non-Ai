(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const codes = ['REVENUE','OPEX','OPEX_SOCIETY','OPEX_BUILDINGS','DEBT_PRINCIPAL','DEBT_INTEREST','DEBT_SERVICE','RESERVE_CLOSING','HOMES_TOTAL','REV_SUBSIDY','REV_RENT_EXISTING','REV_RENT_NEW','REV_OTHER'];
  let batch, candidate;
  async function api(path, options = {}) {
    const response = await fetch('/api/v4/intake/' + path, { ...options, headers: { 'X-Admin-Key': $('key').value, ...options.headers } });
    const data = await response.json();
    if (!response.ok) throw Error(response.status === 401 ? 'Enter the configured pilot access key.' : response.status === 404 ? 'Intake is disabled. Set Irei__IntakeEnabled=true on the server.' : data.error || 'Request failed.');
    return data;
  }
  const post = data => ({method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});
  async function action(work) { $('notice').textContent = ''; try { await work(); } catch (e) { $('notice').textContent = e.message; } }
  function render() {
    $('workspace').hidden = false; $('title').textContent = batch.organizationName + ' · ' + batch.assessmentId;
    $('batch-id').value = batch.id; history.replaceState(null, '', '?batch=' + encodeURIComponent(batch.id));
    $('summary').textContent = `Batch ${batch.id} · version ${batch.version} · ${batch.currency} · fiscal start month ${batch.fiscalStartMonth}. Save this batch ID to resume.`;
    $('sources').innerHTML = batch.sources.map(s => `<details><summary>${esc(s.name)} · ${esc(s.status)} · ${esc(s.processingMilliseconds)} ms</summary><p>${esc(s.error)}</p><small>Parser ${esc(s.parserVersion)} · SHA-256 ${esc(s.sha256)}</small><div class="scroll"><table><tr><th>Worksheet</th><th>Rows</th><th>Columns</th><th>Present cells</th><th>Formulas</th><th>Missing caches</th></tr>${s.sheets.map(t => `<tr><td>${esc(t.name)}</td><td>${t.rows}</td><td>${t.columns}</td><td>${t.nonemptyCells}</td><td>${t.formulaCells}</td><td>${t.missingCaches}</td></tr>`).join('')}</table></div></details>`).join('');
    drawCandidates();
    $('assessments').innerHTML = batch.assessmentRuntimeIds.map(id => `<p><a href="v4.html?assessment=${encodeURIComponent(id)}">Open calculated V4 assessment ${esc(id)}</a></p>`).join('');
    $('effort-list').innerHTML = batch.effort.map(e => `<p>${esc(e.role)} · ${esc(e.task)} · ${esc(e.minutes)} minutes · ${esc(e.machineCost)} ${esc(e.costCurrency)} · ${esc(e.note)}</p>`).join('');
  }
  function drawCandidates() {
    const query = $('filter').value.toLowerCase();
    const list = batch.candidates.filter(c => (!$('state').value || c.state === $('state').value) && [c.sheet,c.suggestedProperty,c.label,c.rawYear,c.rawText].join(' ').toLowerCase().includes(query));
    $('candidates').innerHTML = `<p>${list.length} matching proposals. Showing first 100; narrow the filter for more.</p><div class="scroll"><table><tr><th>Source / location</th><th>Label / property hint</th><th>Period / code hint</th><th>Raw evidence</th><th>State</th><th>Review</th></tr>${list.slice(0,100).map(c => `<tr><td>${esc(c.sheet)}!${esc(c.cell)}</td><td>${esc(c.label)}<br><small>${esc(c.suggestedProperty)}</small></td><td>${esc(c.rawYear)}<br>${esc(c.suggestedCode)}</td><td>${esc(c.rawText)}<br><small>${esc(c.valueState)}</small></td><td>${esc(c.state)}</td><td><button data-review="${esc(c.id)}">Evidence / review</button></td></tr>`).join('')}</table></div>`;
  }
  $('create').onsubmit = e => {e.preventDefault(); action(async () => {const data=Object.fromEntries(new FormData(e.target));data.fiscalStartMonth=Number(data.fiscalStartMonth);batch=await api('batches',post(data));render();});};
  $('reopen').onsubmit = e => {e.preventDefault();action(async () => {batch=await api('batches/'+encodeURIComponent($('batch-id').value));render();});};
  $('upload').onsubmit = e => {e.preventDefault();action(async () => {const button=e.submitter;button.disabled=true;try{batch=await api(`batches/${batch.id}/files`,{method:'POST',body:new FormData(e.target)});render();}finally{button.disabled=false;}});};
  $('filter').oninput = drawCandidates; $('state').onchange = drawCandidates;
  $('candidates').onclick = e => {const button=e.target.closest('[data-review]');if(!button)return;action(async () => {
    candidate=batch.candidates.find(c=>c.id===button.dataset.review);$('review-form').reset();$('review-error').textContent='';
    $('code').innerHTML=codes.map(c=>`<option>${c}</option>`).join('');
    const form=$('review-form');form.elements.code.value=candidate.suggestedCode || codes[0];form.elements.year.value=candidate.rawYear;
    form.elements.scope.required=false;form.elements.year.required=false;
    form.elements.value.value=candidate.rawText;form.elements.scope.value=candidate.scope || '';form.elements.reviewer.value='';
    const letters=candidate.cell.match(/^[A-Z]+/)[0];let col=0;for(const letter of letters)col=col*26+letter.charCodeAt(0)-64;
    const row=Number(candidate.cell.match(/\d+$/)[0]);
    const preview=await api(`batches/${batch.id}/sources/${candidate.sourceId}/preview?sheet=${encodeURIComponent(candidate.sheet)}&row=${Math.max(1,row-3)}&column=${Math.max(1,col-3)}`);
    $('evidence').innerHTML=`<p>Candidate ID: <b>${esc(candidate.id)}</b></p><p>${esc(candidate.sheet)}!${esc(candidate.cell)} · ${esc(candidate.label)} · ${esc(candidate.valueState)}</p><pre>${esc(candidate.formula || candidate.rawText)}</pre><details><summary>Surrounding source cells</summary><pre>${esc(JSON.stringify(preview,null,2))}</pre></details>`;
    $('review').showModal();
  });};
  $('review-form').onsubmit = async e => {e.preventDefault();const data=Object.fromEntries(new FormData(e.target));data.expectedVersion=batch.version;data.value=data.value || null;try{batch=await api(`batches/${batch.id}/candidates/${candidate.id}/review`,post(data));$('review').close();render();}catch(err){$('review-error').textContent=err.message;}};
  $('close').onclick=()=>$('review').close();
  $('commit').onclick=()=>action(async()=>{batch=await api(`batches/${batch.id}/commit`,post({expectedVersion:batch.version}));render();});
  async function download(kind,name){const response=await fetch(`/api/v4/intake/batches/${batch.id}/${kind}`,{headers:{'X-Admin-Key':$('key').value}});if(!response.ok)throw Error((await response.json()).error || 'Download failed');const url=URL.createObjectURL(await response.blob());const a=document.createElement('a');a.href=url;a.download=name;a.click();URL.revokeObjectURL(url);}
  $('download').onclick=()=>action(()=>download('export','reviewed-intake.xlsx'));$('manifest').onclick=()=>action(()=>download('manifest','intake-manifest.json'));
  $('effort').onsubmit=e=>{e.preventDefault();action(async()=>{batch=await api(`batches/${batch.id}/effort`,post(Object.fromEntries(new FormData(e.target))));render();});};
  $('batch-id').value=new URLSearchParams(location.search).get('batch') || '';
})();
