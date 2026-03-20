import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const BASE_URL = 'http://localhost:4175';
const DIST_DIR = join(__dirname, '..', 'dist', 'prerendered');

const PAGES: string[] = [
  '/',
  '/pricing',
  '/get-started',
  '/download',
  '/docs/self-hosting',
  '/terms',
  '/privacy',
];

function outputPathFor(page: string): string {
  if (page === '/') {
    return join(DIST_DIR, 'index.html');
  }
  return join(DIST_DIR, ...page.replace(/^\//, '').split('/'), 'index.html');
}

function waitForServer(url: string, timeoutMs = 30_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    function poll() {
      fetch(url)
        .then(() => resolve())
        .catch(() => {
          if (Date.now() >= deadline) {
            reject(new Error(`Server at ${url} did not become ready within ${timeoutMs}ms`));
          } else {
            setTimeout(poll, 250);
          }
        });
    }
    poll();
  });
}

async function main(): Promise<void> {
  const server = spawn('npx', ['vite', 'preview', '--port', '4175'], {
    cwd: join(__dirname, '..'),
    stdio: 'inherit',
    shell: true,
  });

  server.on('error', (err) => {
    console.error('Failed to start preview server:', err);
  });

  try {
    console.log('Waiting for preview server to be ready...');
    await waitForServer(BASE_URL);
    console.log('Preview server is ready.');

    const browser = await chromium.launch({ headless: true });

    try {
      for (const page of PAGES) {
        const url = `${BASE_URL}${page}`;
        console.log(`Rendering ${url}...`);

        const context = await browser.newContext();
        const browserPage = await context.newPage();

        await browserPage.goto(url, { waitUntil: 'networkidle' });

        const html = await browserPage.evaluate(
          () => document.documentElement.outerHTML
        );

        const outPath = outputPathFor(page);
        mkdirSync(dirname(outPath), { recursive: true });
        writeFileSync(outPath, html, 'utf-8');

        console.log(`  -> wrote ${outPath}`);

        await context.close();
      }
    } finally {
      await browser.close();
    }

    console.log('Prerendering complete.');
  } finally {
    server.kill();
  }
}

main().then(() => process.exit(0)).catch((err) => {
  console.error('Prerender failed:', err);
  process.exit(1);
});
