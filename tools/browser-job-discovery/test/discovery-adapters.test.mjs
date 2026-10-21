import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { ActivityBudget, facebookAccessGate, JobPostExtractor, DiscoveryReporter, runDiscovery } from '../src/core.mjs';
import { buildFacebookSearchUrl, canonicalFacebookPost } from '../src/discovery-contract.mjs';
import { FacebookBrowserDiscoveryAdapter } from '../src/facebook-browser-adapter.mjs';
import { SocialApisHubHttpAdapter, createMockSocialApisTransport, PROVIDER_SCHEMA_STATUS, SocialApisCaptureDiscoveryAdapter, SocialApisLiveDiscoveryAdapter } from '../src/socialapis-adapter.mjs';
import { summarizeSocialApisRows } from '../src/socialapis-live-smoke.mjs';
import { validateDateFilters } from '../src/discovery-contract.mjs';
import { buildDiscoveryYield } from '../src/discovery-yield.mjs';

const query = 'C# .NET Angular Junior Hà Nội';
const urls = ['https://m.facebook.com/groups/fixture.jobs/permalink/1001?ref=search',
  'https://www.facebook.com/groups/fixture.jobs/posts/1001', 'https://www.facebook.com/example/posts/1002'];
const envelope = (posts = [urls[0]], next_cursor = null) => ({ fixture_kind: 'SYNTHETIC_FIXTURE', posts: posts.map(url => ({ url })), next_cursor });
function provider(options = {}, pages = [envelope()]) {
  const transport = createMockSocialApisTransport(pages, options.mock);
  return { transport, adapter: new SocialApisHubHttpAdapter({ baseUrl: 'https://api.socialapis.io',
    apiKey: 'synthetic-test-key', authorizationStatus: 'OFFLINE_FIXTURE_ONLY', transport, ...options }) };
}
function browserPage({ anchors = urls, access = {}, marker = 'synthetic', error, delay = 0 } = {}) {
  const calls = []; let url; let evaluations = 0;
  return { calls, url: () => url, async goto(value) {
    calls.push(value); url = value;
    if (delay) await new Promise(resolve => setTimeout(resolve, delay));
    if (error) throw error;
    return { ok: () => true };
  }, async evaluate() {
    evaluations++;
    return evaluations === 1 ? { url, ...access } : { anchors, marker };
  } };
}
const browser = options => new FacebookBrowserDiscoveryAdapter({ fixtureOrigin: 'http://127.0.0.1:12345', ...options });
function assertRecord(record, strategy) {
  assert.deepEqual(Object.keys(record).sort(), ['source', 'query', 'candidate_post_url', 'source_post_id',
    'retrieved_at', 'evidence', 'authorization_status', 'discovery_strategy'].sort());
  assert.equal(record.source, 'FACEBOOK'); assert.equal(record.query, query);
  assert.equal(record.discovery_strategy, strategy);
  assert.equal(record.authorization_status, 'OFFLINE_FIXTURE_ONLY');
  assert.equal(record.evidence.kind, 'SYNTHETIC_FIXTURE'); assert.equal(record.evidence.live_verified, false);
  assert.equal(new Date(record.retrieved_at).toISOString(), record.retrieved_at);
}

test('C#, .NET, Angular and Vietnamese survive URLSearchParams round-trip without fragment', () => {
  const url = new URL(buildFacebookSearchUrl(query));
  assert.equal(url.searchParams.get('q'), query); assert.equal(url.hash, '');
  assert.equal(url.pathname, '/search/posts/'); assert.match(url.search, /C%23/u);
  for (const input of ['', ' ', 'a\nkey', 'x'.repeat(301), null]) assert.throws(() => buildFacebookSearchUrl(input), /INVALID_QUERY/);
});
for (const input of [
  'https://www.facebook.com/search/posts?q=C%23', 'https://www.facebook.com/search/posts/100',
  'https://www.facebook.com/groups/jobs', 'https://www.facebook.com/example',
  'https://facebook.com.evil.invalid/a/posts/1', 'https://facebook.com@evil.invalid/a/posts/1',
  'https://user:pass@facebook.com/a/posts/1', 'http://facebook.com/a/posts/1',
  'https://facebook.com:123/a/posts/1', 'https://facebook.com/l.php?u=anything',
  'https://facebook.com/a/posts/1/comments', 'https://facebook.com/a/posts/%31',
  'https://facebook.com/story.php?story_fbid=1&id=2&id=3', 'javascript:alert(1)', '/a/posts/1'
]) test(`reject noncanonical/search/unsafe URL: ${input}`, () => assert.equal(canonicalFacebookPost(input), null));

test('canonical permalink aliases share identity and remove tracking', () => {
  assert.deepEqual(canonicalFacebookPost(urls[0]), canonicalFacebookPost(urls[1]));
  assert.deepEqual(canonicalFacebookPost('https://m.facebook.com/story.php?story_fbid=123&id=456&ref=search'),
    { url: 'https://www.facebook.com/456/posts/123', id: '123' });
  assert.equal(canonicalFacebookPost('https://facebook.com/page/posts/pfbidABC123').id, 'pfbidABC123');
});

test('browser discovery emits shared evidence records, deduplicates before candidate budget consumption', async () => {
  const page = browserPage({ anchors: [...urls, buildFacebookSearchUrl(query), 'https://evil.invalid'] });
  const budget = new ActivityBudget(); const rows = await browser().discover(page, query, budget);
  assert.equal(rows.length, 2); rows.forEach(row => assertRecord(row, 'FACEBOOK_BROWSER'));
  assert.equal(budget.snapshot().searches, 1); assert.equal(budget.snapshot().candidates, 2);
  assert.equal(new URL(page.calls[0]).searchParams.get('q'), query);
  assert.equal(new URL(page.calls[0]).hostname, '127.0.0.1');
});
test('browser live and nonloopback origins stay locked even with caller authorization flag', () => {
  assert.throws(() => browser({ mode: 'live', authorized: true }), /FACEBOOK_LIVE_COLLECTION_DISABLED/);
  for (const fixtureOrigin of ['https://www.facebook.com', 'http://localhost:1234', 'http://127.0.0.1@evil.invalid', 'http://127.0.0.1/path']) {
    assert.throws(() => browser({ fixtureOrigin }), /INVALID_FIXTURE_ORIGIN/);
  }
  assert.equal(facebookAccessGate({ authorized: true }).allowed, false);
});
for (const [access, code] of [[{ hasPassword: true }, 'LOGIN_REQUIRED'], [{ hasCaptcha: true }, 'ACCESS_RESTRICTED']]) {
  test(`browser immediately stops on ${code}`, async () => {
    const page = browserPage({ access }); const budget = new ActivityBudget();
    await assert.rejects(() => browser().discover(page, query, budget), new RegExp(code));
    assert.equal(page.calls.length, 1); assert.equal(budget.snapshot().candidates, 0);
  });
}
test('browser refuses unmarked evidence and request/candidate/deadline exhaustion', async () => {
  await assert.rejects(() => browser().discover(browserPage({ marker: 'live' }), query, new ActivityBudget()), /FIXTURE_PROVENANCE_REQUIRED/);
  const budget = new ActivityBudget({ searches: 1 }); budget.consume('searches'); const page = browserPage();
  await assert.rejects(() => browser().discover(page, query, budget), /SEARCHES_BUDGET_EXCEEDED/);
  assert.equal(page.calls.length, 0);
  await assert.rejects(() => browser().discover(browserPage(), query, new ActivityBudget({ candidates: 1 })), /CANDIDATES_BUDGET_EXCEEDED/);
  let now = 0; const deadline = new ActivityBudget({}, () => now); now = 600_000;
  await assert.rejects(() => browser().discover(page, query, deadline), /TIME_BUDGET_EXCEEDED/);
});
test('browser timeout returns safe code, never retries or continues extraction', async () => {
  const page = browserPage({ delay: 40 }); const budget = new ActivityBudget();
  await assert.rejects(() => browser({ timeoutMs: 5 }).discover(page, query, budget), /DISCOVERY_TIMEOUT/);
  await new Promise(resolve => setTimeout(resolve, 60));
  assert.equal(page.calls.length, 1); assert.equal(budget.snapshot().candidates, 0);
});

