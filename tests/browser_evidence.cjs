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
 await admin.locator('[data-tab=administration]').click();await admin.locator('#list-users').click();await admin.locator('#users p').first().waitFor();
 assert.equal(errors.length,0,errors.join('\n'));await browser.close();console.log('BROWSER: client desktop/mobile, 10 categories, upload receipt persistence, role views and analyst workbench passed.');
})().catch(e=>{console.error(e);process.exit(1);});
