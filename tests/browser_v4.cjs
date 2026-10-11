// Optional Playwright verification; use the shared runtime or an existing Playwright installation.
const { chromium } = require('playwright');
const fs = require('fs');
const [base, id, key, output] = process.argv.slice(2);
(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_EXECUTABLE || undefined, args: ['--no-sandbox'] });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(`${base}/v4.html?assessment=${id}#P1`);
    await page.locator('#access-key').fill(key); await page.locator('#access-form button.primary').click();
    await page.locator('.tiles').waitFor();
    await page.locator('#year').selectOption('2026-2027');
    await page.waitForFunction(() => document.querySelector('#context').textContent.includes('2026-2027'));
    for (let n = 1; n <= 7; n++) {
      await page.locator(`#navigation a[href="#P${n}"]`).click();
      await page.waitForFunction(n => document.querySelector('#title').textContent.startsWith(`Page ${n}:`), n);
      await page.screenshot({ path: `${output}/page-${n}.png`, fullPage: true });
      if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw Error(`Page ${n} overflows viewport`);
    }
    await page.locator('#navigation a[href="#P3"]').click();
    await page.locator('#building-details').waitFor();
    if (await page.locator('#building-details').getAttribute('open') !== null) throw Error('Building details expanded by default');
    await page.locator('#building-details summary').click();
    if (await page.locator('#building-details tbody tr').count() !== 16) throw Error('Expected 16 building rows');
    await page.locator('.tile[data-metric]').first().click();
    await page.getByRole('heading', { name: 'Source → mapped facts → calculation → result' }).waitFor();
    await page.locator('#evidence-dialog [data-close]').click();
    await page.locator('#year').selectOption('2025-2026');
    await page.waitForFunction(() => document.querySelector('#context').textContent.includes('2025-2026'));
    if (!(await page.locator('.tile').first().innerText()).includes('$9.35M')) throw Error('Budget year tile failed to update');
    await page.locator('#year').selectOption('2026-2027');
    await page.waitForFunction(() => document.querySelector('#context').textContent.includes('2026-2027'));
    const revenue = await page.locator('.tile').first().innerText();
    if (!revenue.includes('$12.70M')) throw Error('Reporting year failed to update financial tile');
    await page.locator('#option-form [name="case"]').selectOption('Without placeholder');
    await page.locator('#option-form [name="kind"]').selectOption('Bridge');
    await page.locator('#option-form [name="amount"]').fill('3376096.98');
    await page.locator('#option-form button').click(); await page.locator('#option-result table').waitFor();
    if (!(await page.locator('#option-result').innerText()).includes('0.75x')) throw Error('Bridge coverage result missing');
    await page.locator('#review').click();
    await page.locator('#record-id').fill('BROWSER-ACTION');
    await page.locator('[data-field="Action_Text"]').fill('Confirm the subsidy commitment');
    await page.locator('[data-field="Linked_Exception_IDs"]').fill('EXC-NGOB-001');
    await page.locator('#reviewer').fill('Browser verification analyst');
    await page.locator('#review-reason').fill('Verify shared action persistence');
    await page.locator('#record-form button.primary').click();
    await page.waitForFunction(() => !document.querySelector('#record-dialog').open);
    await page.locator('#navigation a[href="#P6"]').click();
    await page.getByText('Confirm the subsidy commitment', { exact: true }).waitFor();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#navigation a[href="#P1"]').click();
    await page.waitForFunction(() => document.querySelector('#title').textContent.startsWith('Page 1:'));
    await page.screenshot({ path: `${output}/mobile-page-1.png`, fullPage: true });
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw Error('Mobile overflow');
    if (errors.length) throw Error(errors.join('\n'));
    console.log('PASS: seven browser pages, mobile width, collapsed details, evidence, reporting year and bridge test; no JavaScript errors');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