test('provider mock uses documented request encoding, shares contract, paginates and deduplicates', async () => {
  const { adapter, transport } = provider({}, [envelope([urls[0]], 'page2'), envelope([urls[1], urls[2]])]);
  const budget = new ActivityBudget(); const rows = await adapter.discover(query, budget);
  assert.equal(rows.length, 2); rows.forEach(row => assertRecord(row, 'SOCIALAPISHUB_HTTP'));
  assert.equal(transport.calls.length, 2); assert.equal(budget.snapshot().candidates, 2);
  const url = new URL(transport.calls[0].url);
  assert.equal(url.origin, 'https://api.socialapis.io'); assert.equal(url.pathname, '/facebook/search/posts');
  assert.equal(url.searchParams.get('query'), query); assert.equal(url.hash, '');
  assert.equal(new URL(transport.calls[1].url).searchParams.get('end_cursor'), 'page2');
  assert.equal(transport.calls[0].method, 'GET'); assert.equal(transport.calls[0].redirect, 'error');
  assert.equal(transport.calls[0].credentials, 'omit');
  assert.doesNotMatch(JSON.stringify({ adapter, transport, rows }), /synthetic-test-key/u);
});
test('provider missing key, authorization, arbitrary transport and live mode fail before transport', () => {
  const { transport } = provider();
  const config = { baseUrl: 'https://api.socialapis.io', apiKey: 'synthetic-test-key', authorizationStatus: 'OFFLINE_FIXTURE_ONLY', transport };
  for (const apiKey of [undefined, '', 'has\r\nheader']) assert.throws(() => new SocialApisHubHttpAdapter({ ...config, apiKey }), /PROVIDER_API_KEY_REQUIRED/);
  for (const authorizationStatus of [undefined, 'AUTHORIZED', 'DENIED']) assert.throws(() => new SocialApisHubHttpAdapter({ ...config, authorizationStatus }), /PROVIDER_AUTHORIZATION_DENIED/);
  assert.throws(() => new SocialApisHubHttpAdapter({ ...config, transport: { request: globalThis.fetch } }), new RegExp(PROVIDER_SCHEMA_STATUS));
  assert.throws(() => new SocialApisHubHttpAdapter({ ...config, mode: 'live', authorized: true }), new RegExp(PROVIDER_SCHEMA_STATUS));
  for (const baseUrl of ['http://api.socialapis.io', 'https://evil.invalid', 'https://api.socialapis.io/?token=secret', 'https://user:pass@api.socialapis.io']) {
    assert.throws(() => new SocialApisHubHttpAdapter({ ...config, baseUrl }), /INVALID_PROVIDER_BASE_URL/);
  }
  assert.equal(transport.calls.length, 0);
});
for (const data of [null, {}, { data: { results: [] } }, envelope([buildFacebookSearchUrl(query)]),
  { ...envelope(), fixture_kind: 'REAL' }, { ...envelope(), next_cursor: 'https://evil.invalid' },
  { ...envelope(), posts: [{ url: urls[0], api_key: 'do-not-copy' }] }, { ...envelope(), next_cursor: undefined }]) {
  test(`provider rejects invalid/missing mock response shape ${JSON.stringify(data)}`, async () => {
    const { adapter, transport } = provider({}, [data]);
    await assert.rejects(() => adapter.discover(query, new ActivityBudget()), /PROVIDER_SCHEMA_INVALID/);
    assert.equal(transport.calls.length, 1);
  });
}
test('provider search budget prevents extra requests, including pagination', async () => {
  const { adapter, transport } = provider({}, [envelope([], 'page2'), envelope()]);
  await assert.rejects(() => adapter.discover(query, new ActivityBudget({ searches: 1 })), /SEARCHES_BUDGET_EXCEEDED/);
  assert.equal(transport.calls.length, 1);
});
test('provider candidate budget fails closed', async () => {
  const { adapter } = provider({}, [envelope([urls[0], urls[2]])]);
  await assert.rejects(() => adapter.discover(query, new ActivityBudget({ candidates: 1 })), /CANDIDATES_BUDGET_EXCEEDED/);
});
test('provider page cap and repeated cursors fail without unbounded pagination', async () => {
  const capped = provider({ maxPages: 1 }, [envelope([], 'next')]);
  await assert.rejects(() => capped.adapter.discover(query, new ActivityBudget()), /PROVIDER_PAGE_BUDGET_EXCEEDED/);
  assert.equal(capped.transport.calls.length, 1);
  const repeated = provider({}, [envelope([], 'same'), envelope([], 'same')]);
  await assert.rejects(() => repeated.adapter.discover(query, new ActivityBudget()), /PROVIDER_CURSOR_REPEATED/);
  assert.equal(repeated.transport.calls.length, 2);
});
test('provider timeout aborts request with zero retry', async () => {
  const { adapter, transport } = provider({ timeoutMs: 5, mock: { delayMs: 100 } });
  await assert.rejects(() => adapter.discover(query, new ActivityBudget()), /DISCOVERY_TIMEOUT/);
  assert.equal(transport.calls.length, 1);
});
for (const status of [401, 403, 429, 500]) test(`provider HTTP ${status} never retries/top-ups`, async () => {
  const { adapter, transport } = provider({ mock: { status } });
  await assert.rejects(() => adapter.discover(query, new ActivityBudget()), /PROVIDER_REQUEST_REJECTED/);
  assert.equal(transport.calls.length, 1);
});
test('provider response size bounded and transport failures safe', async () => {
  const large = provider({}, [envelope(['x'.repeat(64_001)])]);
  await assert.rejects(() => large.adapter.discover(query, new ActivityBudget()), /PROVIDER_SCHEMA_INVALID/);
  const failed = provider({ mock: { fail: true } });
  await assert.rejects(() => failed.adapter.discover(query, new ActivityBudget()), { message: 'DISCOVERY_OPERATION_FAILED' });
  assert.equal(failed.transport.calls.length, 1);
});
test('no secret logs or accidental network/production write dependencies', async () => {
  const paths = ['discovery-contract.mjs', 'facebook-browser-adapter.mjs', 'socialapis-adapter.mjs'];
  for (const path of paths) {
    const source = await readFile(new URL(`../src/${path}`, import.meta.url), 'utf8');
    assert.doesNotMatch(source, /console\.|https\.request|writeFile|Portfolio\.|neon|gemini|gmail/iu);
    if (path !== 'socialapis-adapter.mjs') assert.doesNotMatch(source, /process\.env|\bfetch\s*\(/u);
    const imports = [...source.matchAll(/from ['"]([^'"]+)['"]/gu)].map(match => match[1]);
    assert.ok(imports.every(value => value.startsWith('./')));
  }
  const { adapter } = provider(); const oldFetch = globalThis.fetch;
  try { globalThis.fetch = () => { throw new Error('External network forbidden'); };
    assert.equal((await adapter.discover(query, new ActivityBudget())).length, 1);
  } finally { globalThis.fetch = oldFetch; }
});

function capturedPayload() {
  // Observed property structure only; no real post, contact or author data.
  const texts = ['Tuyển Fresher .NET tại Hà Nội', 'Tuyển Junior C# tại Hanoi',
    'Hà Nội công ty tìm Fresher Lập trình viên SQL C#.NET', 'Tuyển Intern ASP.NET Core tại Hà Nội',
    'HCM tuyển Fresher .NET. Hà Nội tuyển Intern chưa rõ stack.',
    'Em tìm job Fresher .NET ở Hà Nội, nhờ đánh giá CV.', 'Hà Nội tuyển Fresher Java', 'Hà Nội tuyển intern Python, lương NET 12 triệu'];
  return { data: { items: texts.map((post_text, i) => ({
    basic_info: { post_id: String(5001 + i), post_text, post_hashtags: [],
      url: `https://www.facebook.com/groups/fixture.jobs/permalink/${5001 + i}/` },
    feedback_details: {}, owner_info: {}, video_details: {}
  })), page_info: { cursor: 'SYNTHETIC_CURSOR_NEVER_FOLLOW' } }, message: 'OK', meta: { statusCode: 200 } };
}
async function replay(payload, budget = new ActivityBudget()) {
  const adapter = new SocialApisCaptureDiscoveryAdapter(payload, { budget });
  return runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
    reporter: new DiscoveryReporter({ evidenceKind: 'LOCAL_PROVIDER_CAPTURE_REPLAY' }), target: {}, budget,
    matcher: job => adapter.classify(job) });
}

