import { Stop } from './core.mjs';
import { validateQuery, validateDateFilters, canonicalFacebookPost, candidateRecord, boundedOperation } from './discovery-contract.mjs';
import { SMOKE_QUERY, normalizeSocialApisItem, diagnoseSocialApisItem, parseSocialApisSearchResponse, summarizeSocialApisRows, readSocialApisResponse } from './socialapis-live-smoke.mjs';
import { buildReviewItems } from './discovery-review.mjs';

export const PROVIDER_SCHEMA_STATUS = 'PROVIDER_RESPONSE_SCHEMA_UNVERIFIED';
// Separate, manual, one-request diagnostic; V2 discover() remains mock-only.
export { createSocialApisLiveSmoke } from './socialapis-live-smoke.mjs';
const mockTransports = new WeakSet();

/** Reads an already captured provider response. No transport, key or browser.
 * Implements the existing search/read port so extraction/dedup/reporting remain
 * in runDiscovery. A capture is evidence for its one query, not fresh searches.
 */
export class SocialApisCaptureDiscoveryAdapter {
  constructor(payload, { capturedQuery = SMOKE_QUERY, evidenceKind = 'LOCAL_PROVIDER_CAPTURE_REPLAY', budget, reviewContext } = {}) {
    this.query = validateQuery(capturedQuery);
    const parsed = parseSocialApisSearchResponse(payload);
    if (!parsed.rows) throw new Stop(parsed.outcome);
    if (parsed.rows.length > 200) throw new Stop('RESULT_COUNT_LIMIT_EXCEEDED');
    this.reviewItems = buildReviewItems(parsed.rows, reviewContext, budget);
    this.itemCount = parsed.rows.length;
    this.rejectionDiagnostics = [];
    this.itemStats = { totalItems: parsed.rows.length, acceptedItems: 0, rejectedItems: 0, rejectionReasons: [], outcome: 'EMPTY_RESULTS' };
    this.pagination = { cursor: typeof payload.data.page_info?.cursor === 'string' ? payload.data.page_info.cursor : null,
      hasNext: typeof payload.data.page_info?.has_next === 'boolean' ? payload.data.page_info.has_next : null };
    this.records = new Map(); this.matches = new Map();
    for (const [index, row] of parsed.rows.entries()) {
      budget?.check();
      const reject = reason => {
        this.itemStats.rejectedItems++;
        this.itemStats.rejectionReasons.push({ index, reason });
        this.rejectionDiagnostics.push({ index, reason, ...diagnoseSocialApisItem(row, reason) });
      };
      if (!row || typeof row !== 'object' || Array.isArray(row)) { reject('ITEM_NOT_OBJECT'); continue; }
      const normalized = normalizeSocialApisItem(row);
      if (!normalized?.sourceExternalId) {
        const id = Object.hasOwn(row, 'basic_info') ? row.basic_info?.post_id : row.post_id;
        reject(id === undefined || id === null || id === '' ? 'POST_ID_MISSING' : 'POST_ID_INVALID'); continue;
      }
      if (normalized.idUrlConflict) { reject('POST_ID_URL_MISMATCH'); continue; }
      if (!normalized.sourceUrl) {
        const urls = Object.hasOwn(row, 'basic_info') ? [row.basic_info?.url] : [row.permalink, row.url, row.post_url, row.post_link];
        reject(urls.some(url => url !== undefined && url !== null && url !== '') ? 'POST_URL_INVALID_OR_UNSUPPORTED' : 'POST_URL_MISSING'); continue;
      }
      // Same per-post limit as JobPostExtractor; an oversized post must not
      // abort extraction of other validated records later in the batch.
      if ((normalized.rawContent?.length ?? 0) > 24_000) { reject('POST_TEXT_TOO_LARGE'); continue; }
      const summary = summarizeSocialApisRows([row]);
      const match = summary.candidates[0];
      if (!match) { reject('POST_MAPPING_INVALID'); continue; }
      const providerUrl = Object.hasOwn(row, 'basic_info') ? row.basic_info.url
        : row[normalized.permalinkSourceField];
      this.records.set(providerUrl, {
        id: normalized.sourceExternalId, provider: 'SOCIALAPIS', source: 'FACEBOOK', sourceUrl: providerUrl,
        canonicalSourceUrl: normalized.sourceUrl, kind: 'facebook-post', title: '', employer: '', location: '', requirements: '',
        body: normalized.rawContent ?? '', publishedAt: normalized.publishedAt, published_at: normalized.publishedAt,
        relativeTime: null, hasImage: false, evidenceKind
      });
      this.matches.set(providerUrl, { classification: match.classification,
        locationAssignmentAmbiguous: match.location_assignment_ambiguous, freshness: normalized.publishedAt ? 'UNVERIFIED' : 'UNKNOWN',
        evidenceTerms: match.evidence_terms, eligibleForProductionIngestion: false });
      this.itemStats.acceptedItems++;
    }
    this.itemStats.outcome = this.itemStats.acceptedItems ? (this.itemStats.rejectedItems ? 'PARTIAL_SUCCESS' : 'SUCCESS')
      : this.itemStats.totalItems ? 'NO_VALID_ITEMS' : 'EMPTY_RESULTS';
  }
  async search(_page, query, budget) {
    budget.consume('searches');
    if (validateQuery(query) !== this.query) throw new Stop('CAPTURE_QUERY_MISMATCH');
    if (this.itemStats.outcome === 'NO_VALID_ITEMS') throw new Stop('NO_VALID_PROVIDER_ITEMS');
    return [...this.records.keys()];
  }
  async read(_page, url, budget) {
    budget.consume('candidates');
    if (!this.records.has(url)) throw new Stop('CAPTURE_POST_NOT_FOUND');
    return structuredClone(this.records.get(url));
  }
  classify(job) { return structuredClone(job.structuredMatch ?? this.matches.get(job.sourceUrl)); }
}

