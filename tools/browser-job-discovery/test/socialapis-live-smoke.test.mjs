import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { createSocialApisLiveSmoke } from '../src/socialapis-adapter.mjs';
import { diagnoseSocialApisPayload, parseSocialApisSearchResponse, normalizeSocialApisItem, prepareSocialApisDebugResponse, summarizeSocialApisRows } from '../src/socialapis-live-smoke.mjs';

const approval = { enabled: true, approvedOneRequest: true, freshKeyConfirmed: true, freePlanConfirmed: true, noCashChargeConfirmed: true };
const syntheticKey = 'synthetic-never-real-test-key';
const post = { post_id: '1001', text: 'Tuyển fresher .NET tại Hà Nội. Liên hệ private@example.invalid',
  author: { name: 'Synthetic private name', id: '9000' }, created_time: 1726794818,
  permalink: 'https://www.facebook.com/fixture/posts/1001' };
const body = rows => ({ data: { results: rows, page_info: { end_cursor: 'never-follow', has_next: true } } });
async function withSyntheticKey(run) {
  const previous = process.env.SOCIALAPIS_API_KEY;
  process.env.SOCIALAPIS_API_KEY = syntheticKey;
  try { return await run(); } finally {
    if (previous === undefined) delete process.env.SOCIALAPIS_API_KEY;
    else process.env.SOCIALAPIS_API_KEY = previous;
  }
}
function transport(payload = body([post]), { status = 200, credits = '1', alternate, delay = 0, raw, contentType = 'application/json' } = {}) {
  const calls = [];
  const fetch = async (url, options) => {
    calls.push({ url, options });
    if (delay) await new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(new Error('sensitive diagnostic')), { once: true }));
    const headers = {};
    if (contentType !== null) headers['content-type'] = contentType;
    if (credits !== null) headers['X-SocialAPIs-Credits-Deducted'] = credits;
    if (alternate !== undefined) headers['X-SocialAPIs-Credits-Used'] = alternate;
    return new Response(raw ?? JSON.stringify(payload), { status, headers });
  };
  return { fetch, calls };
}