test('Phase A capture, mocked live transport and safe smoke summary use identical vacancy matching', async () => withLiveKey(async () => {
  const payload = capturedPayload();
  payload.data.items[0].basic_info.post_text = 'Hà Nội\n1. QA Engineer Intern\nLương 8–12 triệu\n2. Fresher – Middle .NET Developer\nASP.NET Core, Angular\nUp to 35M\nInbox';
  let requests = 0;
  const capture = await replay(payload);
  const adapter = new SocialApisLiveDiscoveryAdapter(liveOptions, async () => {
    requests++; return new Response(JSON.stringify(payload), { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } });
  });
  const live = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
    reporter: new DiscoveryReporter({ evidenceKind: 'MOCK_PROVIDER_RESPONSE' }), target: {}, budget: new ActivityBudget(), matcher: job => adapter.classify(job) });
  const summary = summarizeSocialApisRows(payload.data.items);
  assert.equal(requests, 1); assert.equal(live.stopCode, null);
  for (const [i, job] of capture.jobs.entries()) {
    assert.deepEqual(live.jobs[i].vacancies, job.vacancies);
    assert.deepEqual(live.jobs[i].match, job.match);
    assert.equal(live.jobs[i].contentHash, job.contentHash);
    assert.equal(summary.candidates[i].classification, job.match.classification);
  }
  assert.equal(capture.jobs[0].title, null); assert.equal(capture.jobs[0].vacancies.length, 2);
  assert.equal(capture.metrics.relevantSourcePosts, 4); assert.equal(capture.metrics.relevantVacancies, 4);
  assert.equal(capture.metrics.unknownFreshness, 8); assert.equal(capture.metrics.structuredExtractionCoverage.withVacancies, 8);
  assert.equal(capture.metrics.emailCoverage.withEmail, 0);
  assert.doesNotMatch(JSON.stringify(summary), /8–12|35M|ASP.NET Core, Angular/u); // no raw JD copied into diagnostic summary
}));

