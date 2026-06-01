// Dumps slowroads.io client assets into ./dump by driving a headless browser
// and persisting every network response, including dynamically fetched ones.
//
// Usage:
//   npm init -y
//   npm install puppeteer
//   node dump.js
//
// After it boots the page, it stays open for SECONDS_TO_WANDER so the game's
// runtime has time to lazy-load further assets (terrain chunks, audio, etc).
// Move the mouse / press WASD in the visible window to trigger more loads.

const fs = require('fs');
const path = require('path');
const url = require('url');
const puppeteer = require('puppeteer');

const TARGET = 'https://slowroads.io/';
const OUT_DIR = path.join(__dirname, 'dump');
const SECONDS_TO_WANDER = 90; // bump this if you want more assets captured

function safePath(u) {
  const parsed = new url.URL(u);
  // Map https://slowroads.io/foo/bar.js -> dump/slowroads.io/foo/bar.js
  let p = path.join(OUT_DIR, parsed.hostname, decodeURIComponent(parsed.pathname));
  if (p.endsWith(path.sep) || parsed.pathname === '/' || parsed.pathname === '') {
    p = path.join(p, 'index.html');
  }
  if (parsed.search) {
    // Preserve query-string variants by appending a hash so different cache-busted
    // URLs don't overwrite each other.
    const tag = Buffer.from(parsed.search).toString('hex').slice(0, 8);
    const ext = path.extname(p);
    p = p.slice(0, p.length - ext.length) + '__' + tag + ext;
  }
  return p;
}

async function main() {
  fs.mkdirSync(OUT_DIR, { recursive: true });

  const browser = await puppeteer.launch({
    headless: false,            // visible so you can drive the car a bit
    args: ['--disable-cache', '--disable-application-cache'],
  });
  const page = await browser.newPage();

  // Disable HTTP cache so every asset is fetched fresh through our hook.
  const client = await page.target().createCDPSession();
  await client.send('Network.setCacheDisabled', { cacheDisabled: true });

  const seen = new Set();
  let saved = 0;
  let skipped = 0;

  page.on('response', async (response) => {
    const u = response.url();
    if (!u.startsWith('http')) return;
    if (seen.has(u)) return;
    seen.add(u);

    try {
      const status = response.status();
      if (status >= 300 && status < 400) { skipped++; return; }   // redirect
      const buf = await response.buffer();
      const out = safePath(u);
      fs.mkdirSync(path.dirname(out), { recursive: true });
      fs.writeFileSync(out, buf);
      saved++;
      if (saved % 25 === 0) console.log(`  saved ${saved} files...`);
    } catch (e) {
      skipped++; // some responses (e.g. preflight, opaque) have no buffer
    }
  });

  console.log(`Navigating to ${TARGET} ...`);
  await page.goto(TARGET, { waitUntil: 'networkidle2', timeout: 120_000 });

  console.log(`Page loaded. Wandering for ${SECONDS_TO_WANDER}s to trigger lazy loads.`);
  console.log('Tip: click the canvas and drive around — new terrain chunks will fetch.');
  await new Promise((r) => setTimeout(r, SECONDS_TO_WANDER * 1000));

  await browser.close();
  console.log(`\nDone. ${saved} files written to ${OUT_DIR} (${skipped} skipped).`);
}

main().catch((e) => { console.error(e); process.exit(1); });