test('default OFF and every approval/free/fresh-key gate reject before any network call', async () => withSyntheticKey(async () => {
  const fake = transport();
  assert.equal((await createSocialApisLiveSmoke({}, fake.fetch).run()).request_count, 0);
  for (const key of Object.keys(approval)) {
    const report = await createSocialApisLiveSmoke({ ...approval, [key]: false }, fake.fetch).run();
    assert.equal(report.verdict, 'LIVE_BLOCKED'); assert.equal(report.request_count, 0);
  }
  assert.equal(fake.calls.length, 0);
}));
test('missing/unsafe process key and invalid timeouts reject without sending', async () => withSyntheticKey(async () => {
  const fake = transport();
  for (const key of ['', 'short', 'header\r\ninjection', undefined]) {
    if (key === undefined) delete process.env.SOCIALAPIS_API_KEY; else process.env.SOCIALAPIS_API_KEY = key;
    const result = await createSocialApisLiveSmoke(approval, fake.fetch).run();
    assert.ok(result.blockers.includes('SAFE_PROCESS_API_KEY_REQUIRED')); assert.equal(result.request_count, 0);
  }
  process.env.SOCIALAPIS_API_KEY = syntheticKey;
  for (const timeoutMs of [0, -1, 20_001, 1.5]) assert.equal((await createSocialApisLiveSmoke({ ...approval, timeoutMs }, fake.fetch).run()).request_count, 0);
  assert.equal(fake.calls.length, 0);
}));
test('exact one request, exact encoded query, manual redirects, no pagination/retry and no live claim from fake transport', async () => withSyntheticKey(async () => {
  const fake = transport(); const smoke = createSocialApisLiveSmoke(approval, fake.fetch);
  const report = await smoke.run();
  assert.equal(fake.calls.length, 1); assert.equal(report.execution, 'MOCK_TRANSPORT'); assert.equal(report.verdict, 'MOCK_ONLY');
  const { url, options } = fake.calls[0];
  assert.equal(new URL(url).origin, 'https://api.socialapis.io'); assert.equal(new URL(url).pathname, '/facebook/search/posts');
  assert.equal(new URL(url).searchParams.get('query'), 'tuyển fresher .NET Hà Nội');
  assert.deepEqual([...new URL(url).searchParams.keys()], ['query', 'recent_posts']);
  assert.equal(options.method, 'GET'); assert.equal(options.redirect, 'manual'); assert.equal(options.credentials, 'omit');
  assert.equal(options.headers['x-api-token'], syntheticKey); assert.ok(!url.includes(syntheticKey));
  assert.equal(report.credits_deducted, 1); assert.equal(report.credit_cost_known, true);
  assert.equal(report.response_content_type, 'application/json');
  assert.ok(Number.isInteger(report.request_duration_ms) && report.request_duration_ms >= 0);
  assert.equal(report.recruitment_posts, 1); assert.equal(report.matching_hanoi_dotnet_entry_level_posts, 1);
  assert.equal(report.verified_facebook_permalinks, 0); assert.equal(report.candidates[0].permalink_existence_verified, false);
  assert.equal(report.candidates[0].permalink_status, 'CANDIDATE');
  assert.equal((await smoke.run()).request_count, 0); assert.equal(fake.calls.length, 1);
  assert.doesNotMatch(JSON.stringify(report), /synthetic-never-real-test-key|private@example|Synthetic private name|9000/u);
}));
test('concurrent calls on one smoke can claim the sender once only', async () => withSyntheticKey(async () => {
  const fake = transport(); const smoke = createSocialApisLiveSmoke(approval, fake.fetch);
  const results = await Promise.all([smoke.run(), smoke.run()]);
  assert.equal(fake.calls.length, 1); assert.equal(results.reduce((count, item) => count + item.request_count, 0), 1);
}));
test('dedup by post ID or canonical URL and keep absent permalink unverified; never invent links', async () => withSyntheticKey(async () => {
  const withoutLink = { ...post, post_id: '2002', permalink: undefined, text: 'Tuyển Junior C#', author: null };
  const payload = body([post, { ...post }, { ...post, post_id: undefined }, withoutLink, { post_id: '3003', text: 'Weather today' }, null]);
  const report = await createSocialApisLiveSmoke(approval, transport(payload).fetch).run();
  assert.equal(report.returned_posts, 6); assert.equal(report.duplicate_posts, 2); assert.equal(report.unique_posts, 3); assert.equal(report.invalid_rows, 1);
  assert.equal(report.provider_post_records_with_id_and_text, 3); assert.equal(report.recruitment_posts, 2);
  assert.equal(report.candidates[1].canonical_post_url, null); assert.equal(report.candidates[1].classification, 'INSUFFICIENT_DATA');
  assert.equal(report.candidates[2].classification, 'NOT_MATCHED');
  assert.ok(report.response_schema_differences.includes('PERMALINK_MISSING_OR_UNSUPPORTED'));
}));
test('ID/link mismatch never counts as verified; schema differences and field types recorded', async () => withSyntheticKey(async () => {
  const payload = { data: { results: [{ ...post, post_id: '9999', created_time: null }] } };
  const report = await createSocialApisLiveSmoke(approval, transport(payload).fetch).run();
  assert.ok(report.blockers.includes('POST_ID_PERMALINK_CONFLICT')); assert.equal(report.candidates[0].canonical_post_url, null);
  assert.equal(report.candidates[0].field_types.created_time, 'null'); assert.equal(report.verified_facebook_permalinks, 0);
}));
test('missing/invalid credit header stays unknown; Credits-Used never silently replaces Deducted', async () => withSyntheticKey(async () => {
  for (const credits of [null, '-1', 'NaN', 'secret-untrusted-header']) {
    const report = await createSocialApisLiveSmoke(approval, transport(undefined, { credits, alternate: '1' }).fetch).run();
    assert.equal(report.credits_deducted, null); assert.equal(report.credit_cost_known, false);
    assert.equal(report.credits_used_alternate_header, 1); assert.ok(report.blockers.includes('CREDIT_COST_UNVERIFIED'));
    assert.ok(!JSON.stringify(report).includes('secret-untrusted-header'));
  }
}));
for (const status of [302, 401, 403, 429, 500]) test(`HTTP ${status} is not followed/retried; preserve status and credits only`, async () => withSyntheticKey(async () => {
  const fake = transport({ message: syntheticKey }, { status, credits: '0' });
  const smoke = createSocialApisLiveSmoke(approval, fake.fetch); const report = await smoke.run();
  assert.equal(report.http_status, status); assert.equal(report.credits_deducted, 0); assert.equal(fake.calls.length, 1);
  assert.ok(report.blockers.includes('HTTP_NOT_200')); assert.equal(report.returned_posts, null);
  await smoke.run(); assert.equal(fake.calls.length, 1); assert.ok(!JSON.stringify(report).includes(syntheticKey));
}));
test('invalid JSON, missing results array, row limit and response size fail safely', async () => withSyntheticKey(async () => {
  for (const [payload, options, blocker] of [
    [null, { raw: 'not json' }, 'INVALID_JSON_OR_BODY'], [{ data: {} }, {}, 'RESULTS_SCHEMA_INVALID'],
    [body(Array(201).fill(post)), {}, 'RESULT_COUNT_LIMIT_EXCEEDED'], [null, { raw: 'x'.repeat(1_048_577) }, 'RESPONSE_TOO_LARGE']
  ]) {
    const fake = transport(payload, options); const report = await createSocialApisLiveSmoke(approval, fake.fetch).run();
    assert.ok(report.blockers.includes(blocker)); assert.equal(fake.calls.length, 1);
  }
}));
test('timeout and ambiguous network failures never retry or leak provider errors', async () => withSyntheticKey(async () => {
  const fake = transport(undefined, { delay: 1 }); const report = await createSocialApisLiveSmoke({ ...approval, timeoutMs: 5 }, fake.fetch).run();
  assert.ok(report.blockers.includes('REQUEST_TIMEOUT_OUTCOME_UNCERTAIN')); assert.equal(fake.calls.length, 1);
  assert.equal(report.credits_deducted, null);
  const failure = await createSocialApisLiveSmoke(approval, async () => { throw new Error(syntheticKey); }).run();
  assert.ok(failure.blockers.includes('NETWORK_ERROR_OUTCOME_UNCERTAIN')); assert.ok(!JSON.stringify(failure).includes(syntheticKey));
}));
test('whole body read shares deadline and post content cannot leak key through a URL', async () => withSyntheticKey(async () => {
  const stalled = await createSocialApisLiveSmoke({ ...approval, timeoutMs: 5 }, async () => new Response(new ReadableStream({ start() {} }))).run();
  assert.ok(stalled.blockers.includes('REQUEST_TIMEOUT_OUTCOME_UNCERTAIN'));
  const payload = body([{ ...post, permalink: `https://www.facebook.com/${syntheticKey}/posts/1001` }]);
  const result = await createSocialApisLiveSmoke(approval, transport(payload).fetch).run();
  assert.ok(!JSON.stringify(result).includes(syntheticKey));
}));