test('Phase A state refresh distinguishes extraction upgrades, raw edits and accumulated historical posts', () => {
  const root = fileURLToPath(new URL('../.debug/', import.meta.url)); mkdirSync(root, { recursive: true });
  const directory = mkdtempSync(join(root, 'vacancy-state-'));
  const capture = join(directory, 'capture.json'); const output = join(directory, 'state.json');
  const cli = fileURLToPath(new URL('../src/cli.mjs', import.meta.url));
  // A subprocess transport guard proves replay cannot reach an external socket/fetch.
  const guard = 'data:text/javascript,' + encodeURIComponent(`import net from 'node:net';
    net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');};
    globalThis.fetch=()=>{throw new Error('FETCH_FORBIDDEN');};`);
  const run = () => JSON.parse(execFileSync(process.execPath, ['--import', guard, cli, '--source', 'socialapis', '--replay', capture, '--output', output], { encoding: 'utf8', stdio: 'pipe' }));
  try {
    const payload = capturedPayload(); writeFileSync(capture, JSON.stringify(payload));
    const firstRun = run(); assert.equal(firstRun.storage.inserted, 8);
    assert.equal(firstRun.queryYield.newSourcePosts, 8); assert.equal(firstRun.queryYield.newRelevantSourcePosts, 4);
    assert.equal(firstRun.queryYield.requestCount, 0); assert.equal(firstRun.queryYield.relevantVacanciesPerCredit, null);
    const old = JSON.parse(readFileSync(output, 'utf8')); const historical = structuredClone(old.jobs.slice(1));
    const originalHash = old.jobs[0].contentHash;
    delete old.jobs[0].extractionVersion; delete old.jobs[0].vacancies;
    writeFileSync(output, JSON.stringify(old));
    payload.data.items = payload.data.items.slice(0, 1); writeFileSync(capture, JSON.stringify(payload));
    const refresh = run();
    assert.equal(refresh.storage.extractionRefreshed, 1); assert.equal(refresh.storage.contentUpdated, 0);
    assert.equal(refresh.storage.updated, 1); assert.equal(refresh.storage.inserted, 0);
    assert.equal(refresh.queryYield.newSourcePosts, 0); assert.equal(refresh.queryYield.existingSourcePosts, 1);
    assert.equal(refresh.queryYield.newRelevantVacancies, 0);
    const refreshed = JSON.parse(readFileSync(output, 'utf8'));
    assert.equal(refreshed.jobs[0].contentHash, originalHash);
    assert.deepEqual(refreshed.jobs.slice(1), historical);
    assert.equal(refreshed.metricsScope, 'CURRENT_RUN'); assert.equal(refreshed.metrics.uniquePosts, 1);
    assert.equal(refreshed.accumulatedMetrics.uniquePosts, 8);
    assert.deepEqual(refreshed.processedSourceIdentities, ['SOCIALAPIS:5001']);
    assert.equal(run().storage.unchanged, 1);
    payload.data.items[0].basic_info.post_text += '\nYêu cầu: SQL Server';
    writeFileSync(capture, JSON.stringify(payload));
    const changed = run(); assert.equal(changed.storage.contentUpdated, 1); assert.equal(changed.storage.extractionRefreshed, 0);
    assert.equal(changed.queryYield.newSourcePosts, 0); assert.equal(changed.queryYield.newRelevantSourcePosts, 0);
    assert.equal(changed.storage.totalCandidates, 8); assert.equal(run().storage.unchanged, 1);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
test('SocialAPIs capture runs through real discovery/extraction/dedup/reporting without browser or transport', async () => {
  const oldFetch = globalThis.fetch;
  try {
    globalThis.fetch = () => { assert.fail('network is forbidden during replay'); };
    const payload = capturedPayload(); const report = await replay(payload);
    assert.equal(report.stopCode, null); assert.equal(report.jobs.length, 8);
    assert.equal(report.metrics.targetMatches, 4); assert.equal(report.metrics.insufficientData, 1); assert.equal(report.metrics.notMatched, 3);
    assert.equal(report.budget.peakSessions, 0); assert.equal(report.budget.searches, 1); assert.equal(report.budget.candidates, 8);
    assert.equal(report.evidenceKind, 'LOCAL_PROVIDER_CAPTURE_REPLAY'); assert.equal(report.gates.allowed, false);
    assert.equal(report.gates.browserTechnical, 'UNVERIFIED'); assert.equal(report.gates.offline, 'NOT_APPLICABLE_PROVIDER_DISCOVERY');
    for (const [i, job] of report.jobs.entries()) {
      assert.equal(job.body, payload.data.items[i].basic_info.post_text);
      assert.equal(job.sourceUrl, payload.data.items[i].basic_info.url);
      assert.equal(job.publishedAt, null); assert.equal(job.published_at, null);
      assert.equal(job.match.freshness, 'UNKNOWN'); assert.equal(job.sourceIdentity, `SOCIALAPIS:${job.id}`);
      assert.equal(job.provenance.source, 'FACEBOOK'); assert.match(job.contentHash, /^[0-9a-f]{64}$/u);
    }
  } finally { globalThis.fetch = oldFetch; }
});

const liveOptions = { enabled: true, freePlanConfirmed: true, noCashConfirmed: true };

for (const filters of [{}, { startTime: '2026-10-01', endTime: '2026-10-07' },
  { startTime: '2024-02-29' }, { endTime: '2026-10-07' }, { startTime: '2000-02-29', endTime: '2000-02-29' }]) {
  test(`B.0 mock live date filters preserve URL and unknown publication: ${JSON.stringify(filters)}`, async () => withLiveKey(async () => {
    let calls = 0; let requested;
    const adapter = new SocialApisLiveDiscoveryAdapter({ ...liveOptions, ...filters }, async url => {
      calls++; requested = new URL(url);
      return new Response(JSON.stringify(mixedPayload()), { headers: { 'X-SocialAPIs-Credits-Deducted': '1', 'X-SocialAPIs-Credits-Used': '6' } });
    });
    const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
      reporter: new DiscoveryReporter({ evidenceKind: 'MOCK_PROVIDER_RESPONSE' }), target: {}, budget: new ActivityBudget(), matcher: job => adapter.classify(job) });
    assert.equal(report.stopCode, null); assert.equal(calls, 1); assert.equal(requested.searchParams.get('recent_posts'), 'true');
    assert.equal(requested.searchParams.get('start_time'), filters.startTime ?? null);
    assert.equal(requested.searchParams.get('end_time'), filters.endTime ?? null);
    if (!Object.keys(filters).length) assert.equal(requested.href, 'https://api.socialapis.io/facebook/search/posts?query=tuy%E1%BB%83n+fresher+.NET+H%C3%A0+N%E1%BB%99i&recent_posts=true');
    assert.equal(requested.searchParams.size, 2 + Object.keys(filters).length);
    assert.equal(report.itemStats.outcome, 'PARTIAL_SUCCESS'); assert.deepEqual(report.itemStats.rejectionReasons, mixedReasons);
    assert.ok(report.jobs.every(job => job.publishedAt === null && job.published_at === null && job.match.freshness === 'UNKNOWN'));
    const yieldReport = buildDiscoveryYield({ jobs: report.jobs, query: adapter.query, requestStats: adapter.requestStats,
      itemStats: adapter.itemStats, dateFilters: adapter.dateFilters, mode: 'MOCK_TRANSPORT' });
    assert.equal(yieldReport.creditsDeducted, 1); assert.equal(yieldReport.creditsUsedAlternateHeader, 6);
    assert.equal(yieldReport.verifiedRecentRelevantVacancies, 0); assert.equal(yieldReport.freshnessUnknownCount, 2);
    assert.equal(yieldReport.requestMetadata.providerDateFilterBehavior, 'UNVERIFIED');
    assert.equal(yieldReport.requestMetadata.providerSearchPerformed, false);
    await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()), /REQUEST_BUDGET_EXCEEDED/u);
    assert.equal(calls, 1);
  }));
}

for (const invalid of ['2026-2-01', '2026-02-30', '2025-02-29', '1900-02-29', '2026-04-31',
  '2026-00-10', '2026-13-01', '2026-01-00', '0000-01-01', '2026-01-01T00:00:00Z', '', ' 2026-01-01', '2026-01-01\n', '2026-01-01\t', 20260101]) {
  test(`B.0 invalid calendar input is rejected before transport: ${JSON.stringify(invalid)}`, () => {
    let calls = 0;
    for (const side of ['startTime', 'endTime']) assert.throws(() => new SocialApisLiveDiscoveryAdapter({ ...liveOptions, [side]: invalid }, () => { calls++; }), /INVALID_DATE_FILTER/u);
    assert.equal(calls, 0);
  });
}
test('B.0 inverted range is rejected before transport; omitted filters stay optional', () => {
  let calls = 0;
  assert.throws(() => new SocialApisLiveDiscoveryAdapter({ ...liveOptions, startTime: '2026-10-08', endTime: '2026-10-07' }, () => { calls++; }), /INVALID_DATE_RANGE/u);
  assert.equal(calls, 0); assert.deepEqual(validateDateFilters(), { startTime: null, endTime: null });
});

