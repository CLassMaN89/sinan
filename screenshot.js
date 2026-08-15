const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage({ viewport: { width: 1600, height: 820 } });
  await page.goto('file://' + path.resolve(__dirname, 'index.html'));
  await page.waitForTimeout(300);

  await page.screenshot({ path: 'screenshot-full.jpg', type: 'jpeg', quality: 92 });

  const strip = await page.$('.patient-strip');
  await strip.screenshot({ path: 'screenshot-strip.jpg', type: 'jpeg', quality: 92 });

  const viewer = await page.$('.viewer');
  await viewer.screenshot({ path: 'screenshot-viewer.jpg', type: 'jpeg', quality: 92 });

  await browser.close();
})();
