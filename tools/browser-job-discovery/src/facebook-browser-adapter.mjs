import { Stop } from './core.mjs';
import { FixtureBrowserDiscoveryAdapter } from './browser.mjs';
import { validateQuery, buildFacebookSearchUrl, canonicalFacebookPost, candidateRecord, boundedOperation } from './discovery-contract.mjs';

// Discovery stage only. Reuses the leased Page; never creates a browser/session.
// Reference: kevinzg's search -> permalink strategy, not copied selectors/code.
export class FacebookBrowserDiscoveryAdapter {
  #fixtureOrigin; #navigator; #timeoutMs;
  constructor({ fixtureOrigin, mode = 'offline', timeoutMs = 5000 } = {}) {
    if (mode !== 'offline') throw new Stop('FACEBOOK_LIVE_COLLECTION_DISABLED');
    let origin;
    try { origin = new URL(fixtureOrigin); } catch { throw new Stop('INVALID_FIXTURE_ORIGIN'); }
    if (origin.protocol !== 'http:' || origin.hostname !== '127.0.0.1' || origin.username || origin.password ||
        origin.pathname !== '/' || origin.search || origin.hash) throw new Stop('INVALID_FIXTURE_ORIGIN');
    this.#fixtureOrigin = origin.origin;
    this.#navigator = new FixtureBrowserDiscoveryAdapter(origin.origin);
    this.#timeoutMs = timeoutMs;
  }
  async discover(page, rawQuery, budget) {
    const query = validateQuery(rawQuery);
    budget.consume('searches');
    const searchUrl = buildFacebookSearchUrl(query);
    const fixtureUrl = new URL('/facebook-search', this.#fixtureOrigin);
    fixtureUrl.search = new URLSearchParams({ q: query }).toString();
    const records = await boundedOperation(async (signal, timeout) => {
      // Explicit native navigation timeout also cancels Playwright navigation.
      const response = await page.goto(fixtureUrl.href, { waitUntil: 'domcontentloaded', timeout });
      if (signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
      budget.check();
      if (new URL(page.url()).origin !== this.#fixtureOrigin) throw new Stop('EXTERNAL_NAVIGATION_BLOCKED');
      await this.#navigator.checkAccess(page);
      if (signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
      if (!response?.ok()) throw new Stop('NAVIGATION_FAILED');
      const snapshot = await page.evaluate(() => ({ marker: document.body.dataset.evidence,
        anchors: Array.from(document.querySelectorAll('a[href]')).slice(0, 200).map(a => a.getAttribute('href')) }));
      if (signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
      if (snapshot.marker !== 'synthetic') throw new Stop('FIXTURE_PROVENANCE_REQUIRED');
      budget.check();
      const seen = new Set(); const result = []; const timestamp = new Date().toISOString();
      for (const href of snapshot.anchors) {
        const post = canonicalFacebookPost(href);
        if (!post || seen.has(post.url)) continue;
        budget.consume('candidates'); seen.add(post.url);
        result.push(candidateRecord(post, query, 'FACEBOOK_BROWSER', {
          kind: 'SYNTHETIC_FIXTURE', fixture_url: fixtureUrl.href,
          search_url: searchUrl, extraction: 'ANCHOR_HREF_ONLY', live_verified: false
        }, timestamp));
      }
      return result;
    }, budget, this.#timeoutMs);
    budget.check(); return records;
  }
  async search(page, query, budget) {
    return (await this.discover(page, query, budget)).map(record => record.candidate_post_url);
  }
}
