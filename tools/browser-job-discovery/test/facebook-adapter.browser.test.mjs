import test from 'node:test';
import assert from 'node:assert/strict';
import { ActivityBudget } from '../src/core.mjs';
import { FixtureBrowserSessionProvider, startFixtureServer } from '../src/browser.mjs';
import { FacebookBrowserDiscoveryAdapter } from '../src/facebook-browser-adapter.mjs';

test('V2: real leased Chromium extracts canonical Facebook anchors from loopback fixture, without navigating to Facebook', async () => {
  const server = await startFixtureServer(); const budget = new ActivityBudget();
  const provider = new FixtureBrowserSessionProvider(server.origin); let session;
  try {
    session = await provider.open(budget, new AbortController().signal);
    const adapter = new FacebookBrowserDiscoveryAdapter({ fixtureOrigin: server.origin });
    const query = 'C# .NET Angular Junior Hà Nội';
    const rows = await adapter.discover(session.page, query, budget);
    assert.equal(rows.length, 3);
    assert.deepEqual(rows.map(row => row.source_post_id), ['1001', '1002', 'pfbidExample123']);
    assert.ok(rows.every(row => row.authorization_status === 'OFFLINE_FIXTURE_ONLY' && row.evidence.kind === 'SYNTHETIC_FIXTURE'));
    assert.ok(rows.every(row => !row.candidate_post_url.includes('search') && !row.candidate_post_url.includes('?')));
    assert.equal(new URL(session.page.url()).origin, server.origin);
    assert.equal(new URL(session.page.url()).searchParams.get('q'), query);
    assert.ok(server.requests.every(url => url.startsWith('/facebook-search')));
    assert.equal(provider.blockedRequests, 0); assert.equal(budget.snapshot().peakSessions, 1);
    assert.equal(budget.snapshot().searches, 1); assert.equal(budget.snapshot().candidates, 3);
    assert.doesNotMatch(JSON.stringify(rows), /access_token|refresh_token|storageState|password/iu);
  } finally { await session?.close(); await server.close(); }
  assert.equal(budget.snapshot().sessions, 0);
});
