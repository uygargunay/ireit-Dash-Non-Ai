const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const crypto = require('node:crypto');
const fs = require('node:fs');
const assert = require('node:assert/strict');
function otp(secret, offset = 0) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '', out = [];
  for (const ch of secret.replace(/=+$/, '')) bits += alphabet.indexOf(ch).toString(2).padStart(5,'0');
  for(let i=0;i+8<=bits.length;i+=8)out.push(parseInt(bits.slice(i,i+8),2));
  const counter=Buffer.alloc(8);counter.writeBigUInt64BE(BigInt(Math.floor(Date.now()/30000)+offset));
  const mac=crypto.createHmac('sha1',Buffer.from(out)).update(counter).digest();const pos=mac.at(-1)&15;
  return String((mac.readUInt32BE(pos)&0x7fffffff)%1000000).padStart(6,'0');
}
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.EVIDENCE_CHROME,headless:true,args:['--no-sandbox','--disable-gpu','--disable-dev-shm-usage','--single-process','--no-zygote']});
 const errors=[];const dir=process.env.EVIDENCE_BROWSER_OUTPUT || '/tmp/evidence-browser-output';fs.mkdirSync(dir,{recursive:true});
 async function login(page,email,admin){
  page.on('pageerror',e=>errors.push(e.message));await page.goto(process.env.EVIDENCE_TEST_URL+'/evidence.html');
  await page.locator('#login input[name=email]').fill(email);await page.locator('#login input[name=password]').fill(process.env.EVIDENCE_TEST_PASSWORD);
  const requiresMfa=process.env.EVIDENCE_TEST_REQUIRE_MFA==='true';
  await page.waitForFunction(expected=>document.querySelector('[data-admin-mfa]').hidden===!expected,requiresMfa);
  assert.equal(await page.locator('#login input[name=totp]').isVisible(),requiresMfa);
  assert.equal(await page.locator('#accept input[name=totpSecret]').isVisible(),false);
  if(admin&&requiresMfa)await page.locator('#login input[name=totp]').fill(otp(process.env.EVIDENCE_TEST_TOTP_SECRET,1));
  await page.locator('#login button').click();await page.locator('#workspace').waitFor({state:'visible'});
 }
 const client=await browser.newPage({viewport:{width:1280,height:900}});await login(client,'approver@example.test',false);
 await client.locator('#categories details').first().waitFor();assert.equal(await client.locator('#categories details').count(),10);
 assert.equal(await client.locator('[data-tab=data]').isVisible(),false);assert.equal(await client.locator('[data-tab=administration]').isVisible(),false);
 await client.screenshot({path:dir+'/client-desktop.png',fullPage:true});
 const category=client.locator('#categories details').filter({hasText:'02 · Financial statements and budgets'});await category.locator('summary').click();await category.locator('[data-upload]').click();
 await client.locator('#upload-files').setInputFiles({name:'browser-source.csv',mimeType:'text/csv',buffer:Buffer.from('metric_code,value,unit,currency,period,basis\nREVENUE,300,Currency,CAD,FY2028,Management annual\n')});
 await client.locator('#upload-form input[name=confirmedNoPersonalData]').check();await client.locator('#start-upload').click();
 await client.waitForFunction(()=>document.getElementById('upload-error').textContent.includes('Receipts saved'));
 await client.locator('#upload-dialog [data-close]').click();await client.reload();await client.locator('#workspace').waitFor({state:'visible'});
 await client.waitForFunction(()=>document.getElementById('document-table').textContent.includes('browser-source.csv'));
 assert(await client.locator('#document-table').innerText().then(t=>t.includes('browser-source.csv')));
 await client.locator('[data-tab=analysis]').click();assert.equal(await client.getByText('INTERNAL PRIVATE ASSUMPTIONS',{exact:true}).count(),0);
 await client.setViewportSize({width:390,height:844});await client.locator('[data-tab=documents]').click();
 assert(await client.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+2));await client.screenshot({path:dir+'/client-mobile.png',fullPage:true});
 const admin=client;await admin.locator('#logout').click();await admin.locator('#access').waitFor({state:'visible'});await admin.setViewportSize({width:1440,height:1000});await login(admin,'admin@example.test',true);
 await admin.locator('[data-tab=data]').click();await admin.locator('#fact-table table').waitFor();
 await admin.waitForFunction(()=>document.getElementById('fact-table').textContent.includes('CASH_BALANCE'));assert(await admin.locator('#fact-table').innerText().then(t=>t.includes('CASH_BALANCE')));await admin.screenshot({path:dir+'/analyst-data.png',fullPage:true});
 await admin.locator('[data-tab=documents]').click();await admin.locator('#upload-main').click();
 await admin.locator('#upload-files').setInputFiles(process.env.EVIDENCE_PDF_TEST_FILE);
 await admin.locator('#upload-form input[name=confirmedNoPersonalData]').check();
 await admin.locator('#start-upload').click();await admin.waitForFunction(()=>document.getElementById('upload-error').textContent.includes('Receipts saved'));
 await admin.locator('#upload-dialog [data-close]').click();
 const pdfrow=admin.locator('#document-table tr').filter({hasText:'browser-statements.pdf'});await pdfrow.getByText('View data preview',{exact:true}).click();
 await admin.locator('#data').waitFor({state:'visible'});assert.equal(await admin.locator('#fact-table tbody tr').count(),2);
 assert(await admin.locator('#data-preview').innerText().then(t=>t.includes('DRAFT')&&t.includes('Operating revenue')));
 await admin.getByText('Review a selected group together',{exact:true}).click();await admin.locator('#select-facts').click();assert.equal(await admin.locator('.fact-check:checked').count(),2);
 await admin.locator('#context-facts').click();await admin.locator('#context-dialog').waitFor({state:'visible'});
 await admin.locator('#context-form input[name=period]').fill('FY2026');await admin.locator('#context-form textarea[name=reason]').fill('Synthetic browser source and context checked');await admin.locator('#context-form input[name=confirmed]').check();await admin.locator('#context-form button.primary').click();await admin.locator('#context-dialog').waitFor({state:'hidden'});
 await admin.locator('#select-facts').click();await admin.locator('#review-facts').click();await admin.locator('#batch-review-dialog').waitFor({state:'visible'});await admin.locator('#batch-review-form select[name=status]').selectOption('Qualified');await admin.locator('#batch-review-form textarea[name=reason]').fill('Synthetic group remains qualified for further review');await admin.locator('#batch-review-form input[name=confirmed]').check();await admin.locator('#batch-review-form button.primary').click();await admin.locator('#batch-review-dialog').waitFor({state:'hidden',timeout:10000}).catch(async e=>{throw Error(e.message+'; review error: '+await admin.locator('#batch-review-error').innerText());});
 assert.equal(await admin.locator('#fact-table tbody tr').count(),2);assert(await admin.locator('#fact-table').innerText().then(t=>t.includes('Qualified')&&t.includes('FY2026')));
 await admin.screenshot({path:dir+'/pdf-data-preview.png',fullPage:true});
 await admin.locator('[data-tab=documents]').click();await pdfrow.getByText('Read extracted text',{exact:true}).click();
 await admin.locator('#pdf-text-dialog').waitFor({state:'visible'});assert(await admin.locator('#pdf-text-content').innerText().then(t=>t.includes('Total revenue')&&t.includes('Page 2')));
 await admin.locator('#pdf-text-dialog [data-close]').click();

 const session=await admin.request.get(process.env.EVIDENCE_TEST_URL+'/api/evidence/session').then(r=>r.json());
 const createdTenant=await admin.request.post(process.env.EVIDENCE_TEST_URL+'/api/evidence/tenants',{headers:{'X-CSRF-Token':session.csrf},data:{name:'Browser assessment organization',pilotScope:'Synthetic browser test',reportingCutoff:'2026-10-31',scopeIds:['organization']}}).then(r=>r.json());
 await admin.reload();await admin.locator('#organization').selectOption(createdTenant.id);await admin.waitForFunction(()=>document.getElementById('org-title').textContent==='Browser assessment organization');
 await admin.locator('[data-tab=documents]').click();await admin.locator('#upload-main').click();
 await admin.locator('#upload-files').setInputFiles({name:'browser-portfolio.csv',mimeType:'text/csv',buffer:Buffer.from('Metric,2026-2027\nTOTAL REVENUE,1000\nTOTAL OPERATING EXPENSES,600\nANNUAL DEBT SERVICE,200\nCLOSING RESERVE,50\nTOTAL UNITS,10\n')});
 await admin.locator('#upload-form input[name=confirmedNoPersonalData]').check();await admin.locator('#start-upload').click();await admin.waitForFunction(()=>document.getElementById('upload-error').textContent.includes('Receipts saved'));await admin.locator('#upload-dialog [data-close]').click();
 await admin.locator('#document-table tr').filter({hasText:'browser-portfolio.csv'}).getByText('View data preview',{exact:true}).click();

 // Start from the user's exact blocked state: extracted records, zero verified.
 await admin.locator('#go-v4').click();await admin.locator('#assessment-dialog').waitFor({state:'visible'});
 assert(await admin.locator('#assessment-readiness').innerText().then(t=>t.includes('0 already verified')));
 assert(await admin.locator('#assessment-source option:checked').innerText().then(t=>t.includes('browser-portfolio.csv')));
 await admin.locator('#assessment-property').selectOption('__org');await admin.locator('#assessment-form select[name=basis]').selectOption('Projection');await admin.locator('#assessment-form input[name=currentYear]').fill('2026-2027');await admin.locator('#assessment-form textarea[name=reason]').fill('One synthetic financial summary checked against its source');await admin.locator('#assessment-form input[name=confirmed]').check();
 await admin.locator('#assessment-form select[name=currency]').selectOption('USD');assert.equal(await admin.locator('#assessment-form input[name=confirmed]').isChecked(),false);await admin.locator('#assessment-form select[name=currency]').selectOption('CAD');await admin.locator('#assessment-form input[name=confirmed]').check();
 await admin.locator('#assessment-prepare').click();await admin.locator('#assessment-verify').waitFor({state:'visible'});assert(await admin.locator('#assessment-preparation').innerText().then(t=>t.includes('5 records can be reviewed together')));
 await admin.screenshot({path:dir+'/guided-source-review.png',fullPage:true});
 await admin.locator('#assessment-verify').click();await admin.waitForFunction(()=>document.getElementById('assessment-preview').textContent.includes('5 verified inputs'));await admin.locator('#assessment-submit').click();
 await admin.waitForURL('**/v4.html?portal=1&assessment=*');await admin.locator('#year-control').waitFor({state:'visible'});assert.equal(await admin.locator('#access').isVisible(),false);assert.equal(await admin.locator('#access-dialog').isVisible(),false);
 await admin.locator('#navigation a[href="#P3"]').click();await admin.waitForFunction(()=>document.getElementById('title').textContent.includes('Financial'));assert(await admin.locator('#page').innerText().then(t=>t.includes('2.00x')));
 await admin.locator('[data-metric]').first().click();await admin.locator('#evidence-dialog').waitFor({state:'visible'});await admin.waitForFunction(()=>document.getElementById('evidence-body').textContent.includes('CSV!'));assert(await admin.locator('#evidence-body').innerText().then(t=>t.includes('CSV!')));await admin.locator('#evidence-dialog [data-close]').click();
 for(const pageId of ['P1','P2','P4','P5','P6','P7','P3']){const response=admin.waitForResponse(r=>r.url().includes('/pages/'+pageId)&&r.request().method()==='GET');await admin.locator(`#navigation a[href="#${pageId}"]`).click();assert.equal((await response).status(),200);}
 const [reportPage]=await Promise.all([admin.context().waitForEvent('page'),admin.getByRole('link',{name:'Open draft report / save PDF',exact:true}).click()]);await reportPage.waitForLoadState();assert(await reportPage.locator('body').innerText().then(t=>t.includes('INTERNAL DRAFT')&&t.includes('1,000.00')&&t.includes('2.000×')&&t.includes('browser-portfolio.csv')));await reportPage.screenshot({path:dir+'/internal-draft-report.png',fullPage:true});await reportPage.emulateMedia({media:'print'});assert.equal(await reportPage.locator('#print-report').isVisible(),false);await reportPage.close();
 await admin.screenshot({path:dir+'/portal-assessment-financial.png',fullPage:true});await admin.setViewportSize({width:390,height:844});assert(await admin.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+2));await admin.screenshot({path:dir+'/portal-assessment-mobile.png',fullPage:true});await admin.setViewportSize({width:1440,height:1000});
 await admin.locator('#upload').click();await admin.locator('#workspace').waitFor({state:'visible'});await admin.locator('#organization').selectOption(createdTenant.id);await admin.waitForFunction(()=>document.getElementById('org-title').textContent==='Browser assessment organization');await admin.locator('[data-tab=data]').click();assert.equal(await admin.locator('#assessment-list a').count(),2);
 await admin.locator('[data-tab=administration]').click();await admin.locator('#list-users').click();await admin.locator('#users p').first().waitFor();
 assert.equal(errors.length,0,errors.join('\n'));await browser.close();console.log('BROWSER: client desktop/mobile, 10 categories, upload receipt persistence, role views, analyst workbench, automatic PDF extraction, filtered draft preview, selection, page text, zero-verified guided preparation, bulk review, direct assessment creation/reopening and printable draft report passed.');
})().catch(e=>{console.error(e);process.exit(1);});
