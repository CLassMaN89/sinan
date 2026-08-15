const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });

  for (const name of ['variant1', 'variant2', 'variant3']) {
    await page.goto('file://' + path.resolve(__dirname, name + '.html'));
    await page.waitForTimeout(250);
    await page.screenshot({ path: name + '.jpg', type: 'jpeg', quality: 92 });
  }

  await browser.close();
})();