test('B.0 a provider timestamp stays UNVERIFIED even inside the requested window; fractional actual credits are used', async () => withLiveKey(async () => {
  const adapter = new SocialApisLiveDiscoveryAdapter({ ...liveOptions, startTime: '2026-10-01', endTime: '2026-10-07' }, async () =>
    new Response(JSON.stringify({ data: { results: [{ post_id: '5001', text: 'Hiring Junior .NET Developer in Hanoi',
      created_time: Date.parse('2026-10-05T00:00:00Z') / 1000, url: 'https://www.facebook.com/fixture/posts/5001' }] } }),
    { headers: { 'X-SocialAPIs-Credits-Deducted': '0.5', 'X-SocialAPIs-Credits-Used': '6' } }));
  const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
    reporter: new DiscoveryReporter({ evidenceKind: 'MOCK_PROVIDER_RESPONSE' }), target: {}, budget: new ActivityBudget(), matcher: job => adapter.classify(job) });
  assert.equal(report.stopCode, null); assert.equal(report.jobs[0].publishedAt, '2026-10-05T00:00:00.000Z');
  assert.equal(report.jobs[0].match.freshness, 'UNVERIFIED');
  const metrics = buildDiscoveryYield({ jobs: report.jobs, query: adapter.query, itemStats: adapter.itemStats,
    requestStats: adapter.requestStats, dateFilters: adapter.dateFilters, mode: 'MOCK_TRANSPORT', now: new Date('2026-10-07T00:00:00Z') });
  assert.equal(metrics.requestMetadata.publicationDates.timestampPresentUnverified, 1);
  assert.equal(metrics.verifiedRecentRelevantVacancies, 0); assert.equal(metrics.relevantVacanciesPerCredit, 2);
  assert.equal(metrics.creditsDeducted, 0.5); assert.equal(metrics.creditsUsedAlternateHeader, 6);
}));

test('B.0 yield separates distinct posts, vacancies, registry duplicates, timestamps and shared contacts', async () => {
  const payload = capturedPayload(); payload.data.items = payload.data.items.slice(0, 1);
  payload.data.items[0].basic_info.post_text = 'Hanoi\n1. Junior .NET Developer\n2. Intern C# Developer\nEmail: jobs@example.invalid';
  const report = await replay(payload); const job = report.jobs[0];
  const options = { jobs: report.jobs, query: 'synthetic-query', itemStats: { totalItems: 3, acceptedItems: 2, rejectedItems: 1 },
    requestStats: { requestCount: 1, creditsDeducted: 0.5, creditsUsedAlternateHeader: 6 },
    dateFilters: { startTime: '2026-10-01', endTime: '2026-10-07' }, mode: 'MOCK_TRANSPORT', now: new Date('2026-10-07T00:00:00Z') };
  const first = buildDiscoveryYield(options);
  assert.equal(first.newSourcePosts, 1); assert.equal(first.relevantSourcePosts, 1); assert.equal(first.newRelevantSourcePosts, 1);
  assert.equal(first.relevantVacancies, 2); assert.equal(first.newRelevantVacancies, 2);
  assert.equal(first.duplicateRate, 0.5); assert.equal(first.relevantVacanciesWithContactEmail, 2);
  assert.equal(first.relevantVacanciesPerCredit, 4); assert.equal(first.newRelevantVacanciesPerCredit, 4);
  assert.equal(first.verifiedRecentVacanciesPerCredit, 0); assert.equal(first.creditsUsedAlternateHeader, 6);
  assert.equal(first.verifiedRecentRelevantVacancies, 0); assert.equal(first.freshnessUnknownCount, 1);
  assert.equal(first.requestMetadata.publicationDates.unknown, 1);
  const second = buildDiscoveryYield({ ...options, existingJobs: [job, { sourceIdentity: 'SOCIALAPIS:historical' }] });
  assert.equal(second.newSourcePosts, 0); assert.equal(second.existingSourcePosts, 1); assert.equal(second.duplicateRate, 1);
  assert.equal(second.newRelevantSourcePosts, 0); assert.equal(second.newRelevantVacancies, 0); assert.equal(second.relevantSourcePosts, 1);
  for (const cost of [null, 0]) {
    const unknown = buildDiscoveryYield({ ...options, requestStats: { creditsDeducted: cost } });
    assert.equal(unknown.relevantVacanciesPerCredit, null); assert.equal(unknown.newRelevantVacanciesPerCredit, null);
    assert.equal(unknown.verifiedRecentVacanciesPerCredit, null);
  }
  const timestamped = { ...job, publishedAt: '2026-10-06T00:00:00Z', match: { ...job.match, freshness: 'UNVERIFIED' } };
  assert.equal(buildDiscoveryYield({ ...options, jobs: [timestamped] }).verifiedRecentRelevantVacancies, 0);
  const verified = { ...timestamped, match: { ...job.match, freshness: 'RECENT_7D' } };
  assert.equal(buildDiscoveryYield({ ...options, jobs: [verified] }).verifiedRecentRelevantVacancies, 2);
  assert.equal(buildDiscoveryYield({ ...options, jobs: [{ ...verified, publishedAt: '2026-10-08T00:00:00Z' }] }).verifiedRecentRelevantVacancies, 0);
  const privateToRole = structuredClone(job); privateToRole.postSections.sharedFooter = null;
  privateToRole.vacancies[0].contactEmail = privateToRole.contactEmail;
  assert.equal(buildDiscoveryYield({ ...options, jobs: [privateToRole] }).relevantVacanciesWithContactEmail, 1);
  const empty = buildDiscoveryYield({ jobs: [], query: 'synthetic-query' });
  assert.equal(empty.newSourcePosts, 0); assert.equal(empty.duplicateRate, null); assert.equal(empty.relevantVacanciesPerCredit, null);
});

