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
    await page.goto(process.env.EVIDENCE_TEST_URL + '/');
    await page.locator('#open-upload').click();
    await page.locator('#organization-name').fill('Synthetic browser V2 upload');
    const workbook = path.resolve('src/IreiMvp.Web/Data/Examples/IREI_MVP_Stage1_V2_NGOB_Demo.xlsx');
    await page.locator('#file-input').setInputFiles(workbook);
    let uploads = 0;
    page.on('request', request => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/submissions') uploads++;
    });
    await page.locator('#submit-upload').click();
    await page.locator('#staff-sign-in').waitFor({ state: 'visible' });
    assert.match(await page.locator('#upload-error').innerText(), /Sign in/);
    assert.equal(uploads, 0, 'An unsigned request must not transmit the workbook');
    assert(await page.locator('#submit-upload').isEnabled());
    assert.equal(await page.locator('#file-input').evaluate(el => el.files.length), 1);
    assert.equal(await page.locator('#staff-sign-in a').getAttribute('target'), '_blank');

    // Sign in in a second tab while the workbook remains selected in the first.
    const login = await context.newPage();
    await login.goto(process.env.EVIDENCE_TEST_URL + '/evidence.html');
    await login.locator('#login input[name=email]').fill('admin@example.test');
    await login.locator('#login input[name=password]').fill(process.env.EVIDENCE_TEST_PASSWORD);
    await login.locator('#login button').click();
    await login.locator('#workspace').waitFor({ state: 'visible' });
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/api/submissions' && r.request().method() === 'POST');
    await page.locator('#submit-upload').click();
    assert.equal((await response).status(), 200);
    await page.locator('#upload-dialog').waitFor({ state: 'hidden', timeout: 60000 });
    assert.equal(uploads, 1);
    assert.match(await page.locator('#organization-chip').innerText(), /Synthetic browser V2 upload/);
    assert.equal(await page.locator('body').getAttribute('aria-busy'), 'false');
    assert.equal(errors.length, 0, errors.join('\n'));
    console.log('V2 BROWSER: sign-in link, selected-file retention, cross-tab sign-in, authenticated Excel upload, dashboard render and cleared loading state passed.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exit(1); });
