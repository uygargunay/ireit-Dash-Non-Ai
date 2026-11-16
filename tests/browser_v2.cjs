// Exercise the exact V2 workbook upload flow with synthetic test credentials only.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
(async () => {
  const browser = await chromium.launch({ executablePath: process.env.EVIDENCE_CHROME, headless: true,
    args: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage', '--single-process', '--no-zygote'] });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(process.env.EVIDENCE_TEST_URL + '/legacy-v2.html');
    await page.locator('#open-upload').click();
    await page.locator('#organization-name').fill('Synthetic browser V2 upload');
    const workbook = path.resolve('src/IreiMvp.Web/Data/Examples/IREI_MVP_Stage1_V2_NGOB_Demo.xlsx');
    await page.locator('#file-input').setInputFiles(workbook);
    let uploads = 0;
    page.on('request', request => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/submissions') uploads++;
    });
    let sessionRequests = 0;
    await page.route('**/api/evidence/session', route => {
      sessionRequests++;
      return route.fulfill({ status: 403, contentType: 'application/json', body: '{"error":"No portal session"}' });
    });
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/api/submissions' && r.request().method() === 'POST');
    await page.locator('#submit-upload').click();
    assert.equal((await response).status(), 200);
    await page.locator('#upload-dialog').waitFor({ state: 'hidden', timeout: 60000 });
    assert.equal(uploads, 1, "Anonymous V2 upload succeeds once");
    assert.equal(sessionRequests, 0, "V2 never requests portal session confirmation");
    assert.match(await page.locator('#organization-chip').innerText(), /Synthetic browser V2 upload/);
    assert.equal(await page.locator('body').getAttribute('aria-busy'), 'false');
    assert.equal(errors.length, 0, errors.join('\n'));
    console.log('V2 BROWSER: anonymous Excel upload without a portal session, dashboard render and cleared loading state passed.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exit(1); });
