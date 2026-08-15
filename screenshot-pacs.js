const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage({ viewport: { width: 1680, height: 1000 } });
  await page.goto('file://' + path.resolve(__dirname, 'pacs-import.html'));
  await page.waitForTimeout(250);
  await page.screenshot({ path: 'pacs-import.jpg', type: 'jpeg', quality: 92, fullPage: true });
  await browser.close();
})();