const schemaFixtures = JSON.parse(await readFile(new URL('../fixtures/socialapis-schemas.json', import.meta.url), 'utf8'));
for (const fixture of schemaFixtures.cases) test(`schema diagnostic fixture: ${fixture.name}`, () => {
  const parsed = parseSocialApisSearchResponse(fixture.payload);
  assert.equal(parsed.outcome, fixture.expected);
  assert.equal(parsed.path, ['LIVE_RESULTS_PARSED', 'EMPTY_RESULTS'].includes(fixture.expected) ? fixture.expected_path ?? '$.data.results' : null);
  assert.equal(parsed.diagnostics.values_included, false);
  assert.doesNotMatch(JSON.stringify(parsed.diagnostics), /SYNTHETIC_PRIVATE|SYNTHETIC_FAILURE|SYNTHETIC_CURSOR|9999|1726794818/u);
});
test('schema diagnostics show nested keys/types and array paths/lengths, not values', () => {
  const result = diagnoseSocialApisPayload(schemaFixtures.cases.find(item => item.name === 'documented-results').payload);
  assert.deepEqual(result.top_level_keys, ['data']);
  assert.ok(result.nodes.some(node => node.path === '$.data.results[0].text' && node.type === 'string'));
  assert.ok(result.nodes.some(node => node.path === '$.data.results[0].author' && node.keys.includes('name')));
  assert.ok(result.array_paths.some(node => node.path === '$.data.results' && node.length === 1));
  assert.doesNotMatch(JSON.stringify(result), /Synthetic recruitment text|SYNTHETIC_PRIVATE_AUTHOR/u);
});
test('diagnostics redact unknown key names/opaque subtrees even when PII or token is itself a key', () => {
  const result = diagnoseSocialApisPayload({ 'private@example.invalid': { [syntheticKey]: 'secret' }, data: { results: [{
    text: syntheticKey, author: { name: 'PRIVATE_PERSON', id: 'PRIVATE_ID' },
    credentials: { [syntheticKey]: 'secret' }, 'PRIVATE_PERSON': ['PRIVATE_VALUE']
  }] } });
  const serialized = JSON.stringify(result);
  assert.doesNotMatch(serialized, /private@example|PRIVATE_PERSON|PRIVATE_ID|PRIVATE_VALUE|synthetic-never-real-test-key/u);
  assert.match(serialized, /REDACTED_KEY/u);
});
test('diagnostics have hard depth/node/key/sample bounds and serialized size under 16 KiB', () => {
  let value = { data: { results: Array(1000).fill({ text: 'PRIVATE_VALUE', author: { name: 'PRIVATE_PERSON' } }) } };
  for (let depth = 0; depth < 50; depth++) value = { data: value };
  for (const payload of [value, Object.fromEntries(Array.from({ length: 1000 }, (_, i) => [`PRIVATE_KEY_${i}`, 'PRIVATE_VALUE'])),
    { data: { results: Array(1000).fill(schemaFixtures.cases[0].payload) } }]) {
    const result = diagnoseSocialApisPayload(payload);
    assert.equal(result.truncated, true); assert.ok(result.nodes.length <= 64);
    assert.ok(result.nodes.every(node => !node.keys || node.keys.length <= 12));
    assert.ok(Buffer.byteLength(JSON.stringify(result)) < 16_384);
    assert.doesNotMatch(JSON.stringify(result), /PRIVATE_VALUE|PRIVATE_PERSON|PRIVATE_KEY/u);
  }
  const envelopeTree = depth => depth ? Object.fromEntries(['data', 'response', 'payload'].map(key => [key, envelopeTree(depth - 1)]))
    : { status: 'ok', code: 200, success: true, ok: true, error: null, errors: [], error_code: 0, status_code: 200 };
  const bounded = diagnoseSocialApisPayload(envelopeTree(4));
  assert.ok(bounded.indicators.length <= 16); assert.ok(Buffer.byteLength(JSON.stringify(bounded)) <= 16_384);
});
test('provider error checks never inspect post/author status as an envelope error', () => {
  const payload = body([{ ...post, status: 'error', author: { status: 'failed' } }]);
  assert.equal(parseSocialApisSearchResponse(payload).outcome, 'LIVE_RESULTS_PARSED');
  assert.equal(parseSocialApisSearchResponse({ error: null, errors: [], data: { results: [] } }).outcome, 'EMPTY_RESULTS');
});
test('diagnostic output cap never masks an envelope error after many benign indicators', () => {
  const benign = { error: null, errors: [], error_code: 0, success: true, ok: true, status: 'ok', status_code: 200, code: 200 };
  const payload = { ...benign, data: { ...benign, results: [] }, response: { error: 'PRIVATE_ERROR' } };
  const parsed = parseSocialApisSearchResponse(payload);
  assert.equal(parsed.outcome, 'PROVIDER_ERROR'); assert.equal(parsed.diagnostics.provider_error_detected, true);
  assert.equal(parsed.diagnostics.indicators_truncated, true); assert.ok(parsed.diagnostics.indicators.length <= 16);
  assert.doesNotMatch(JSON.stringify(parsed.diagnostics), /PRIVATE_ERROR/u);
});
test('HTTP 200 provider error is distinct from schema mismatch, empty results and parsed rows in real smoke composition', async () => withSyntheticKey(async () => {
  for (const [payload, outcome] of [[{ error: { message: syntheticKey } }, 'PROVIDER_ERROR'],
    [{ results: [] }, 'SCHEMA_MISMATCH'], [body([]), 'EMPTY_RESULTS'], [body([post]), 'LIVE_RESULTS_PARSED']]) {
    const fake = transport(payload); const report = await createSocialApisLiveSmoke(approval, fake.fetch).run();
    assert.equal(report.http_status, 200); assert.equal(report.parse_outcome, outcome);
    assert.equal(fake.calls.length, 1); assert.equal(report.schema_diagnostics.values_included, false);
    if (outcome === 'PROVIDER_ERROR' || outcome === 'SCHEMA_MISMATCH') assert.equal(report.returned_posts, null);
    if (outcome === 'EMPTY_RESULTS') assert.equal(report.returned_posts, 0);
    assert.doesNotMatch(JSON.stringify(report), /synthetic-never-real-test-key/u);
  }
}));
test('observed Deducted=1 / Used=5 remain independent; no inferred total or cumulative meaning', async () => withSyntheticKey(async () => {
  const report = await createSocialApisLiveSmoke(approval, transport({ data: { unexpected: [] } }, { credits: '1', alternate: '5' }).fetch).run();
  assert.equal(report.credits_deducted, 1); assert.equal(report.credits_used_alternate_header, 5);
  assert.equal(report.credit_cost_known, false);
  assert.ok(report.blockers.includes('CREDIT_HEADERS_DISAGREE_SEMANTICS_UNVERIFIED'));
  assert.equal(report.parse_outcome, 'SCHEMA_MISMATCH'); assert.equal(report.returned_posts, null);
  assert.equal(Object.hasOwn(report, 'total_credits'), false);
}));

