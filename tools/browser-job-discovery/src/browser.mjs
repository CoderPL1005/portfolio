import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { Stop, inspectAccess } from './core.mjs';

let activeSession = false;
const fixtures = new URL('../fixtures/', import.meta.url);
const pages = new Map([
  ['/facebook-search', 'facebook-search.html'],
  ['/search', 'search.html'], ['/posts/entry', 'entry.html'], ['/posts/duplicate', 'entry.html'],
  ['/posts/dtmed', 'dtmed.html'], ['/posts/image', 'image.html'], ['/posts/unrelated', 'unrelated.html'],
  ['/posts/unknown', 'unknown.html'], ['/login', 'login.html'], ['/checkpoint', 'denied.html'],
  ['/recruitment.svg', 'recruitment.svg']
]);

export async function startFixtureServer() {
  const requests = [];
  const server = createServer(async (request, response) => {
    requests.push(request.url);
    const url = new URL(request.url, 'http://fixture.invalid');
    response.setHeader('Content-Security-Policy', "default-src 'none'; img-src 'self'; style-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'");
    response.setHeader('Cache-Control', 'no-store');
    if (request.method !== 'GET' || !pages.has(url.pathname)) { response.writeHead(404); response.end('Fixture not found'); return; }
    try {
      const body = await readFile(new URL(pages.get(url.pathname), fixtures));
      response.writeHead(200, { 'Content-Type': url.pathname.endsWith('.svg') ? 'image/svg+xml' : 'text/html; charset=utf-8' }); response.end(body);
    } catch { response.writeHead(500); response.end('FIXTURE_READ_FAILED'); }
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  return { origin: `http://127.0.0.1:${server.address().port}`, requests,
    close: () => new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())) };
}

/** @implements {import('./contracts').IBrowserSessionProvider} */
export class FixtureBrowserSessionProvider {
  constructor(origin) {
    const url = new URL(origin);
    if (url.hostname !== '127.0.0.1' || url.protocol !== 'http:') throw new Stop('INVALID_FIXTURE_ORIGIN');
    this.origin = url.origin;
    this.blockedRequests = 0;
  }
  async open(budget, signal) {
    if (activeSession) throw new Stop('SESSION_ALREADY_ACTIVE');
    const release = budget.acquireSession(); activeSession = true;
    let browser; let context; let closePromise; let launchSettled = false;
    const close = () => {
      if (closePromise) return closePromise;
      // Hold the lease until an in-flight launch settles; a timeout must not
      // allow another launch while the first browser is still starting.
      if (!launchSettled) return Promise.resolve();
      signal.removeEventListener('abort', onAbort);
      closePromise = (async () => {
        try { await browser?.close(); } finally { release(); activeSession = false; }
      })();
      return closePromise;
    };
    const onAbort = () => { void close(); };
    signal.addEventListener('abort', onAbort, { once: true });
    try {
      if (signal.aborted) throw new Stop('TIME_BUDGET_EXCEEDED');
      try {
        browser = await chromium.launch({ headless: true, timeout: Math.min(30_000, budget.limits.durationMs),
          ...(process.env.BROWSER_POC_EXECUTABLE ? { executablePath: process.env.BROWSER_POC_EXECUTABLE } : {}) });
      } finally { launchSettled = true; }
      // Close even when timeout occurred during browser.launch, before a handle existed.
      if (signal.aborted) { throw new Stop('TIME_BUDGET_EXCEEDED'); }
      context = await browser.newContext({ serviceWorkers: 'block', acceptDownloads: false });
      await context.route('**/*', async route => {
        const request = route.request();
        let allowed = false;
        try {
          const url = new URL(request.url());
          allowed = url.origin === this.origin && pages.has(url.pathname) && request.method() === 'GET';
          budget.check();
        } catch { allowed = false; }
        if (!allowed || signal.aborted) { this.blockedRequests++; await route.abort('blockedbyclient'); return; }
        await route.continue();
      });
      await context.routeWebSocket('**/*', socket => socket.close());
      const page = await context.newPage();
      page.setDefaultTimeout(Math.min(5_000, budget.limits.durationMs));
      page.setDefaultNavigationTimeout(Math.min(10_000, budget.limits.durationMs));
      return { page, close };
    } catch (error) { launchSettled = true; await close(); throw error; }
  }
}

/** @implements {import('./contracts').IBrowserDiscoveryAdapter} */
export class FixtureBrowserDiscoveryAdapter {
  constructor(origin, { searchPath = '/search' } = {}) {
    this.origin = new URL(origin).origin; this.searchPath = searchPath;
  }
  async navigate(page, url, budget) {
    if (new URL(url).origin !== this.origin) throw new Stop('EXTERNAL_NAVIGATION_BLOCKED');
    budget.check();
    let response;
    try { response = await page.goto(url, { waitUntil: 'domcontentloaded' }); }
    catch { throw new Stop('NAVIGATION_FAILED'); }
    budget.check();
    await this.checkAccess(page);
    if (!response?.ok()) throw new Stop('NAVIGATION_FAILED');
  }
  async checkAccess(page) {
    const snapshot = await page.evaluate(() => ({
      url: location.href, title: document.title, body: document.body.innerText.slice(0, 30_000),
      marker: document.body.dataset.access ?? '', hasPassword: !!document.querySelector('input[type=password]'),
      hasCaptcha: !!document.querySelector('[data-captcha],iframe[src*=captcha]')
    }));
    inspectAccess(snapshot);
  }
  async search(page, query, budget) {
    budget.consume('searches');
    await this.navigate(page, new URL(this.searchPath, this.origin).href, budget);
    await page.getByRole('searchbox', { name: 'Search jobs' }).fill(query);
    await Promise.all([
      page.waitForURL(url => url.searchParams.get('q') === query),
      page.getByRole('button', { name: 'Search', exact: true }).click()
    ]);
    budget.check(); await this.checkAccess(page);
    const links = await page.locator('a[data-candidate]').evaluateAll(nodes => nodes.map(node => node.href));
    return [...new Set(links)].filter(link => {
      const url = new URL(link);
      return url.origin === this.origin && url.pathname.startsWith('/posts/');
    }).slice(0, 20);
  }
  async read(page, url, budget) {
    budget.consume('candidates');
    await this.navigate(page, url, budget);
    const post = await page.locator('article[data-post]').evaluate(article => {
      const text = selector => article.querySelector(selector)?.textContent?.trim() ?? '';
      return {
        id: article.dataset.id ?? null, kind: article.dataset.kind ?? 'unknown', title: text('h1'),
        employer: text('[data-employer]'), location: text('[data-location]'), requirements: text('[data-requirements]'),
        body: text('[data-content]'), publishedAt: article.querySelector('time')?.getAttribute('datetime') ?? null,
        relativeTime: article.querySelector('time')?.textContent?.trim() ?? null,
        hasImage: !!article.querySelector('img'),
        evidenceKind: article.dataset.evidence === 'user-provided' ? 'USER_PROVIDED_FIXTURE' : 'SYNTHETIC_FIXTURE'
      };
    });
    budget.check();
    return { ...post, sourceUrl: url };
  }
}

export const fixtureDirectory = fileURLToPath(fixtures);
