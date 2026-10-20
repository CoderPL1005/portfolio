import test from 'node:test';
import assert from 'node:assert/strict';
import { ActivityBudget, SearchPlanner, JobPostExtractor, DiscoveryReporter, runDiscovery } from '../src/core.mjs';
import { FixtureBrowserSessionProvider, FixtureBrowserDiscoveryAdapter, startFixtureServer } from '../src/browser.mjs';

const target = { technologies: ['.NET', 'C#', 'ASP.NET Core', 'Angular', 'TypeScript .NET'],
  locations: ['Hà Nội', 'Hanoi', 'Ha Noi'], seniorities: ['Intern', 'Fresher', 'Junior', '0–2 years'] };

test('real Chromium: search without post URLs, extract Group/Page fixtures, match, dedup and preserve unknown DTMED evidence', async () => {
  const server = await startFixtureServer();
  try {
    const report = await runDiscovery({ sessionProvider: new FixtureBrowserSessionProvider(server.origin),
      adapter: new FixtureBrowserDiscoveryAdapter(server.origin), planner: new SearchPlanner(),
      extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(), target, budget: new ActivityBudget() });
    assert.equal(report.stopCode, null); assert.equal(report.gates.browserTechnical, 'BROWSER_TECHNICALLY_READY');
    assert.equal(report.budget.searches, 5); assert.equal(report.budget.candidates, 6);
    assert.equal(report.budget.peakSessions, 1); assert.equal(report.budget.sessions, 0);
    assert.equal(report.metrics.uniquePosts, 5); assert.equal(report.metrics.duplicates, 1); assert.equal(report.metrics.targetMatches, 1);
    assert.equal(report.metrics.imageExtractionRequired, 1);
    assert.equal(report.jobs.find(j => j.employer === 'DTMED').match.reason, 'LOCATION_UNVERIFIED');
    assert.equal(report.jobs.find(j => j.employer === 'DTMED').match.experience.maximum, null);
    assert.equal(report.jobs.find(j => j.employer === 'DTMED').evidenceKind, 'USER_PROVIDED_FIXTURE');
    assert.ok(report.jobs.some(j => j.kind === 'page'));
    assert.ok(report.jobs.every(j => j.sourceUrl.startsWith(server.origin)));
    assert.ok(server.requests.some(url => url.includes('q=')));
    assert.equal(report.gates.liveDiscovery, 'LIVE_UNSEEDED_DISCOVERY_NOT_RUN');
    assert.doesNotMatch(JSON.stringify(report), /access_token|refresh_token|storageState|password/iu);
  } finally { await server.close(); }
});

for (const [path, code] of [['/login', 'LOGIN_REQUIRED'], ['/checkpoint', 'ACCESS_RESTRICTED'], ['/missing', 'NAVIGATION_FAILED']]) {
  test(`real Chromium: ${path} stops before candidate reads or later searches`, async () => {
    const server = await startFixtureServer();
    try {
      const result = await runDiscovery({ sessionProvider: new FixtureBrowserSessionProvider(server.origin),
        adapter: new FixtureBrowserDiscoveryAdapter(server.origin, { searchPath: path }), planner: new SearchPlanner(),
        extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(), target, budget: new ActivityBudget() });
      assert.equal(result.stopCode, code); assert.equal(result.budget.searches, 1); assert.equal(result.budget.candidates, 0);
      assert.ok(!server.requests.some(url => url.startsWith('/posts/')));
    } finally { await server.close(); }
  });
}

test('real Chromium: external requests are blocked, concurrent session rejected, next session has no persisted cookie', async () => {
  const server = await startFixtureServer(); const budget = new ActivityBudget();
  const provider = new FixtureBrowserSessionProvider(server.origin); let session;
  try {
    session = await provider.open(budget, new AbortController().signal);
    await assert.rejects(() => new FixtureBrowserSessionProvider(server.origin).open(new ActivityBudget(), new AbortController().signal), /SESSION_ALREADY_ACTIVE/);
    await session.page.goto(`${server.origin}/search`);
    await session.page.context().addCookies([{ name: 'synthetic_session', value: 'not-a-real-credential', url: server.origin }]);
    const fetched = await session.page.evaluate(async () => {
      try { await fetch('https://www.facebook.com/'); return true; } catch { return false; }
    });
    assert.equal(fetched, false);
    // Direct navigation is also stopped by routing before any external HTTP.
    await assert.rejects(() => session.page.goto('https://www.facebook.com/'));
    assert.ok(provider.blockedRequests >= 1);
    await session.close(); session = undefined;
    session = await provider.open(budget, new AbortController().signal);
    assert.deepEqual(await session.page.context().cookies(), []);
  } finally { await session?.close(); await server.close(); }
});

test('real Chromium: candidate checkpoint stops before the next candidate', async () => {
  const server = await startFixtureServer();
  const adapter = new FixtureBrowserDiscoveryAdapter(server.origin);
  adapter.search = async (_, __, budget) => { budget.consume('searches'); return [`${server.origin}/checkpoint`, `${server.origin}/posts/entry`]; };
  try {
    const report = await runDiscovery({ sessionProvider: new FixtureBrowserSessionProvider(server.origin), adapter,
      planner: new SearchPlanner(), extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(), target,
      budget: new ActivityBudget() });
    assert.equal(report.stopCode, 'ACCESS_RESTRICTED'); assert.equal(report.budget.candidates, 1);
    assert.ok(!server.requests.includes('/posts/entry'));
  } finally { await server.close(); }
});

test('real Chromium: wall-clock deadline closes an in-flight operation and releases the lease', async () => {
  const server = await startFixtureServer();
  const adapter = new FixtureBrowserDiscoveryAdapter(server.origin);
  adapter.search = async (page, _, budget) => { budget.consume('searches'); await page.waitForTimeout(30_000); return []; };
  try {
    const report = await runDiscovery({ sessionProvider: new FixtureBrowserSessionProvider(server.origin), adapter,
      planner: new SearchPlanner(), extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(), target,
      budget: new ActivityBudget({ durationMs: 1500 }) });
    assert.equal(report.stopCode, 'TIME_BUDGET_EXCEEDED'); assert.equal(report.budget.sessions, 0);
    assert.equal(report.budget.candidates, 0);
  } finally { await server.close(); }
});
