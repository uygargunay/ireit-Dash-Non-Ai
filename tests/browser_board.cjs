const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.EVIDENCE_CHROME||'/usr/bin/chromium',headless:true,args:['--no-sandbox','--disable-dev-shm-usage','--single-process','--no-zygote','--disable-gpu']});
 try{
  const context=await browser.newContext({viewport:{width:1320,height:960}});const page=await context.newPage();const errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const base=process.env.BOARD_TEST_URL;await page.goto(base+'/');
  await page.locator('#open-upload').click();await page.locator('#organization-name').fill('NGO B');
  const files=[path.resolve('src/IreiMvp.Web/Data/Examples/IREI_Stage1_V4.2_NGOB_Real_Data_Test.xlsx')];if(process.env.BOARD_RAW_WORKBOOK)files.push(process.env.BOARD_RAW_WORKBOOK);
  await page.locator('#file-input').setInputFiles(files);await page.locator('#submit-upload').click();
  await page.locator('#staff-sign-in').waitFor({state:'visible'});assert(await page.locator('#submit-upload').isEnabled());assert.equal(await page.locator('#file-input').evaluate(el=>el.files.length),files.length);
  const login=await context.newPage();await login.goto(base+'/evidence.html');await login.locator('#login input[name=email]').fill('admin@example.test');await login.locator('#login input[name=password]').fill(process.env.BOARD_TEST_PASSWORD);await login.locator('#login button').click();await login.locator('#workspace').waitFor({state:'visible'});
  let staleChecks=0,forceStale=false;const uploadStatuses=[];
  page.on('response',r=>{if(r.url().endsWith('/api/submissions/board/assessments')&&r.request().method()==='POST')uploadStatuses.push(r.status());});
  await page.route('**/api/evidence/session',async route=>{const response=await route.fetch();const body=await response.json();if(body.authenticated&&(staleChecks===0||forceStale)){body.csrf='synthetic-stale-token';staleChecks++;}await route.fulfill({response,json:body});});
  const uploaded=page.waitForResponse(r=>r.url().endsWith('/api/submissions/board/assessments')&&r.request().method()==='POST'&&r.status()===200);await page.locator('#submit-upload').click();assert.equal((await uploaded).status(),200);
  assert.deepEqual(uploadStatuses,[403,200],'One explicit stale-token rejection refreshes and retries the same selected files');await page.locator('#upload-dialog').waitFor({state:'hidden',timeout:60000});await page.locator('.tiles').waitFor();
  assert.match(await page.locator('#content').innerText(),/715/);assert.match(await page.locator('#content').innerText(),/1\.58x/);assert.match(await page.locator('#content').innerText(),/\$2\.31M/);
  assert.equal(await page.locator('#page-nav a').count(),7);assert.equal(await page.locator('body').getAttribute('aria-busy'),'false');
  await page.locator('#present').click();
  const dir=process.env.BOARD_PREVIEW_DIR||'/tmp/board-previews';fs.mkdirSync(dir,{recursive:true});
  for(let i=1;i<=7;i++){
   await page.locator(`#page-nav a[href="#P${i}"]`).click();await page.waitForFunction(p=>document.body.classList.contains('page-'+p),'P'+i);
   assert.equal(await page.locator('#content > .tiles > *').count(),7);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+1),'No horizontal clipping on page '+i);
   const overflow=await page.locator('.panel,.tile').evaluateAll(els=>els.filter(el=>el.scrollWidth>el.clientWidth+2).map(el=>el.className+':'+el.textContent.slice(0,70)));assert.deepEqual(overflow,[],'No card content clipping on page '+i);
   if(i===4){assert.match(await page.locator('#content').innerText(),/Placeholder subsidy/);assert.match(await page.locator('#content').innerText(),/Loan terms|loan terms|Mortgage|debt/i);}
   await page.screenshot({path:dir+`/IREI_V2_Page_${String(i).padStart(2,'0')}.png`,fullPage:true});
  }
  await page.locator('#page-nav a[href="#P3"]').click();await page.locator('.tiles [data-metric]').first().click();await page.locator('#detail-dialog').waitFor({state:'visible'});assert.match(await page.locator('#detail-body').innerText(),/CASH FLOW/);await page.locator('#detail-dialog [data-close]').click();
  await page.locator('#page-nav a[href="#P2"]').click();await page.locator('[data-property]').first().click();await page.locator('#detail-dialog').waitFor({state:'visible'});assert.match(await page.locator('#detail-body').innerText(),/Property Name/);await page.locator('#detail-dialog [data-close]').click();
  await page.locator('#page-nav a[href="#P6"]').click();await page.locator('[data-proposal]').first().click();await page.locator('#action-dialog').waitFor({state:'visible'});await page.locator('#action-form input[name=owner]').fill('Browser CFO');await page.locator('#action-form button.primary').click();await page.locator('#action-dialog').waitFor({state:'hidden'});assert.match(await page.locator('#content').innerText(),/Browser CFO/);
  await page.reload();await page.locator('.tiles').waitFor();assert.match(await page.locator('#content').innerText(),/Browser CFO/);
  await page.locator('#page-nav a[href="#P7"]').click();await page.locator('#option-form input[name=amount]').fill('3376096.98');await page.locator('#option-form button').click();await page.waitForFunction(()=>document.getElementById('option-result').textContent.includes('0.75x'));assert.match(await page.locator('#option-result').innerText(),/unchanged/);
  await page.evaluate(()=>{window.print=()=>{};});await page.locator('#print-all').click();assert.equal(await page.locator('#print-report .report-page').count(),7);assert.match(await page.locator('#print-report').innerText(),/16 open questions/);
  await page.emulateMedia({media:'print'});await page.pdf({path:dir+'/IREI_V2_Internal_Draft.pdf',format:'A4',landscape:true,printBackground:true});await page.emulateMedia({media:'screen'});await page.evaluate(()=>window.dispatchEvent(new Event('afterprint')));
  await page.setViewportSize({width:390,height:844});for(let i=1;i<=7;i++){await page.evaluate(i=>{location.hash='P'+i;},i);await page.waitForFunction(i=>document.body.classList.contains('page-P'+i),i);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Mobile page '+i+' fits viewport');}
  await page.screenshot({path:dir+'/IREI_V2_Mobile.png',fullPage:true});
  await page.setViewportSize({width:1320,height:960});await page.locator('#open-upload').click();await page.locator('#file-input').setInputFiles(files);forceStale=true;
  const before=uploadStatuses.length;await page.locator('#submit-upload').click();await page.waitForFunction(()=>!document.getElementById('submit-upload').disabled);
  assert.equal(uploadStatuses.length-before,2,'Persistent CSRF rejection retries once, without looping');assert.equal(uploadStatuses.at(-1),403);
  assert.equal(await page.locator('#file-input').evaluate(el=>el.files.length),files.length);assert.match(await page.locator('#upload-error').innerText(),/Sign out.*sign back in/);assert.equal(await page.locator('body').getAttribute('aria-busy'),'false');
  assert(await page.locator('#staff-sign-in').isVisible());
  await page.locator('#cancel-upload').click();forceStale=false;
  await page.route('**/api/submissions/board/assessments',route=>route.fulfill({status:403,contentType:'application/json',body:JSON.stringify({error:'Synthetic permission denial',code:'forbidden'})}));
  await page.locator('#open-upload').click();const deniedBefore=uploadStatuses.length;await page.locator('#submit-upload').click();await page.waitForFunction(()=>!document.getElementById('submit-upload').disabled);
  assert.equal(uploadStatuses.length-deniedBefore,1,'Permission failures are not retried');assert.match(await page.locator('#upload-error').innerText(),/permission denial/);
  assert.equal(errors.length,0,errors.join('\n'));
  console.log('BOARD BROWSER: real upload, retained files/sign-in, seven reference pages, source and building drill-through, persistent actions, option test, seven-page print output and mobile layouts passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