test('items and results normalize identically, while retaining the actual selected path', async () => withSyntheticKey(async () => {
  for (const field of ['items', 'results']) {
    const payload = { data: { [field]: [post] }, message: 'PRIVATE_MESSAGE', meta: {} };
    const fake = transport(payload); const result = await createSocialApisLiveSmoke(approval, fake.fetch).run();
    assert.equal(result.parse_outcome, 'LIVE_RESULTS_PARSED'); assert.equal(result.results_path, `$.data.${field}`);
    assert.equal(result.results_is_array, true); assert.equal(result.returned_items, 1);
    assert.equal(result.mapped_items, 1); assert.equal(result.unmapped_items, 0);
    assert.equal(result.provider_post_records_with_id_and_text, 1);
    assert.equal(result.matching_hanoi_dotnet_entry_level_posts, 1); assert.equal(result.verified_facebook_permalinks, 0);
    assert.equal(result.candidates[0].mapping_status, 'MAPPED_DOCUMENTED_FIELDS');
    assert.equal(fake.calls.length, 1); assert.equal(result.execution, 'MOCK_TRANSPORT');
    assert.doesNotMatch(JSON.stringify(result), /PRIVATE_MESSAGE|private@example|Synthetic private name/u);
  }
}));
test('both result arrays select items deterministically without merging entities', () => {
  const parsed = parseSocialApisSearchResponse({ data: { items: [], results: [post] } });
  assert.equal(parsed.outcome, 'EMPTY_RESULTS'); assert.equal(parsed.path, '$.data.items');
  assert.ok(parsed.schema_warnings.includes('BOTH_RESULT_ARRAYS_ITEMS_SELECTED'));
});
test('normalization uses only documented flat fields and returns an in-memory non-write candidate', () => {
  const normalized = normalizeSocialApisItem(post);
  assert.equal(normalized.source, 'FACEBOOK'); assert.equal(normalized.sourceExternalId, '1001');
  assert.equal(normalized.rawContent, post.text); assert.equal(normalized.publishedAt, '2024-09-20T01:13:38.000Z');
  assert.equal(normalized.sourceUrl, post.permalink); assert.equal(normalized.permalinkSourceField, 'permalink');
  assert.equal(normalized.permalinkVerified, false); assert.equal(normalized.urlInferred, false);
  assert.equal(normalized.eligibleForProductionIngestion, false); assert.equal(Object.hasOwn(normalized, 'author'), false);
  const unknown = normalizeSocialApisItem({ id: '1001', message: post.text, details: { post_id: '1001' }, values: { text: post.text } });
  assert.equal(unknown.mappingStatus, 'UNRECOGNIZED_ITEM_FIELDS'); assert.equal(unknown.rawContent, null); assert.equal(unknown.sourceExternalId, null);
});
test('invalid items and unknown item fields do not become jobs; actual item shape is diagnosable', async () => withSyntheticKey(async () => {
  const rows = [null, 'private body', [], {}, { post_id: {}, text: {}, created_time: 'not-a-date' }, { payload: { text: post.text } }];
  const result = await createSocialApisLiveSmoke(approval, transport({ data: { items: rows } }).fetch).run();
  assert.equal(result.returned_items, 6); assert.equal(result.invalid_rows, 6); assert.equal(result.unique_posts, 0);
  assert.equal(result.mapped_items, 0); assert.equal(result.unmapped_items, 6);
  assert.equal(result.recruitment_posts, 0); assert.equal(result.matching_hanoi_dotnet_entry_level_posts, 0);
  assert.ok(result.response_schema_differences.includes('UNSUPPORTED_OR_INVALID_ITEM_FIELDS'));
  assert.ok(result.schema_diagnostics.array_paths.some(item => item.path === '$.data.items' && item.length === 6));
  assert.doesNotMatch(JSON.stringify(result.schema_diagnostics), /private body|Tuyển fresher/u);
}));
test('no guessed dates, identifiers or permalink aliases in normalization', () => {
  for (const created_time of ['1726794818', '2024-09-20T00:13:38Z', -1, 1.5, 8_640_000_000_001]) {
    assert.equal(normalizeSocialApisItem({ ...post, created_time }).publishedAt, null);
  }
  assert.equal(normalizeSocialApisItem({ ...post, post_id: 1001 }).sourceExternalId, null);
  const missingUrl = normalizeSocialApisItem({ post_id: '1001', text: 'Synthetic text', link: post.permalink });
  assert.equal(missingUrl.sourceUrl, null); assert.equal(missingUrl.permalinkSourceField, null);
  assert.equal(normalizeSocialApisItem({ created_time: 1726794818 }).mappingStatus, 'UNRECOGNIZED_ITEM_FIELDS');
});
test('items dedup uses post ID/canonical aliases, not identical text from different posts', async () => withSyntheticKey(async () => {
  const rows = [post, { ...post, permalink: post.permalink + '?ref=tracking' },
    { ...post, post_id: undefined }, { ...post, post_id: '1002', permalink: undefined }];
  const result = await createSocialApisLiveSmoke(approval, transport({ data: { items: rows } }).fetch).run();
  assert.equal(result.returned_items, 4); assert.equal(result.duplicate_posts, 2); assert.equal(result.unique_posts, 2);
  assert.equal(result.mapped_items, 4); assert.equal(result.unmapped_items, 0);
  assert.equal(result.candidates[1].canonical_post_url, null); assert.equal(result.verified_facebook_permalinks, 0);
}));
test('items diagnostics prioritize known fields under caps and never expose content/author/unknown keys', () => {
  const item = { ...Object.fromEntries(Array.from({ length: 20 }, (_, i) => [`PRIVATE_FIELD_${i}`, 'PRIVATE_VALUE'])), ...post };
  const result = diagnoseSocialApisPayload({ data: { items: [item] }, message: 'PRIVATE_MESSAGE', meta: {} });
  const itemNode = result.nodes.find(node => node.path === '$.data.items[0]');
  for (const field of ['post_id', 'text', 'created_time', 'permalink', 'author']) assert.ok(itemNode.keys.includes(field));
  assert.equal(result.truncated, true); assert.ok(Buffer.byteLength(JSON.stringify(result)) <= 16_384);
  assert.doesNotMatch(JSON.stringify(result), /PRIVATE_VALUE|PRIVATE_FIELD|PRIVATE_MESSAGE|private@example|Synthetic private name/u);
});
test('latest observed credit numbers 1 and 6 remain separate and flagged, never summed', async () => withSyntheticKey(async () => {
  const result = await createSocialApisLiveSmoke(approval, transport({ data: { items: [] }, message: 'private', meta: {} }, { credits: '1', alternate: '6' }).fetch).run();
  assert.equal(result.credits_deducted, 1); assert.equal(result.credits_used_alternate_header, 6); assert.equal(result.credit_cost_known, false);
  assert.ok(result.blockers.includes('CREDIT_HEADERS_DISAGREE_SEMANTICS_UNVERIFIED'));
  assert.equal(result.parse_outcome, 'EMPTY_RESULTS'); assert.equal(Object.hasOwn(result, 'total_credits'), false);
}));