/** One bounded request, no retry/pagination. Billing assertions are operator
 * attestations, not access to provider billing controls. The CLI validates the
 * restored state before entering this adapter or exposing the key to transport.
 */
export class SocialApisLiveDiscoveryAdapter {
  constructor({ enabled = false, freePlanConfirmed = false, noCashConfirmed = false,
    maxRequests = 1, maxCredits = 1, timeoutMs = 20_000, query = SMOKE_QUERY, startTime, endTime, endCursor = null, reviewContext, retainAudit = false } = {}, transport) {
    this.reviewContext = reviewContext;
    this.retainAudit=retainAudit;
    this.options = { enabled, freePlanConfirmed, noCashConfirmed, maxRequests, maxCredits, timeoutMs };
    this.query = validateQuery(query); this.transport = transport;
    this.dateFilters = Object.freeze(validateDateFilters({ startTime, endTime }));
    if (endCursor !== null && (typeof endCursor !== 'string' || !endCursor.trim() || endCursor.length > 4096 || /[\p{C}]/u.test(endCursor))) throw new Stop('INVALID_CURSOR');
    this.endCursor = endCursor;
    this.requestStats = { requestCount: 0, creditsDeducted: null, creditsUsedAlternateHeader: null };
  }
  async search(_page, query, budget) {
    budget.consume('searches');
    const { enabled, freePlanConfirmed, noCashConfirmed, maxRequests, maxCredits, timeoutMs } = this.options;
    if (!enabled || !freePlanConfirmed || !noCashConfirmed) throw new Stop('SOCIALAPIS_LIVE_APPROVAL_REQUIRED');
    if (maxRequests !== 1 || maxCredits !== 1 || !Number.isInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 20_000) throw new Stop('INVALID_LIVE_BUDGET');
    const key = process.env.SOCIALAPIS_API_KEY;
    if (typeof key !== 'string' || key.length < 16 || key.length > 512 || /[\s\p{C}]/u.test(key)) throw new Stop('SOCIALAPIS_API_KEY_REQUIRED');
    if (validateQuery(query) !== this.query) throw new Stop('DISCOVERY_QUERY_MISMATCH');
    if (this.requestStats.requestCount) throw new Stop('REQUEST_BUDGET_EXCEEDED');
    this.requestStats.requestCount = 1; // Claim synchronously before any await.
    const controller = new AbortController(); let timer;
    try {
      const operation = async () => {
        const url = new URL('https://api.socialapis.io/facebook/search/posts');
        url.searchParams.set('query', this.query); url.searchParams.set('recent_posts', 'true');
        if (this.dateFilters.startTime) url.searchParams.set('start_time', this.dateFilters.startTime);
        if (this.dateFilters.endTime) url.searchParams.set('end_time', this.dateFilters.endTime);
        if (this.endCursor !== null) url.searchParams.set('end_cursor', this.endCursor);
        const response = await (this.transport ?? globalThis.fetch)(url.href, { method: 'GET',
          headers: { 'x-api-token': key, Accept: 'application/json' }, redirect: 'manual', credentials: 'omit', signal: controller.signal });
        if (controller.signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
        this.requestStats.httpStatus = response.status;
        const numeric = value => typeof value === 'string' && /^\d+(?:\.\d+)?$/u.test(value) && Number.isFinite(Number(value)) ? Number(value) : null;
        this.requestStats.creditsDeducted = numeric(response.headers.get('X-SocialAPIs-Credits-Deducted'));
        this.requestStats.creditsUsedAlternateHeader = numeric(response.headers.get('X-SocialAPIs-Credits-Used'));
        if (response.status !== 200) { void response.body?.cancel().catch(() => {}); throw new Stop('PROVIDER_HTTP_REJECTED'); }
        const raw = await readSocialApisResponse(response, controller.signal);
        if (controller.signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
        const payload = JSON.parse(raw);
        if ([raw, JSON.stringify(payload)].some(value => value.includes(key) || value.includes(encodeURIComponent(key)))) throw new Stop('PROVIDER_SECRET_REFLECTION');
        if(this.retainAudit)this.responseAudit={kind:'PRIVATE_PROVIDER_JSON',payload};
        const bodyCredits = payload?.meta?.creditsCharged;
        if (this.requestStats.creditsDeducted === null || this.requestStats.creditsDeducted > maxCredits ||
            typeof bodyCredits === 'number' && bodyCredits > maxCredits) throw new Stop('PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED');
        // Credits-Used remains a separate opaque usage metric, never added.
        const adapter = new SocialApisCaptureDiscoveryAdapter(payload, { capturedQuery: this.query,
          evidenceKind: this.transport ? 'MOCK_PROVIDER_RESPONSE' : 'LIVE_PROVIDER_RESPONSE', budget, reviewContext: this.reviewContext });
        if (controller.signal.aborted) throw new Stop('DISCOVERY_TIMEOUT');
        this.adapter = adapter; this.itemCount = adapter.itemCount; this.pagination = adapter.pagination; this.itemStats = adapter.itemStats;
        this.rejectionDiagnostics = adapter.rejectionDiagnostics;
        if (this.itemStats.outcome === 'NO_VALID_ITEMS') throw new Stop('NO_VALID_PROVIDER_ITEMS');
        return [...adapter.records.keys()];
      };
      return await Promise.race([operation(), new Promise((_, reject) => {
        timer = setTimeout(() => { controller.abort(); reject(new Stop('DISCOVERY_TIMEOUT')); }, timeoutMs);
      })]);
    } catch (error) { throw error instanceof Stop ? error : new Stop('PROVIDER_REQUEST_FAILED'); }
    finally { clearTimeout(timer); }
  }
  async read(page, url, budget) { return this.adapter.read(page, url, budget); }
  classify(job) { return this.adapter.classify(job); }
}

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