test('B.0 CLI forwards dates to actual live adapter under a mock, preserves state, and validates before IO', () => {
  const root = fileURLToPath(new URL('../.debug/', import.meta.url)); mkdirSync(root, { recursive: true });
  const directory = mkdtempSync(join(root, 'b0-cli-')); const output = join(directory, 'state.json');
  const cli = fileURLToPath(new URL('../src/cli.mjs', import.meta.url));
  const mock = 'data:text/javascript,' + encodeURIComponent(`import assert from 'node:assert/strict'; import net from 'node:net';
    net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');}; let calls=0;
    globalThis.fetch=async input=>{ assert.equal(++calls,1); const url=new URL(input);
      assert.equal(url.searchParams.get('start_time'),'2026-10-01'); assert.equal(url.searchParams.get('end_time'),'2026-10-07');
      return new Response(${JSON.stringify(JSON.stringify(capturedPayload()))},{headers:{'X-SocialAPIs-Credits-Deducted':'1','X-SocialAPIs-Credits-Used':'6'}}); };`);
  const env = { ...process.env, SOCIALAPIS_API_KEY: 'synthetic-b0-cli-key-not-real', SOCIALAPIS_LIVE_ENABLED: 'true',
    SOCIALAPIS_FREE_PLAN_CONFIRMED: 'true', SOCIALAPIS_NO_CASH_CONFIRMED: 'true' };
  const run = extra => JSON.parse(execFileSync(process.execPath, ['--import', mock, cli, '--source', 'socialapis', '--output', output, ...extra], { encoding: 'utf8', env, stdio: 'pipe' }));
  try {
    run(['--prepare-state', '--initialize-state']);
    const args = ['--live', '--start-time', '2026-10-01', '--end-time', '2026-10-07'];
    const first = run(args); assert.equal(first.queryYield.newSourcePosts, 8); assert.equal(first.queryYield.newRelevantVacancies, 4);
    assert.equal(first.queryYield.freshnessUnknownCount, 8); assert.equal(first.queryYield.verifiedRecentRelevantVacancies, 0);
    assert.equal(first.queryYield.requestMetadata.startTime, '2026-10-01');
    assert.equal(first.queryYield.requestMetadata.providerDateFilterBehavior, 'UNVERIFIED');
    const second = run(args); assert.equal(second.queryYield.newSourcePosts, 0); assert.equal(second.queryYield.existingSourcePosts, 8);
    assert.equal(second.queryYield.newRelevantVacanciesPerCredit, 0); assert.equal(second.storage.unchanged, 8);
    const unchanged = readFileSync(output, 'utf8');
    assert.throws(() => run(['--live', '--start-time', '2026-02-30']), error => error.stderr.toString().includes('INVALID_DATE_FILTER'));
    assert.equal(readFileSync(output, 'utf8'), unchanged);
    const state = JSON.parse(unchanged); assert.ok(state.jobs.every(j => j.publishedAt === null && j.match.freshness === 'UNKNOWN'));
    assert.doesNotMatch(unchanged, /synthetic-b0-cli-key-not-real/u);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
async function withLiveKey(run) {
  const previous = process.env.SOCIALAPIS_API_KEY;
  process.env.SOCIALAPIS_API_KEY = 'synthetic-live-transport-test-key';
  try { return await run(); } finally {
    if (previous === undefined) delete process.env.SOCIALAPIS_API_KEY; else process.env.SOCIALAPIS_API_KEY = previous;
  }
}
test('live adapter gates, missing key and all invalid budgets reject before transport', async () => withLiveKey(async () => {
  let calls = 0; const transport = () => { calls++; assert.fail('must not reach transport'); };
  for (const options of [{}, { ...liveOptions, enabled: false }, { ...liveOptions, freePlanConfirmed: false },
    { ...liveOptions, noCashConfirmed: false }, { ...liveOptions, maxRequests: 0 }, { ...liveOptions, maxRequests: 2 },
    { ...liveOptions, maxCredits: 0 }, { ...liveOptions, maxCredits: 2 }, { ...liveOptions, timeoutMs: 20_001 }]) {
    const adapter = new SocialApisLiveDiscoveryAdapter(options, transport);
    await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()));
  }
  delete process.env.SOCIALAPIS_API_KEY;
  const adapter = new SocialApisLiveDiscoveryAdapter(liveOptions, transport);
  await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()), /SOCIALAPIS_API_KEY_REQUIRED/u);
  assert.equal(calls, 0);
}));
test('mock live transport executes the real pipeline once, keeps separate credits, and never follows cursor/redirect', async () => withLiveKey(async () => {
  const calls = [];
  const adapter = new SocialApisLiveDiscoveryAdapter(liveOptions, async (url, options) => {
    calls.push({ url, options });
    return new Response(JSON.stringify(capturedPayload()), { status: 200,
      headers: { 'X-SocialAPIs-Credits-Deducted': '1', 'X-SocialAPIs-Credits-Used': '6' } });
  });
  const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
    reporter: new DiscoveryReporter({ evidenceKind: 'MOCK_PROVIDER_RESPONSE' }), target: {}, budget: new ActivityBudget(), matcher: job => adapter.classify(job) });
  assert.equal(report.stopCode, null); assert.equal(report.jobs.length, 8); assert.equal(report.metrics.targetMatches, 4);
  assert.equal(report.metrics.insufficientData, 1); assert.equal(report.metrics.notMatched, 3);
  assert.equal(calls.length, 1); assert.equal(calls[0].options.redirect, 'manual'); assert.equal(calls[0].options.credentials, 'omit');
  const url = new URL(calls[0].url);
  assert.equal(url.href, 'https://api.socialapis.io/facebook/search/posts?query=tuy%E1%BB%83n+fresher+.NET+H%C3%A0+N%E1%BB%99i&recent_posts=true');
  assert.equal(calls[0].options.headers['x-api-token'], process.env.SOCIALAPIS_API_KEY);
  assert.equal(adapter.requestStats.creditsDeducted, 1); assert.equal(adapter.requestStats.creditsUsedAlternateHeader, 6);
  assert.ok(report.jobs.every(job => job.evidenceKind === 'MOCK_PROVIDER_RESPONSE' && job.publishedAt === null));
  assert.doesNotMatch(JSON.stringify(report), /synthetic-live-transport-test-key/u);
  await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()), /REQUEST_BUDGET_EXCEEDED/u);
  assert.equal(calls.length, 1);
}));
test('live HTTP/schema/credit/network/timeout failures never retry and never leak private exceptions', async () => withLiveKey(async () => {
  for (const transport of [async () => new Response('{}', { status: 429 }), async () => new Response('{}', { status: 302 }),
    async () => new Response('{invalid', { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } }),
    async () => new Response(JSON.stringify(capturedPayload()), { headers: { 'X-SocialAPIs-Credits-Deducted': '2' } }),
    async () => new Response(JSON.stringify({ error: 'SYNTHETIC_PROVIDER_ERROR' }), { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } }),
    async () => new Response(JSON.stringify({ message: process.env.SOCIALAPIS_API_KEY }), { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } }),
    async () => new Response(JSON.stringify(capturedPayload())),
    async () => { throw new Error('PRIVATE_PROVIDER_DETAIL'); }, async () => new Promise(() => {})]) {
    let calls = 0;
    const adapter = new SocialApisLiveDiscoveryAdapter({ ...liveOptions, timeoutMs: 10 }, (...args) => { calls++; return transport(...args); });
    await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()), error => !error.message.includes('PRIVATE_PROVIDER_DETAIL'));
    await assert.rejects(() => adapter.search(undefined, adapter.query, new ActivityBudget()), /REQUEST_BUDGET_EXCEEDED/u);
    assert.equal(calls, 1);
  }
}));

test('concurrent searches on one live adapter claim exactly one request', async () => withLiveKey(async () => {
  let calls = 0;
  const adapter = new SocialApisLiveDiscoveryAdapter(liveOptions, async () => {
    calls++;
    return new Response(JSON.stringify(capturedPayload()), { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } });
  });
  const results = await Promise.allSettled([adapter.search(undefined, adapter.query, new ActivityBudget()), adapter.search(undefined, adapter.query, new ActivityBudget())]);
  assert.equal(calls, 1); assert.equal(results.filter(result => result.status === 'fulfilled').length, 1);
  assert.equal(results.find(result => result.status === 'rejected').reason.code, 'REQUEST_BUDGET_EXCEEDED');
}));