test('V4 safe content type/duration for errors and null timing before preflight; no raw header echo', async () => withSyntheticKey(async () => {
  const blocked = await createSocialApisLiveSmoke({}, transport().fetch).run();
  assert.equal(blocked.request_duration_ms, null); assert.equal(blocked.response_content_type, null);
  for (const [status, contentType, expected] of [[200, 'application/json; private=SECRET_HEADER', 'application/json'],
    [403, 'text/html', 'text/html'], [200, syntheticKey, 'OTHER']]) {
    const report = await createSocialApisLiveSmoke(approval, transport(undefined, { status, contentType }).fetch).run();
    assert.equal(report.response_content_type, expected); assert.ok(report.request_duration_ms >= 0);
    assert.doesNotMatch(JSON.stringify(report), /SECRET_HEADER|synthetic-never-real-test-key/u);
  }
}));
test('V4 per-item field metadata includes unmapped items without exposing values and remains bounded', () => {
  const rows = [post, { details: { private: 'PRIVATE_TEXT' }, message: 'PRIVATE_BODY' }, null,
    { 'PRIVATE_PERSON': 'PRIVATE_VALUE', text: 'PRIVATE_CONTACT', author: { name: 'PRIVATE_AUTHOR' } }];
  const diagnostic = diagnoseSocialApisPayload({ data: { items: rows } });
  assert.equal(diagnostic.item_fields_total, 4); assert.equal(diagnostic.item_fields_included, 4);
  assert.equal(diagnostic.item_fields_truncated, false);
  assert.equal(diagnostic.item_fields[2].type, 'null');
  assert.ok(diagnostic.item_fields[1].fields.some(field => field.name === 'message' && field.type === 'string'));
  assert.doesNotMatch(JSON.stringify(diagnostic), /PRIVATE_TEXT|PRIVATE_BODY|PRIVATE_PERSON|PRIVATE_VALUE|PRIVATE_CONTACT|PRIVATE_AUTHOR|private@example/u);
  const bounded = diagnoseSocialApisPayload({ data: { items: Array(200).fill(post) } });
  assert.ok(Buffer.byteLength(JSON.stringify(bounded)) <= 16_384);
  assert.equal(bounded.item_fields_truncated, true); assert.ok(bounded.item_fields_included < 200);
});

