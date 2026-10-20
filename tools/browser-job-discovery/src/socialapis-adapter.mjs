import { Stop } from './core.mjs';
import { validateQuery, canonicalFacebookPost, candidateRecord, boundedOperation } from './discovery-contract.mjs';

export const PROVIDER_SCHEMA_STATUS = 'PROVIDER_RESPONSE_SCHEMA_UNVERIFIED';
const mockTransports = new WeakSet();

// Purpose-built mock wire envelope, NOT the SocialAPIs response schema.
// No fetch implementation exists in this V2 spike.
export function createMockSocialApisTransport(pages, { delayMs = 0, fail = false, status = 200 } = {}) {
  if (!Array.isArray(pages) || pages.length > 3 || !Number.isInteger(delayMs) || delayMs < 0 || delayMs > 1000) throw new Stop('INVALID_MOCK');
  const copies = structuredClone(pages); let count = 0; const calls = [];
  const transport = {
    get calls() { return structuredClone(calls); },
    async request(request, signal) {
      calls.push({ method: request.method, url: request.url, redirect: request.redirect, credentials: request.credentials });
      if (delayMs) await new Promise(resolve => {
        const finish = () => { clearTimeout(timer); signal.removeEventListener('abort', finish); resolve(); };
        const timer = setTimeout(finish, delayMs); signal.addEventListener('abort', finish, { once: true });
        if (signal.aborted) finish();
      });
      if (signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
      if (fail) throw new Error('Mock transport failure');
      return { status, body: JSON.stringify(copies[count++] ?? null) };
    }
  };
  mockTransports.add(transport); return Object.freeze(transport);
}

function validateMockResponse(response) {
  if (!response || typeof response.body !== 'string' || Buffer.byteLength(response.body) > 64_000) throw new Stop('PROVIDER_SCHEMA_INVALID');
  if (response.status !== 200) throw new Stop('PROVIDER_REQUEST_REJECTED');
  let data;
  try { data = JSON.parse(response.body); } catch { throw new Stop('PROVIDER_SCHEMA_INVALID'); }
  if (!data || data.fixture_kind !== 'SYNTHETIC_FIXTURE' || !Array.isArray(data.posts) || data.posts.length > 20 ||
      !(data.next_cursor === null || typeof data.next_cursor === 'string' && /^[A-Za-z0-9_-]{1,128}$/u.test(data.next_cursor)) ||
      data.posts.some(post => !post || Object.keys(post).some(key => key !== 'url') || !canonicalFacebookPost(post.url))) {
    throw new Stop('PROVIDER_SCHEMA_INVALID');
  }
  return data;
}

export class SocialApisHubHttpAdapter {
  #baseUrl; #apiKey; #transport; #timeoutMs; #maxPages;
  constructor({ baseUrl, apiKey, mode = 'offline', authorizationStatus, transport, timeoutMs = 5000, maxPages = 2 } = {}) {
    if (mode !== 'offline') throw new Stop(PROVIDER_SCHEMA_STATUS);
    if (authorizationStatus !== 'OFFLINE_FIXTURE_ONLY') throw new Stop('PROVIDER_AUTHORIZATION_DENIED');
    if (typeof apiKey !== 'string' || !apiKey.trim() || apiKey.length > 512 || /[\s\p{C}]/u.test(apiKey)) throw new Stop('PROVIDER_API_KEY_REQUIRED');
    let base;
    try { base = new URL(baseUrl); } catch { throw new Stop('INVALID_PROVIDER_BASE_URL'); }
    if (base.origin !== 'https://api.socialapis.io' || base.pathname !== '/' || base.search || base.hash || base.username || base.password) throw new Stop('INVALID_PROVIDER_BASE_URL');
    if (!mockTransports.has(transport)) throw new Stop(PROVIDER_SCHEMA_STATUS);
    if (!Number.isInteger(maxPages) || maxPages < 1 || maxPages > 3) throw new Stop('INVALID_PAGINATION_LIMIT');
    this.#baseUrl = base.href; this.#apiKey = apiKey; this.#transport = transport;
    this.#timeoutMs = timeoutMs; this.#maxPages = maxPages;
  }
  async discover(rawQuery, budget) {
    const query = validateQuery(rawQuery); const records = []; const seen = new Set(); const cursors = new Set();
    let cursor = null;
    for (let page = 0; page < this.#maxPages; page++) {
      budget.consume('searches');
      const url = new URL('/facebook/search/posts', this.#baseUrl);
      url.searchParams.set('query', query);
      if (cursor) url.searchParams.set('end_cursor', cursor);
      const response = await boundedOperation(signal => this.#transport.request({ method: 'GET', url: url.href,
        headers: { 'x-api-token': this.#apiKey }, credentials: 'omit', redirect: 'error' }, signal), budget, this.#timeoutMs);
      budget.check();
      const data = validateMockResponse(response);
      for (const item of data.posts) {
        const post = canonicalFacebookPost(item.url);
        if (seen.has(post.url)) continue;
        budget.consume('candidates'); seen.add(post.url);
        records.push(candidateRecord(post, query, 'SOCIALAPISHUB_HTTP', {
          kind: 'SYNTHETIC_FIXTURE', schema_status: PROVIDER_SCHEMA_STATUS,
          endpoint: url.href, live_verified: false
        }, new Date().toISOString()));
      }
      cursor = data.next_cursor;
      if (cursor === null) return records;
      if (cursors.has(cursor)) throw new Stop('PROVIDER_CURSOR_REPEATED');
      cursors.add(cursor);
    }
    // Never silently report a truncated page set as complete discovery.
    throw new Stop('PROVIDER_PAGE_BUDGET_EXCEEDED');
  }
}
