import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { ActivityBudget, facebookAccessGate } from '../src/core.mjs';
import { buildFacebookSearchUrl, canonicalFacebookPost } from '../src/discovery-contract.mjs';
import { FacebookBrowserDiscoveryAdapter } from '../src/facebook-browser-adapter.mjs';
import { SocialApisHubHttpAdapter, createMockSocialApisTransport, PROVIDER_SCHEMA_STATUS } from '../src/socialapis-adapter.mjs';

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
    assert.doesNotMatch(source, /console\.|process\.env|\bfetch\s*\(|https\.request|writeFile|Portfolio\.|neon|gemini|gmail/iu);
    const imports = [...source.matchAll(/from ['"]([^'"]+)['"]/gu)].map(match => match[1]);
    assert.ok(imports.every(value => value.startsWith('./')));
  }
  const { adapter } = provider(); const oldFetch = globalThis.fetch;
  try { globalThis.fetch = () => { throw new Error('External network forbidden'); };
    assert.equal((await adapter.discover(query, new ActivityBudget())).length, 1);
  } finally { globalThis.fetch = oldFetch; }
});