test('CLI mock-live restores state on a fresh simulated runner and refuses missing state before transport', () => {
  const root = fileURLToPath(new URL('../.debug/', import.meta.url)); mkdirSync(root, { recursive: true });
  const directory = mkdtempSync(join(root, 'live-integration-'));
  const firstOutput = join(directory, 'run-one', 'state.json'); const nextOutput = join(directory, 'run-two', 'state.json');
  const cli = fileURLToPath(new URL('../src/cli.mjs', import.meta.url));
  const fake = 'data:text/javascript,' + encodeURIComponent(`import net from 'node:net';
    net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');};
    globalThis.fetch=async()=>new Response(${JSON.stringify(JSON.stringify(capturedPayload()))}, {headers:{'X-SocialAPIs-Credits-Deducted':'1'}});`);
  const env = { ...process.env, SOCIALAPIS_API_KEY: 'synthetic-live-cli-test-key', SOCIALAPIS_LIVE_ENABLED: 'true',
    SOCIALAPIS_FREE_PLAN_CONFIRMED: 'true', SOCIALAPIS_NO_CASH_CONFIRMED: 'true' };
  const run = (output, flags) => JSON.parse(execFileSync(process.execPath, ['--import', fake, cli, '--source', 'socialapis', '--output', output, ...flags], { encoding: 'utf8', env, stdio: 'pipe' }));
  try {
    assert.throws(() => run(firstOutput, ['--live']), error => error.stderr.toString().includes('DISCOVERY_STATE_REQUIRED'));
    assert.equal(run(firstOutput, ['--prepare-state', '--initialize-state']).totalCandidates, 0);
    assert.equal(run(firstOutput, ['--live']).storage.inserted, 8);
    // Simulate artifact restore onto an entirely new runner filesystem.
    mkdirSync(join(directory, 'run-two'));
    writeFileSync(nextOutput, readFileSync(firstOutput));
    const restored = run(nextOutput, ['--live']);
    assert.equal(restored.storage.inserted, 0); assert.equal(restored.storage.unchanged, 8);
    assert.equal(restored.storage.totalCandidates, 8); assert.equal(restored.live.networkRequests, 1);
    const state = JSON.parse(readFileSync(nextOutput, 'utf8'));
    assert.equal(state.jobs.length, 8); assert.equal(state.metrics.targetMatches, 4);
    assert.ok(state.jobs.every(job => job.published_at === null && job.sourceUrl.includes('/permalink/')));
    assert.doesNotMatch(JSON.stringify(state), /synthetic-live-cli-test-key/u);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
test('capture preserves distinct post IDs with identical text and dedups repeat identity', async () => {
  const payload = capturedPayload();
  payload.data.items = [payload.data.items[0], structuredClone(payload.data.items[0]),
    { ...structuredClone(payload.data.items[0]), basic_info: { ...payload.data.items[0].basic_info, post_id: '6001',
      url: 'https://www.facebook.com/groups/fixture.jobs/permalink/6001/' } }];
  const report = await replay(payload);
  assert.equal(report.jobs.length, 2); assert.equal(report.jobs[0].contentHash, report.jobs[1].contentHash);
  const adapter = new SocialApisCaptureDiscoveryAdapter(payload);
  await assert.rejects(() => adapter.search(undefined, 'different query', new ActivityBudget()), /CAPTURE_QUERY_MISMATCH/u);
  payload.data.items[0].basic_info.url = 'https://example.invalid/not-facebook';
  const rejected = new SocialApisCaptureDiscoveryAdapter(payload);
  assert.equal(rejected.itemStats.rejectedItems, 1);
  assert.deepEqual(rejected.itemStats.rejectionReasons, [{ index: 0, reason: 'POST_URL_INVALID_OR_UNSUPPORTED' }]);
  assert.equal(rejected.records.has('https://example.invalid/not-facebook'), false);
  assert.throws(() => new SocialApisCaptureDiscoveryAdapter({ error: 'denied' }), /PROVIDER_ERROR/u);
  const limited = await replay(capturedPayload(), new ActivityBudget({ candidates: 1 }));
  assert.equal(limited.stopCode, 'CANDIDATES_BUDGET_EXCEEDED');
});

function mixedPayload() {
  const payload = capturedPayload(); const first = payload.data.items[0];
  const changed = fields => ({ ...first, basic_info: { ...first.basic_info, ...fields } });
  payload.data.items = [first, null, changed({ post_id: undefined }), changed({ post_id: 123 }),
    changed({ url: undefined }), changed({ url: 'https://example.invalid/PRIVATE_INVALID_URL' }),
    changed({ post_id: '9999' }), changed({ post_text: 'x'.repeat(24_001) }), payload.data.items[1]];
  return payload;
}
const mixedReasons = [
  { index: 1, reason: 'ITEM_NOT_OBJECT' }, { index: 2, reason: 'POST_ID_MISSING' },
  { index: 3, reason: 'POST_ID_INVALID' }, { index: 4, reason: 'POST_URL_MISSING' },
  { index: 5, reason: 'POST_URL_INVALID_OR_UNSUPPORTED' }, { index: 6, reason: 'POST_ID_URL_MISMATCH' },
  { index: 7, reason: 'POST_TEXT_TOO_LARGE' }
];
test('mixed batch rejects malformed items individually and preserves later valid posts with exact reasons', async () => {
  const report = await replay(mixedPayload());
  assert.equal(report.stopCode, null); assert.deepEqual(report.jobs.map(job => job.id), ['5001', '5002']);
  assert.deepEqual(report.itemStats, { totalItems: 9, acceptedItems: 2, rejectedItems: 7,
    rejectionReasons: mixedReasons, outcome: 'PARTIAL_SUCCESS' });
  assert.equal(report.budget.candidates, 2);
  // Wall-clock decimals can contain "9999"; they are not rejected provider data.
  assert.doesNotMatch(JSON.stringify(report, (key, value) => key === 'elapsedMs' ? 0 : value), /PRIVATE_INVALID_URL|9999/u);
  assert.equal(report.jobs[0].body, mixedPayload().data.items[0].basic_info.post_text);
  // Already-supported Page and story.php variants remain valid alongside Group
  // permalinks; no new URL grammar is introduced for partial recovery.
  const variants = capturedPayload();
  variants.data.items[0].basic_info.url = 'https://www.facebook.com/fixture.page/posts/5001';
  variants.data.items[1] = { post_id: '5002', text: 'Tuyển Junior C# tại Hà Nội',
    url: 'https://m.facebook.com/story.php?story_fbid=5002&id=9000' };
  const variantReport = await replay(variants);
  assert.equal(variantReport.stopCode, null); assert.equal(variantReport.itemStats.acceptedItems, 8);
  assert.equal(variantReport.itemStats.rejectedItems, 0);
});
test('mock live mixed batch uses exactly the same strict per-item normalization and recovery', async () => withLiveKey(async () => {
  let calls = 0;
  const adapter = new SocialApisLiveDiscoveryAdapter(liveOptions, async () => {
    calls++; return new Response(JSON.stringify(mixedPayload()), { headers: { 'X-SocialAPIs-Credits-Deducted': '1' } });
  });
  const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] }, extractor: new JobPostExtractor(),
    reporter: new DiscoveryReporter({ evidenceKind: 'MOCK_PROVIDER_RESPONSE' }), target: {}, budget: new ActivityBudget(), matcher: job => adapter.classify(job) });
  assert.equal(calls, 1); assert.equal(report.stopCode, null); assert.equal(report.jobs.length, 2);
  assert.equal(report.itemStats.outcome, 'PARTIAL_SUCCESS'); assert.deepEqual(report.itemStats.rejectionReasons, mixedReasons);
}));
test('all-invalid is an explicit failure, empty results are distinct, and response errors remain fatal', async () => {
  const invalid = capturedPayload(); invalid.data.items = [null, { basic_info: { post_id: '5001', url: 'https://example.invalid/no' } }];
  const report = await replay(invalid);
  assert.equal(report.stopCode, 'NO_VALID_PROVIDER_ITEMS'); assert.equal(report.jobs.length, 0);
  assert.equal(report.itemStats.outcome, 'NO_VALID_ITEMS'); assert.equal(report.itemStats.acceptedItems, 0);
  assert.equal(report.itemStats.rejectedItems, 2);
  const empty = await replay({ data: { items: [] } });
  assert.equal(empty.stopCode, null); assert.equal(empty.itemStats.outcome, 'EMPTY_RESULTS');
  assert.throws(() => new SocialApisCaptureDiscoveryAdapter({ data: { items: {} } }), /SCHEMA_MISMATCH/u);
  assert.throws(() => new SocialApisCaptureDiscoveryAdapter({ error: 'PRIVATE_ERROR', data: { items: [] } }), /PROVIDER_ERROR/u);
  const oversized = capturedPayload(); oversized.data.items = Array(201).fill(null);
  assert.throws(() => new SocialApisCaptureDiscoveryAdapter(oversized), /RESULT_COUNT_LIMIT_EXCEEDED/u);
  let now = 0; const budget = new ActivityBudget({ durationMs: 1 }, () => now); now = 1;
  assert.throws(() => new SocialApisCaptureDiscoveryAdapter(mixedPayload(), { budget }), /TIME_BUDGET_EXCEEDED/u);
  const capped = await replay(mixedPayload(), new ActivityBudget({ candidates: 1 }));
  assert.equal(capped.stopCode, 'CANDIDATES_BUDGET_EXCEEDED'); assert.equal(capped.budget.candidates, 1);
});
test('mixed replay persists valid candidates only; all-invalid failure preserves checkpoint byte-for-byte', () => {
  const root = fileURLToPath(new URL('../.debug/', import.meta.url)); mkdirSync(root, { recursive: true });
  const directory = mkdtempSync(join(root, 'partial-integration-'));
  const capture = join(directory, 'capture.json'); const output = join(directory, 'state.json');
  const cli = fileURLToPath(new URL('../src/cli.mjs', import.meta.url));
  const noNetwork = 'data:text/javascript,' + encodeURIComponent("import net from 'node:net'; globalThis.fetch=()=>{throw new Error('NETWORK_FORBIDDEN');}; net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');};");
  const run = () => execFileSync(process.execPath, ['--import', noNetwork, cli, '--source', 'socialapis', '--replay', capture, '--output', output], { encoding: 'utf8', stdio: 'pipe' });
  try {
    writeFileSync(capture, JSON.stringify(mixedPayload()), 'utf8');
    const result = JSON.parse(run()); assert.equal(result.itemStats.rejectedItems, 7);
    const persisted = readFileSync(output, 'utf8'); const state = JSON.parse(persisted);
    assert.deepEqual(state.jobs.map(job => job.id), ['5001', '5002']);
    assert.equal(state.itemStats.acceptedItems, 2); assert.doesNotMatch(persisted, /PRIVATE_INVALID_URL/u);
    assert.equal(JSON.parse(run()).storage.inserted, 0);
    const beforeFailure = readFileSync(output, 'utf8');
    writeFileSync(capture, JSON.stringify({ data: { items: [null] } }), 'utf8');
    assert.throws(run, error => {
      assert.equal(error.status, 1); assert.match(error.stderr.toString(), /NO_VALID_PROVIDER_ITEMS/u);
      assert.equal(JSON.parse(error.stdout).itemStats.outcome, 'NO_VALID_ITEMS'); return true;
    });
    assert.equal(readFileSync(output, 'utf8'), beforeFailure);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
test('existing CLI replays twice into private local store with no duplicate inserts and no network', () => {
  const privateDirectory = fileURLToPath(new URL('../.debug/', import.meta.url));
  mkdirSync(privateDirectory, { recursive: true });
  const directory = mkdtempSync(join(privateDirectory, 'integration-test-'));
  const capture = join(directory, 'capture.json'); const output = join(directory, 'candidates.json');
  const cli = fileURLToPath(new URL('../src/cli.mjs', import.meta.url));
  const noNetwork = 'data:text/javascript,' + encodeURIComponent("import net from 'node:net'; globalThis.fetch=()=>{throw new Error('NETWORK_FORBIDDEN');}; net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');};");
  const run = () => JSON.parse(execFileSync(process.execPath, ['--import', noNetwork, cli, '--source', 'socialapis', '--replay', capture, '--output', output], { encoding: 'utf8' }));
  try {
    const raw = JSON.stringify(capturedPayload()); writeFileSync(capture, raw, 'utf8');
    const first = run(); const second = run();
    assert.equal(first.storage.inserted, 8); assert.equal(second.storage.inserted, 0);
    assert.equal(second.storage.updated, 0); assert.equal(second.storage.unchanged, 8);
    assert.equal(second.storage.totalCandidates, 8); assert.equal(second.replay.processedItems, 8);
    assert.equal(second.metrics.targetMatches, 4); assert.equal(second.metrics.insufficientData, 1); assert.equal(second.metrics.notMatched, 3);
    assert.equal(second.replay.networkRequests, 0); assert.equal(second.replay.pagination.cursorPresent, true);
    assert.equal(second.replay.pagination.followed, false);
    assert.equal(JSON.parse(readFileSync(output, 'utf8')).jobs.length, 8);
    assert.equal(readFileSync(capture, 'utf8'), raw);
    const changed = capturedPayload(); changed.data.items[0].basic_info.post_text += ' Updated vacancy.';
    writeFileSync(capture, JSON.stringify(changed), 'utf8');
    assert.equal(run().storage.updated, 1); assert.equal(run().storage.inserted, 0);
    assert.throws(() => execFileSync(process.execPath, [cli, '--source', 'socialapis', '--replay', capture, '--output', capture], { stdio: 'pipe' }),
      error => error.stderr.toString().includes('PRIVATE_REPLAY_OUTPUT_REQUIRED'));
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
