import { canonicalFacebookPost } from './discovery-contract.mjs';
import { closeSync, lstatSync, mkdirSync, openSync, unlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const SMOKE_QUERY = 'tuyển fresher .NET Hà Nội';
const ENDPOINT = 'https://api.socialapis.io/facebook/search/posts';
const MAX_BYTES = 1_048_576;
const DEBUG_DIRECTORY = fileURLToPath(new URL('../.debug/', import.meta.url));
const isObject = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const type = value => value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
const safeContentType = value => {
  if (typeof value !== 'string') return null;
  const mediaType = value.split(';', 1)[0].trim().toLowerCase();
  return ['application/json', 'application/problem+json', 'text/plain', 'text/html', 'application/octet-stream'].includes(mediaType) ? mediaType : 'OTHER';
};
const creditValue = value => typeof value === 'string' && /^\d+(?:\.\d+)?$/u.test(value.trim()) && Number.isFinite(Number(value)) ? Number(value) : null;
const redact = (value, secret) => typeof value === 'string' ? value.replaceAll(secret, '[REDACTED]').replaceAll(encodeURIComponent(secret), '[REDACTED]')
  : Array.isArray(value) ? value.map(item => redact(item, secret))
    : isObject(value) ? Object.fromEntries(Object.entries(value).map(([key, item]) => [key, redact(item, secret)])) : value;

// Opt-in local capture only. Reserve before sending so an existing capture or
// unwritable directory cannot waste another request. Never overwrite evidence.
export function prepareSocialApisDebugResponse(directory = DEBUG_DIRECTORY) {
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  if (!lstatSync(directory).isDirectory() || lstatSync(directory).isSymbolicLink()) throw new Error('DEBUG_DIRECTORY_INVALID');
  const path = join(directory, 'socialapis-response.json');
  const fd = openSync(path, 'wx', 0o600); let saved = false; let closed = false;
  return {
    save(raw, payload, secret) {
      if (closed || saved || typeof raw !== 'string' || Buffer.byteLength(raw) > MAX_BYTES) throw new Error('DEBUG_CAPTURE_REJECTED');
      // Preserve the complete body, without field redaction. If the provider
      // echoes auth material, refuse the capture rather than write a credential.
      const containsSecret = value => typeof value === 'string' ? value.includes(secret) || value.includes(encodeURIComponent(secret))
        : Array.isArray(value) ? value.some(containsSecret)
          : isObject(value) ? Object.entries(value).some(([key, item]) =>
            /^(?:authorization|proxy-authorization|x-api-token|cookie|set-cookie)$/iu.test(key) || containsSecret(key) || containsSecret(item)) : false;
      if (typeof secret !== 'string' || !secret || containsSecret(payload) || raw.includes(secret) || raw.includes(encodeURIComponent(secret))) {
        throw new Error('DEBUG_CAPTURE_AUTH_MATERIAL_REJECTED');
      }
      writeFileSync(fd, raw, { encoding: 'utf8' }); saved = true;
    },
    close() {
      if (closed) return;
      closed = true;
      try { closeSync(fd); } finally { if (!saved) unlinkSync(path); }
    }
  };
}

// Unknown JSON keys can themselves contain names, contact details or secrets.
// Emit known structural names only; never stringify arbitrary keys or values.
const DIAGNOSTIC_KEYS = new Set(['data', 'results', 'posts', 'items', 'response', 'payload', 'result', 'body',
  'page_info', 'paging', 'pagination', 'end_cursor', 'next_cursor', 'has_next', 'has_next_page',
  'post_id', 'id', 'text', 'message', 'author', 'name', 'created_time', 'details', 'values',
  'permalink', 'url', 'post_url', 'post_link', 'status', 'status_code', 'code', 'error', 'errors',
  'error_code', 'success', 'ok', 'request_id', 'meta', 'metadata', 'edges', 'node', 'count', 'total']);
const DIAGNOSTIC_LIMITS = Object.freeze({ depth: 5, nodes: 64, keysPerObject: 12, arraySamples: 2, indicators: 16, bytes: 16_384 });
const ITEM_FIELD_KEYS = ['post_id', 'text', 'created_time', 'permalink', 'url', 'post_url', 'post_link', 'author'];

function providerIndicators(payload) {
  const indicators = []; let errorDetected = false; let truncated = false;
  function inspect(value, path, depth) {
    if (!isObject(value) || depth > 3) return;
    for (const field of ['error', 'errors', 'error_code', 'success', 'ok', 'status', 'status_code', 'code']) {
      if (!Object.hasOwn(value, field)) continue;
      const item = value[field]; let signal = 'PRESENT'; let error = false;
      if (['error', 'errors', 'error_code'].includes(field)) {
        error = item !== null && item !== undefined && item !== false && item !== 0 && item !== '' && item !== '0' &&
          (!Array.isArray(item) || item.length > 0) && (!isObject(item) || Object.keys(item).length > 0);
        signal = error ? 'ERROR_PRESENT' : 'NO_ERROR_INDICATOR';
      } else if (['success', 'ok'].includes(field) && typeof item === 'boolean') {
        error = !item; signal = error ? 'FALSE_SUCCESS' : 'TRUE_SUCCESS';
      } else if (['status', 'status_code', 'code'].includes(field)) {
        error = typeof item === 'number' && item >= 400 && item <= 599 ||
          typeof item === 'string' && /^(?:error|failed|failure|denied|unauthorized|forbidden|[45]\d\d)$/iu.test(item);
        signal = error ? 'ERROR_STATUS' : 'UNCLASSIFIED_STATUS';
      }
      errorDetected ||= error;
      if (indicators.length < DIAGNOSTIC_LIMITS.indicators) indicators.push({ path: `${path}.${field}`, type: type(item), signal, error });
      else truncated = true;
    }
    // Only envelope objects, never post rows/author objects or arbitrary values.
    for (const field of ['data', 'response', 'payload']) inspect(value[field], `${path}.${field}`, depth + 1);
  }
  inspect(payload, '$', 0); return { indicators, errorDetected, truncated };
}

export function diagnoseSocialApisPayload(payload) {
  const nodes = []; let truncated = false;
  function visit(value, path, depth) {
    if (nodes.length >= DIAGNOSTIC_LIMITS.nodes) { truncated = true; return; }
    const node = { path, type: type(value) }; nodes.push(node);
    if (Array.isArray(value)) {
      node.length = value.length;
      if (depth >= DIAGNOSTIC_LIMITS.depth) { if (value.length) truncated = true; return; }
      for (let i = 0; i < Math.min(value.length, DIAGNOSTIC_LIMITS.arraySamples); i++) visit(value[i], `${path}[${i}]`, depth + 1);
      if (value.length > DIAGNOSTIC_LIMITS.arraySamples) truncated = true;
    } else if (isObject(value)) {
      const keys = Object.keys(value); node.key_count = keys.length;
      // Item fields remain visible even when many unrelated fields precede them.
      const ordered = [...keys.filter(key => ITEM_FIELD_KEYS.includes(key)), ...keys.filter(key => !ITEM_FIELD_KEYS.includes(key))];
      const sampled = ordered.slice(0, DIAGNOSTIC_LIMITS.keysPerObject);
      const labels = sampled.map((key, i) => DIAGNOSTIC_KEYS.has(key) ? key : `[REDACTED_KEY_${i}]`);
      node.keys = labels;
      if (keys.length > sampled.length) truncated = true;
      if (depth >= DIAGNOSTIC_LIMITS.depth) { if (keys.length) truncated = true; return; }
      for (let i = 0; i < sampled.length; i++) {
        const key = sampled[i]; const child = value[key];
        // Unknown subtrees are opaque: they may be arbitrary dictionaries keyed by PII.
        if (!DIAGNOSTIC_KEYS.has(key)) {
          if (nodes.length < DIAGNOSTIC_LIMITS.nodes) nodes.push({ path: `${path}.${labels[i]}`, type: type(child),
            ...(Array.isArray(child) ? { length: child.length } : {}), opaque: true });
          else truncated = true;
        } else visit(child, `${path}.${key}`, depth + 1);
      }
    }
  }
  visit(payload, '$', 0);
  const status = providerIndicators(payload);
  const items = isObject(payload?.data) ? (Object.hasOwn(payload.data, 'items') ? payload.data.items : payload.data.results) : null;
  const itemFields = Array.isArray(items) ? items.slice(0, 200).map((item, index) => {
    if (!isObject(item)) return { index, type: type(item) };
    const keys = Object.keys(item);
    const ordered = [...keys.filter(key => ITEM_FIELD_KEYS.includes(key)), ...keys.filter(key => !ITEM_FIELD_KEYS.includes(key))];
    return { index, type: 'object', field_count: keys.length, fields: ordered.slice(0, DIAGNOSTIC_LIMITS.keysPerObject).map((key, i) => ({
      name: DIAGNOSTIC_KEYS.has(key) ? key : `[REDACTED_KEY_${i}]`, type: type(item[key])
    })) };
  }) : [];
  const report = { limits: DIAGNOSTIC_LIMITS, root_type: type(payload), top_level_keys: nodes[0].keys ?? [],
    nodes, array_paths: nodes.filter(node => node.type === 'array').map(node => ({ path: node.path, length: node.length })),
    item_fields: itemFields, item_fields_total: Array.isArray(items) ? items.length : null,
    item_fields_included: itemFields.length, item_fields_truncated: Array.isArray(items) && items.length > itemFields.length,
    indicators: status.indicators, provider_error_detected: status.errorDetected, indicators_truncated: status.truncated,
    truncated: truncated || status.truncated || Array.isArray(items) && items.length > itemFields.length,
    values_included: false, unknown_keys_redacted: true };
  while (Buffer.byteLength(JSON.stringify(report)) > DIAGNOSTIC_LIMITS.bytes) {
    report.truncated = true;
    if (nodes.length > 1) nodes.pop();
    else { itemFields.pop(); report.item_fields_included = itemFields.length; report.item_fields_truncated = true; }
    report.array_paths = nodes.filter(node => node.type === 'array').map(node => ({ path: node.path, length: node.length }));
  }
  return report;
}

export function parseSocialApisSearchResponse(payload) {
  const diagnostics = diagnoseSocialApisPayload(payload);
  if (diagnostics.provider_error_detected) return { outcome: 'PROVIDER_ERROR', rows: null, path: null, diagnostics };
  if (!isObject(payload) || !isObject(payload.data)) {
    return { outcome: 'SCHEMA_MISMATCH', rows: null, path: null, diagnostics };
  }
  // User-supplied live evidence confirms data.items; results remains the
  // documented compatibility path. Never recursively guess another array.
  const field = Object.hasOwn(payload.data, 'items') ? 'items' : 'results';
  const rows = payload.data[field];
  if (!Array.isArray(rows)) return { outcome: 'SCHEMA_MISMATCH', rows: null, path: null, diagnostics };
  return { outcome: rows.length ? 'LIVE_RESULTS_PARSED' : 'EMPTY_RESULTS', rows, path: `$.data.${field}`, diagnostics,
    schema_warnings: field === 'items' && Array.isArray(payload.data.results) ? ['BOTH_RESULT_ARRAYS_ITEMS_SELECTED'] : [] };
}

/** Private, in-memory ingestion candidate. This is NOT a JobPosting command:
 * missing company/title/location are never fabricated; no write is performed.
 * Supports the documented flat contract and the captured data.items[].basic_info
 * shape. The observed nested shape has no publication timestamp; never invent it.
 */
export function normalizeSocialApisItem(row) {
  if (!isObject(row)) return null;
  const nested = Object.hasOwn(row, 'basic_info');
  const fields = nested ? (isObject(row.basic_info) ? row.basic_info : {}) : row;
  const postId = typeof fields.post_id === 'string' && /^(?:\d+|pfbid[A-Za-z0-9]+)$/u.test(fields.post_id) ? fields.post_id : null;
  const text = typeof fields[nested ? 'post_text' : 'text'] === 'string' ? fields[nested ? 'post_text' : 'text'] : null;
  const links = (nested ? ['url'] : ['permalink', 'url', 'post_url', 'post_link'])
    .map(field => ({ field: nested ? `basic_info.${field}` : field, post: canonicalFacebookPost(fields[field]) })).filter(item => item.post);
  const link = links.find(item => postId === null || item.post.id === postId);
  let publishedAt = null;
  // Only the documented numeric Unix-seconds representation; no ms/date-string guessing.
  if (!nested && typeof row.created_time === 'number' && Number.isSafeInteger(row.created_time) && row.created_time >= 0 && row.created_time <= 8_640_000_000_000) {
    publishedAt = new Date(row.created_time * 1000).toISOString();
  }
  const mapped = postId !== null || !!text?.trim() || link !== undefined;
  return { source: 'FACEBOOK', sourceExternalId: postId, sourceUrl: link?.post.url ?? null, rawContent: text, publishedAt,
    permalinkSourceField: link?.field ?? null, permalinkVerified: false, urlInferred: false,
    idUrlConflict: links.some(item => postId !== null && item.post.id !== postId),
    mappingStatus: !mapped ? 'UNRECOGNIZED_ITEM_FIELDS' : nested ? 'PARTIAL_OBSERVED_FIELDS'
      : postId && text?.trim() && link && publishedAt ? 'MAPPED_DOCUMENTED_FIELDS' : 'PARTIAL_DOCUMENTED_FIELDS',
    eligibleForProductionIngestion: false };
}

export async function readSocialApisResponse(response, signal) {
  if (!response.body) throw new Error('NO_BODY');
  const reader = response.body.getReader(); const chunks = []; let size = 0;
  try {
    while (true) {
      if (signal.aborted) throw new Error('TIMEOUT');
      const { value, done } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_BYTES) { void reader.cancel().catch(() => {}); throw new Error('RESPONSE_TOO_LARGE'); }
      chunks.push(Buffer.from(value));
    }
    return Buffer.concat(chunks).toString('utf8');
  } finally { reader.releaseLock(); }
}