test('opt-in debug capture preserves complete JSON fields and values byte-for-byte without request metadata', () => {
  const directory = mkdtempSync(join(tmpdir(), 'socialapis-capture-'));
  try {
    // File IO test data only; not evidence of the live nested provider schema.
    const raw = '{\n  "unknown_property": {"text":"Nội dung riêng tư", "other_field":42}\n}\n';
    const capture = prepareSocialApisDebugResponse(directory);
    capture.save(raw, JSON.parse(raw), syntheticKey); capture.close();
    const stored = readFileSync(join(directory, 'socialapis-response.json'), 'utf8');
    assert.equal(stored, raw); assert.doesNotMatch(stored, /x-api-token|synthetic-never-real-test-key/u);
    assert.throws(() => prepareSocialApisDebugResponse(directory));
    assert.equal(readFileSync(join(directory, 'socialapis-response.json'), 'utf8'), raw);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('debug setup creates a missing nested directory and writes/reads the intended filename locally', () => {
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'socialapis-setup-'));
  const directory = join(temporaryRoot, 'browser-job-discovery', '.debug');
  try {
    assert.equal(existsSync(directory), false);
    const capture = prepareSocialApisDebugResponse(directory);
    const payload = { local_only: true, message: 'Kiểm tra ghi file, không gọi API' };
    const raw = JSON.stringify(payload);
    capture.save(raw, payload, syntheticKey); capture.close();
    const file = join(directory, 'socialapis-response.json');
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), payload);
    assert.throws(() => prepareSocialApisDebugResponse(directory), { code: 'EEXIST', syscall: 'open' });
    assert.equal(readFileSync(file, 'utf8'), raw);
  } finally { rmSync(temporaryRoot, { recursive: true, force: true }); }
});

