const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage({ viewport: { width: 1600, height: 130 } });
  for (const name of ['header1', 'header2', 'header3', 'header4']) {
    await page.goto('file://' + path.resolve(__dirname, name + '.html'));
    await page.waitForTimeout(200);
    await page.screenshot({ path: name + '.jpg', type: 'jpeg', quality: 92 });
  }
  await browser.close();
})();