// Keep provider text and author identity in memory only. Store only observed
// target terms, IDs and canonical URL candidates, not contact data/raw payloads.
export function summarizeSocialApisRows(rows) {
  const ids = new Set(); const urls = new Set(); const candidates = []; let duplicates = 0; let invalid = 0;
  for (const row of rows) {
    const normalized = normalizeSocialApisItem(row);
    if (!normalized || normalized.mappingStatus === 'UNRECOGNIZED_ITEM_FIELDS') { invalid++; continue; }
    const postId = normalized.sourceExternalId;
    if (postId && ids.has(postId) || normalized.sourceUrl && urls.has(normalized.sourceUrl)) { duplicates++; continue; }
    if (postId) ids.add(postId);
    if (normalized.sourceUrl) urls.add(normalized.sourceUrl);
    const text = normalized.rawContent?.normalize('NFC') ?? '';
    const terms = {
      technology: [...new Set([...text.matchAll(/(?:^|[^\p{L}\p{N}])(asp\.net(?: core)?|\.net|c#)(?=$|[^\p{L}\p{N}])/giu)].map(match => match[1]))],
      seniority: [...new Set(text.match(/\b(?:intern(?:ship)?|fresher|junior)\b|thực tập/giu) ?? [])],
      location: [...new Set(text.match(/hà nội|ha noi|hanoi/giu) ?? [])],
      recruitment: [...new Set(text.match(/tuyển(?: dụng)?|\b(?:hiring|recruitment|vacanc(?:y|ies))\b|tìm\s+(?:intern|fresher|junior)\s+(?:lập trình viên|developer)/giu) ?? [])]
    };
    const recruitment = terms.recruitment.length > 0;
    // A multi-city roundup does not prove the .NET vacancy belongs to Hanoi.
    // Preserve it for review rather than joining technology from HCM to Hanoi.
    const locationAmbiguous = terms.location.length > 0 && /hồ chí minh|ho chi minh|\bhcm\b|sài gòn|sai gon/iu.test(text);
    const matched = recruitment && terms.technology.length > 0 && terms.seniority.length > 0 && terms.location.length > 0 && !locationAmbiguous;
    const classification = matched ? 'TARGET_MATCH' : !text || recruitment && terms.technology.length > 0 ? 'INSUFFICIENT_DATA' : 'NOT_MATCHED';
    candidates.push({ post_id: postId, canonical_post_url: normalized.sourceUrl,
      permalink_status: normalized.sourceUrl ? 'CANDIDATE' : 'MISSING',
      permalink_field: normalized.permalinkSourceField, permalink_syntax_valid: normalized.sourceUrl !== null, permalink_existence_verified: false,
      id_url_conflict: normalized.idUrlConflict, mapping_status: normalized.mappingStatus,
      published_at_valid: normalized.publishedAt !== null, location_assignment_ambiguous: locationAmbiguous,
      url_inferred: false, eligible_for_production_ingestion: false,
      field_types: { post_id: type(Object.hasOwn(row, 'basic_info') ? row.basic_info?.post_id : row.post_id),
        text: type(Object.hasOwn(row, 'basic_info') ? row.basic_info?.post_text : row.text), author: type(row.author),
        author_name: type(row.author?.name), author_id: type(row.author?.id), created_time: type(row.created_time) },
      has_text: !!text.trim(), recruitment, classification, evidence_terms: terms });
  }
  return { candidates, duplicates, invalid };
}

/** Opt-in by explicit local confirmations. testFetch always produces MOCK evidence.
 * No caller-supplied API key is accepted: only SOCIALAPIS_API_KEY in this process.
 * Confirmations are user attestations, not a programmatic billing/rights audit.
 */
export function createSocialApisLiveSmoke({ enabled = false, approvedOneRequest = false, freshKeyConfirmed = false,
  freePlanConfirmed = false, noCashChargeConfirmed = false, timeoutMs = 20_000, debugResponse = false } = {}, testFetch) {
  let attempted = false;
  const mock = typeof testFetch === 'function';
  return Object.freeze({ async run() {
    const report = { execution: mock ? 'MOCK_TRANSPORT' : 'LIVE_OPT_IN', verdict: 'LIVE_BLOCKED',
      query: SMOKE_QUERY, endpoint: ENDPOINT, request_count: 0, http_status: null,
      response_content_type: null, request_duration_ms: null, credits_deducted: null,
      credits_used_alternate_header: null, credit_cost_known: false, response_json_valid: null, results_is_array: null,
      returned_posts: null, returned_items: null, mapped_items: null, unmapped_items: null,
      unique_posts: null, provider_post_records_with_id_and_text: null,
      recruitment_posts: null, matching_hanoi_dotnet_entry_level_posts: null,
      verified_facebook_permalinks: 0, duplicate_posts: null, invalid_rows: null,
      response_schema_differences: [], parse_outcome: null, results_path: null, schema_diagnostics: null,
      debug_response_saved: false,
      debug_response_setup_error: null,
      candidates: [], blockers: [],
      billing_basis: 'USER_ATTESTATION_REQUIRED_NOT_AN_ACCOUNT_API_CHECK' };
    if (!enabled) report.blockers.push('LIVE_OPT_IN_REQUIRED');
    if (!approvedOneRequest) report.blockers.push('ONE_REQUEST_APPROVAL_REQUIRED');
    if (!freshKeyConfirmed) report.blockers.push('FRESH_UNEXPOSED_KEY_CONFIRMATION_REQUIRED');
    if (!freePlanConfirmed || !noCashChargeConfirmed) report.blockers.push('FREE_QUOTA_AND_NO_CASH_CONFIRMATION_REQUIRED');
    if (!mock && (process.env.GITHUB_ACTIONS || process.env.CI)) report.blockers.push('MANUAL_LOCAL_EXECUTION_ONLY');
    const apiKey = process.env.SOCIALAPIS_API_KEY;
    if (typeof apiKey !== 'string' || apiKey.length < 16 || apiKey.length > 512 || /[\s\p{C}]/u.test(apiKey)) report.blockers.push('SAFE_PROCESS_API_KEY_REQUIRED');
    if (!Number.isInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 20_000) report.blockers.push('INVALID_TIMEOUT');
    if (typeof debugResponse !== 'boolean') report.blockers.push('INVALID_DEBUG_OPTION');
    if (attempted) report.blockers.push('REQUEST_ALREADY_ATTEMPTED');
    if (report.blockers.length) return report;
    attempted = true; // Claim before any await: same-instance concurrent calls cannot both send.
    let debugCapture;
    if (debugResponse) {
      try { debugCapture = prepareSocialApisDebugResponse(); }
      catch (error) {
        // Never print the arbitrary exception message (which can contain private
        // paths). Distinguish an existing capture from an actual IO failure.
        const code = ['EEXIST', 'EACCES', 'EPERM', 'ENOENT', 'ENOTDIR', 'ENOSPC', 'EROFS', 'EMFILE', 'ENFILE'].includes(error.code)
          ? error.code : error.message === 'DEBUG_DIRECTORY_INVALID' ? 'DEBUG_DIRECTORY_INVALID' : 'UNKNOWN';
        report.debug_response_setup_error = {
          code,
          operation: ['mkdir', 'stat', 'lstat', 'open'].includes(error.syscall) ? error.syscall : 'DEBUG_SETUP',
          output_file: 'tools/browser-job-discovery/.debug/socialapis-response.json',
          action: code === 'EEXIST' ? 'REUSE_EXISTING_LOCAL_RESPONSE_DO_NOT_REPEAT_LIVE_REQUEST' : 'CHECK_LOCAL_DIRECTORY_AND_FILE_PERMISSIONS'
        };
        report.blockers.push(code === 'EEXIST' ? 'DEBUG_RESPONSE_ALREADY_EXISTS_NO_REQUEST' : 'DEBUG_RESPONSE_SETUP_FAILED_NO_REQUEST');
        return report;
      }
    }
    const requestStarted = performance.now();
    const controller = new AbortController(); let timer;
    try {
      const operation = async () => {
        const url = new URL(ENDPOINT); url.searchParams.set('query', SMOKE_QUERY); url.searchParams.set('recent_posts', 'true');
        report.request_count = 1;
        const response = await (testFetch ?? globalThis.fetch)(url.href, { method: 'GET',
          headers: { 'x-api-token': apiKey, Accept: 'application/json' },
          redirect: 'manual', credentials: 'omit', signal: controller.signal });
        if (controller.signal.aborted) return;
        report.http_status = response.status;
        report.response_content_type = safeContentType(response.headers.get('content-type'));
        report.credits_deducted = creditValue(response.headers.get('X-SocialAPIs-Credits-Deducted'));
        report.credits_used_alternate_header = creditValue(response.headers.get('X-SocialAPIs-Credits-Used'));
        const creditsConflict = report.credits_deducted !== null && report.credits_used_alternate_header !== null &&
          report.credits_deducted !== report.credits_used_alternate_header;
        report.credit_cost_known = report.credits_deducted !== null && !creditsConflict;
        if (creditsConflict) report.blockers.push('CREDIT_HEADERS_DISAGREE_SEMANTICS_UNVERIFIED');
        if (!report.credit_cost_known) report.blockers.push('CREDIT_COST_UNVERIFIED');
        if (report.credits_used_alternate_header !== null) report.response_schema_differences.push('ALTERNATE_CREDITS_USED_HEADER_PRESENT');
        if (response.status !== 200) { void response.body?.cancel().catch(() => {}); report.blockers.push('HTTP_NOT_200'); return; }
        let payload; let raw;
        try { raw = await readSocialApisResponse(response, controller.signal); payload = JSON.parse(raw); }
        catch (error) {
          if (!controller.signal.aborted) { report.response_json_valid = error.message === 'RESPONSE_TOO_LARGE' ? null : false;
            report.blockers.push(error.message === 'RESPONSE_TOO_LARGE' ? 'RESPONSE_TOO_LARGE' : 'INVALID_JSON_OR_BODY'); }
          return;
        }
        if (controller.signal.aborted) return;
        report.response_json_valid = true;
        if (debugCapture) {
          try { debugCapture.save(raw, payload, apiKey); report.debug_response_saved = true; }
          catch { report.blockers.push('DEBUG_RESPONSE_SAVE_FAILED'); }
        }
        const parsed = parseSocialApisSearchResponse(payload);
        report.results_is_array = Array.isArray(parsed.rows);
        report.parse_outcome = parsed.outcome; report.results_path = parsed.path; report.schema_diagnostics = parsed.diagnostics;
        report.response_schema_differences.push(...(parsed.schema_warnings ?? []));
        if (parsed.outcome === 'PROVIDER_ERROR') { report.blockers.push('PROVIDER_ERROR'); return; }
        if (parsed.outcome === 'SCHEMA_MISMATCH') { report.blockers.push('RESULTS_SCHEMA_INVALID'); return; }
        const rows = parsed.rows;
        if (rows.length > 200) { report.blockers.push('RESULT_COUNT_LIMIT_EXCEEDED'); return; }
        const summary = summarizeSocialApisRows(rows);
        report.returned_posts = rows.length; report.returned_items = rows.length; report.unique_posts = summary.candidates.length;
        report.mapped_items = rows.length - summary.invalid; report.unmapped_items = summary.invalid;
        report.duplicate_posts = summary.duplicates; report.invalid_rows = summary.invalid;
        report.candidates = summary.candidates;
        report.provider_post_records_with_id_and_text = summary.candidates.filter(row => row.post_id && row.has_text).length;
        report.recruitment_posts = summary.candidates.filter(row => row.recruitment).length;
        report.matching_hanoi_dotnet_entry_level_posts = summary.candidates.filter(row => row.classification === 'TARGET_MATCH').length;
        if (summary.invalid) report.response_schema_differences.push('UNSUPPORTED_OR_INVALID_ITEM_FIELDS');
        if (summary.candidates.some(row => !row.canonical_post_url)) report.response_schema_differences.push('PERMALINK_MISSING_OR_UNSUPPORTED');
        if (summary.candidates.some(row => row.id_url_conflict)) report.blockers.push('POST_ID_PERMALINK_CONFLICT');
        if (!isObject(payload.data.page_info) || typeof payload.data.page_info.has_next !== 'boolean') report.response_schema_differences.push('PAGE_INFO_MISSING_OR_DIFFERENT');
        // No next_cursor or link is followed. Syntax is not existence verification.
        report.verdict = 'LIVE_API_PARTIAL';
        if (rows.length) report.blockers.push('PERMALINK_EXISTENCE_NOT_INDEPENDENTLY_VERIFIED');
      };
      await Promise.race([operation(), new Promise((_, reject) => {
        timer = setTimeout(() => { controller.abort(); reject(new Error('TIMEOUT')); }, timeoutMs);
      })]);
    } catch {
      report.blockers.push(controller.signal.aborted ? 'REQUEST_TIMEOUT_OUTCOME_UNCERTAIN' : 'NETWORK_ERROR_OUTCOME_UNCERTAIN');
    } finally {
      clearTimeout(timer);
      if (debugCapture) {
        try { debugCapture.close(); } catch { report.blockers.push('DEBUG_RESPONSE_CLEANUP_FAILED'); }
      }
      report.request_duration_ms = Math.max(0, Math.round(performance.now() - requestStarted));
    }
    // No raw provider messages, text, author identities, headers or body escape.
    if (mock) report.verdict = 'MOCK_ONLY';
    return redact(report, apiKey);
  } });
}