test('debug capture refuses reflected credentials/authentication headers and removes unsaved files', () => {
  const directory = mkdtempSync(join(tmpdir(), 'socialapis-capture-'));
  try {
    for (const raw of [JSON.stringify({ message: syntheticKey }), JSON.stringify({ [syntheticKey]: {} }),
      JSON.stringify({ message: encodeURIComponent(syntheticKey) }),
      JSON.stringify({ message: syntheticKey }).replace('synthetic', '\\u0073ynthetic'),
      JSON.stringify({ headers: { 'X-Api-Token': 'OTHER_PRIVATE_KEY' } }),
      JSON.stringify({ headers: { Authorization: 'Bearer PRIVATE' } })]) {
      const capture = prepareSocialApisDebugResponse(directory);
      assert.throws(() => capture.save(raw, JSON.parse(raw), syntheticKey)); capture.close();
      assert.equal(existsSync(join(directory, 'socialapis-response.json')), false);
    }
    const capture = prepareSocialApisDebugResponse(directory); capture.close(); capture.close();
    assert.equal(existsSync(join(directory, 'socialapis-response.json')), false);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('debug capture rejects oversized body without leaving a capture', () => {
  const directory = mkdtempSync(join(tmpdir(), 'socialapis-capture-'));
  try {
    const capture = prepareSocialApisDebugResponse(directory);
    assert.throws(() => capture.save(' '.repeat(1_048_577), {}, syntheticKey)); capture.close();
    assert.equal(existsSync(join(directory, 'socialapis-response.json')), false);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('CLI accepts --debug-response without bypassing live approvals or making a request', () => {
  let stdout;
  try {
    execFileSync(process.execPath, [fileURLToPath(new URL('../src/socialapis-smoke-cli.mjs', import.meta.url)), '--debug-response'],
      { encoding: 'utf8', env: { ...process.env, SOCIALAPIS_API_KEY: '' }, stdio: 'pipe' });
    assert.fail('preflight should reject');
  } catch (error) {
    assert.equal(error.status, 1); stdout = error.stdout;
  }
  const report = JSON.parse(stdout);
  assert.equal(report.request_count, 0); assert.equal(report.debug_response_saved, false);
  assert.ok(report.blockers.includes('LIVE_OPT_IN_REQUIRED'));
});

test('captured nested item structure maps basic_info only, without private identity or invented publication date', () => {
  // Exact property structure observed in the local capture; ALL values below
  // are synthetic, not copied posts, contacts, author identities or real URLs.
  const item = {
    basic_info: { post_id: '7001', post_text: 'Tuyển Fresher .NET tại Hà Nội', post_hashtags: [],
      url: 'https://www.facebook.com/groups/fixture/permalink/7001/' },
    feedback_details: { reaction_count: 1, top_reactions: [{ reaction: 'Like', count: 1 }],
      total_shares: '0', total_comments: 0, video_play_count: null },
    owner_info: { owner_name: 'SYNTHETIC_PRIVATE_OWNER', owner_id: '8001', owner_url: null,
      owner_profile_picture: { uri: 'https://example.invalid/synthetic.png' } },
    video_details: {}
  };
  const payload = { data: { items: [item, structuredClone(item)], page_info: { cursor: 'SYNTHETIC_CURSOR' } },
    message: 'OK', meta: { statusCode: 200, duration: 1, creditsCharged: 1, creditsRemaining: 1, freeEndpoint: false } };
  const parsed = parseSocialApisSearchResponse(payload);
  assert.equal(parsed.path, '$.data.items'); assert.equal(parsed.rows.length, 2);
  const normalized = normalizeSocialApisItem(parsed.rows[0]);
  assert.equal(normalized.sourceExternalId, '7001'); assert.equal(normalized.rawContent, item.basic_info.post_text);
  assert.equal(normalized.sourceUrl, 'https://www.facebook.com/groups/fixture/posts/7001');
  assert.equal(normalized.permalinkSourceField, 'basic_info.url'); assert.equal(normalized.publishedAt, null);
  assert.equal(normalized.permalinkVerified, false); assert.equal(normalized.mappingStatus, 'PARTIAL_OBSERVED_FIELDS');
  assert.doesNotMatch(JSON.stringify(normalized), /SYNTHETIC_PRIVATE_OWNER|8001|synthetic.png/u);
  const summary = summarizeSocialApisRows(parsed.rows);
  assert.equal(summary.invalid, 0); assert.equal(summary.duplicates, 1); assert.equal(summary.candidates.length, 1);
  assert.equal(summary.candidates[0].classification, 'TARGET_MATCH');
  assert.equal(summary.candidates[0].field_types.post_id, 'string');
  assert.equal(summary.candidates[0].field_types.text, 'string');
  const conflict = normalizeSocialApisItem({ ...item, basic_info: { ...item.basic_info, post_id: '7002' } });
  assert.equal(conflict.idUrlConflict, true); assert.equal(conflict.sourceUrl, null);
  assert.equal(normalizeSocialApisItem({ ...item, basic_info: null }).mappingStatus, 'UNRECOGNIZED_ITEM_FIELDS');
  assert.equal(normalizeSocialApisItem({ basic_info: {}, owner_info: { owner_id: '8001' } }).sourceExternalId, null);
});

test('nested recruitment matching separates offers, job seekers, salary NET and multi-city roundups', () => {
  const texts = [
    'Hà Nội: Công ty tìm Fresher Lập trình viên SQL, C#.NET',
    'Em mong muốn tìm job Fresher .NET ở Hà Nội, nhờ đánh giá CV.',
    'Hà Nội tuyển intern C/C++, Python. Lương NET từ 12 triệu.',
    'HCM: tuyển Fresher .NET Developer. Hà Nội: Developer Intern/Fresher, chưa có stack.'
  ];
  const rows = texts.map((post_text, i) => ({ basic_info: { post_id: String(7001 + i), post_text,
    url: `https://www.facebook.com/groups/fixture/permalink/${7001 + i}/`, post_hashtags: [] },
    feedback_details: {}, owner_info: {}, video_details: {} }));
  const summary = summarizeSocialApisRows(rows);
  assert.equal(summary.invalid, 0);
  assert.deepEqual(summary.candidates.map(row => row.classification), ['TARGET_MATCH', 'NOT_MATCHED', 'NOT_MATCHED', 'INSUFFICIENT_DATA']);
  assert.equal(summary.candidates[3].location_assignment_ambiguous, true);
});
