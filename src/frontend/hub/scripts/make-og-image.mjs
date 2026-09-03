// Renders public/og-image.png from source rather than shipping an opaque
// bitmap nobody can edit. Run after any brand change:
//
//   node scripts/make-og-image.mjs
//
// Playwright is already a dev dependency here for the E2E suite, and it is the
// only renderer on hand that loads the real self-hosted brand faces — every
// other converter falls back to DejaVu, which is not the typeface.

import { chromium } from 'playwright';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { writeFile } from 'node:fs/promises';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const font = (p) => `file://${resolve(root, 'node_modules', p)}`;

const WIDTH = 1200;
const HEIGHT = 630;

// Tokens, copied from src/index.css. Kept literal because this runs outside the
// bundler and cannot import the stylesheet.
const BG = '#131114';
const PANE = '#1a171b';
const LINE = '#2c272c';
const TEXT = '#efe9e4';
const MUTED = '#a09aa0';
const BRAND = '#d4943a';

const html = `<!doctype html>
<html><head><meta charset="utf-8"><style>
  @font-face {
    font-family: 'Schibsted Grotesk Variable';
    src: url('${font('@fontsource-variable/schibsted-grotesk/files/schibsted-grotesk-latin-wght-normal.woff2')}') format('woff2-variations');
    font-weight: 400 900;
  }
  @font-face {
    font-family: 'Figtree Variable';
    src: url('${font('@fontsource-variable/figtree/files/figtree-latin-wght-normal.woff2')}') format('woff2-variations');
    font-weight: 300 900;
  }
  @font-face {
    font-family: 'IBM Plex Mono';
    src: url('${font('@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2')}') format('woff2');
    font-weight: 400;
  }

  * { margin: 0; padding: 0; box-sizing: border-box; }

  body {
    width: ${WIDTH}px; height: ${HEIGHT}px;
    background: ${BG};
    color: ${TEXT};
    font-family: 'Figtree Variable', sans-serif;
    display: flex; flex-direction: column; justify-content: space-between;
    padding: 76px 84px;
  }

  /* The card is the Deck, seen from far enough away to read as a shape: one
     strip of tabs over one pane. Nothing else earns the space. */
  .mark { display: flex; align-items: center; gap: 20px; }
  .mark svg { width: 56px; height: 56px; }
  .wordmark {
    font-family: 'Schibsted Grotesk Variable', sans-serif;
    font-weight: 700; font-size: 40px; letter-spacing: -0.02em;
  }

  h1 {
    font-family: 'Schibsted Grotesk Variable', sans-serif;
    font-weight: 700; font-size: 76px; line-height: 1.04;
    letter-spacing: -0.035em; max-width: 15ch;
  }
  h1 em { font-style: normal; color: ${BRAND}; }

  .foot { display: flex; align-items: flex-end; justify-content: space-between; gap: 40px; }

  .replaces { display: flex; gap: 10px; }
  .chip {
    font-family: 'IBM Plex Mono', monospace;
    font-size: 15px; letter-spacing: 0.06em; text-transform: uppercase;
    color: ${MUTED};
    border: 1px solid ${LINE}; border-radius: 999px;
    padding: 8px 16px; background: ${PANE};
  }

  .domain {
    font-family: 'IBM Plex Mono', monospace;
    font-size: 19px; color: ${MUTED}; letter-spacing: 0.02em;
  }
</style></head>
<body>
  <div class="mark">
    <svg viewBox="0 0 512 512" fill="none" stroke="${TEXT}" stroke-width="29"
         stroke-linecap="round" stroke-linejoin="round">
      <path d="M58,141 C146,209 146,303 58,371"/>
      <path d="M116,102 C205,180 205,332 116,410"/>
      <path d="M175,62 C263,160 263,352 175,450"/>
      <path d="M337,62 C249,160 249,352 337,450"/>
      <path d="M396,102 C307,180 307,332 396,410"/>
      <path d="M454,141 C366,209 366,303 454,371"/>
    </svg>
    <span class="wordmark">Xcord</span>
  </div>

  <h1>Four tools in one platform <em>you own</em>.</h1>

  <div class="foot">
    <div class="replaces">
      <span class="chip">Chat</span>
      <span class="chip">Community</span>
      <span class="chip">Memberships</span>
      <span class="chip">Live</span>
    </div>
    <span class="domain">xcord.net</span>
  </div>
</body></html>`;

const browser = await chromium.launch();
try {
  const page = await browser.newPage({
    viewport: { width: WIDTH, height: HEIGHT },
    deviceScaleFactor: 1,
  });
  await page.setContent(html, { waitUntil: 'load' });
  await page.evaluate(() => document.fonts.ready);
  const png = await page.screenshot({ type: 'png' });
  const out = resolve(root, 'public', 'og-image.png');
  await writeFile(out, png);
  console.log(`wrote ${out} (${WIDTH}x${HEIGHT}, ${png.length} bytes)`);
} finally {
  await browser.close();
}
