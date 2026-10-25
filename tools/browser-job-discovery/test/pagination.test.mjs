import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { beginExecution, claimPage, performPage, acknowledgePersistence, calendarDay, configuration, workflowMetrics, reconcileTerminalFailure } from '../src/pagination-orchestrator.mjs';
import { SocialApisCaptureDiscoveryAdapter } from '../src/socialapis-adapter.mjs';
import { normalizeSocialApisItem } from '../src/socialapis-live-smoke.mjs';

const key = 'synthetic-pagination-test-key';
const profile = { technologies: ['.NET', 'ASP.NET Core'], locations: ['Hanoi', 'Hà Nội'], seniorities: ['Junior', 'Fresher', 'Intern'] };
const config = { query: 'C# .NET tuyển dụng Hà Nội', profile, maxRequests: 6, maxCredits: 6, target: 20 };
const now = new Date('2026-10-22T03:00:00Z');
const item = (id, body = `Hiring Junior .NET Developer\nLocation: Hanoi\nRequirements: ASP.NET Core\nJob reference: ${id}`) => ({ basic_info: {
  post_id: id, post_text: body, url: `https://www.facebook.com/fixture/posts/${id}` }, owner_info: {}, feedback_details: {}, video_details: {} });
const payload = (ids, cursor = null) => ({ data: { items: ids.map(id => typeof id === 'string' ? item(id) : id), page_info: { cursor } } });
function state(options = {}, previous = { evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] }, runId = '100') {
  return beginExecution(structuredClone(previous), { ...config, ...options }, runId);
}
async function page(s, body, { cost = '1', status = 200, transport, consent = true } = {}) {
  assert.equal(claimPage(s, now), true);
  const oldKey = process.env.SOCIALAPIS_API_KEY; process.env.SOCIALAPIS_API_KEY = key;
  let calls = 0; const requests = [];
  try {
    await performPage(s, { now, secret: key, receipt: '123', enabled: consent, freePlanConfirmed: consent, noCashConfirmed: consent,
      transport: async (url, options) => {
        calls++; requests.push({ url: new URL(url), redirect: options.redirect, credentials: options.credentials });
        if (transport) return transport(url, options);
        return new Response(JSON.stringify(body), { status, headers: { ...(cost === null ? {} : { 'X-SocialAPIs-Credits-Deducted': cost }),
          'X-SocialAPIs-Credits-Used': '999' } });
      } });
    if (s.orchestration.phase === 'COMPLETED_UNCONFIRMED') acknowledgePersistence(s, '456');
  } finally { if (oldKey === undefined) delete process.env.SOCIALAPIS_API_KEY; else process.env.SOCIALAPIS_API_KEY = oldKey; }
  return { calls, requests };
}

test('real cursor pagination sends end_cursor, keeps filters/Unicode, extracts and deduplicates each page', async () => {
  const s = state({ maxRequests: 3, maxCredits: 3, startTime: '2026-10-16', endTime: '2026-10-22' });
  const a = await page(s, payload(['1001'], 'sensitive-cursor-1'));
  const b = await page(s, payload(['1001', '1002'], 'sensitive-cursor-2'));
  const c = await page(s, payload(['1003']));
  assert.equal(a.requests[0].url.searchParams.has('end_cursor'), false);
  assert.equal(b.requests[0].url.searchParams.get('end_cursor'), 'sensitive-cursor-1');
  assert.equal(c.requests[0].url.searchParams.get('end_cursor'), 'sensitive-cursor-2');
  for (const result of [a,b,c]) {
    const { url, redirect, credentials } = result.requests[0];
    assert.equal(url.searchParams.get('query'), config.query); assert.equal(url.searchParams.get('start_time'), '2026-10-16');
    assert.equal(url.searchParams.get('end_time'), '2026-10-22'); assert.equal(redirect, 'manual'); assert.equal(credentials, 'omit');
  }
  assert.equal(s.jobs.length, 3); assert.deepEqual(s.orchestration.pages.map(p => p.returnedPostIds), [['1001'], ['1001','1002'], ['1003']]);
  assert.deepEqual(s.orchestration.pages.map(p => p.newlyInsertedIds), [['1001'], ['1002'], ['1003']]);
  assert.equal(s.orchestration.pages[1].overlap, 1); assert.equal(workflowMetrics(s).actualRequests, 3);
  assert.equal(workflowMetrics(s).totalDeductedCredits, 3); assert.equal(s.orchestration.pages[0].creditsUsedAlternateHeader, 999);
  assert.equal(s.orchestration.pages[0].freshnessUnknown, 1); assert.equal(s.jobs[0].publishedAt, null);
  assert.equal(s.jobs[0].match.freshness, 'UNKNOWN');
  assert.doesNotMatch(JSON.stringify(s), /sensitive-cursor|synthetic-pagination-test-key/u);
});

test('three repeated page-1 calls are NOT pages 1-3: source identities and new vacancy yield differ', async () => {
  const paginated = state({ maxRequests: 3 });
  for (const [id,cursor] of [['1101','next-1'], ['1102','next-2'], ['1103',null]]) await page(paginated, payload([id], cursor));
  let repeated = { evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] };
  for (let i=0;i<3;i++) { repeated = state({ maxRequests: 1 }, repeated, String(100+i)); await page(repeated, payload(['1101'], 'next-1')); }
  assert.equal(paginated.jobs.length, 3); assert.equal(repeated.jobs.length, 1);
  assert.equal(paginated.orchestration.newRelevantKeys.length, 3); assert.equal(repeated.orchestration.newRelevantKeys.length, 0);
});

for (const scenario of [
  { name: 'no cursor', body: payload(['1201']), reason: 'NO_CURSOR' }
]) test(`stop: ${scenario.name}`, async () => {
  const s = state(); await page(s, scenario.body);
  assert.equal(s.orchestration.stopReason, scenario.reason); assert.equal(claimPage(s, now), false);
});

for (const [name, firstPage] of [
  ['no matching vacancies', payload([item('1201', 'Hiring Senior Java Developer in Hanoi')], 'second-page')],
  ['no posts', payload([], 'second-page')]
]) test(`core page 1 with ${name} still reaches relevant page 2 within a two-request budget`, async () => {
  const s = state({ maxRequests: 2, maxCredits: 2 });
  const first = await page(s, firstPage);
  assert.equal(first.calls, 1); assert.equal(s.orchestration.pages[0].newRelevantVacancies, 0);
  assert.equal(s.orchestration.stopReason, null);
  assert.equal(s.orchestration.plan.allocation, 'CORE'); assert.equal(s.orchestration.plan.pageNumber, 2);
  const second = await page(s, payload(['1202'], 'unused-third-page'));
  assert.equal(second.calls, 1); assert.equal(second.requests[0].url.searchParams.get('end_cursor'), 'second-page');
  assert.deepEqual(s.orchestration.pages.map(p => p.newRelevantVacancies), [0, 1]);
  assert.deepEqual(s.orchestration.pages.map(p => p.pageNumber), [1, 2]);
  assert.equal(workflowMetrics(s).actualRequests, 2); assert.equal(workflowMetrics(s).totalDeductedCredits, 2);
  assert.equal(s.orchestration.stopReason, 'REQUEST_BUDGET'); assert.equal(claimPage(s, now), false);
});

test('zero-yield core pages ignore legacy minMarginal but never exceed four pages', async () => {
  const s = state({ minMarginal: 200 });
  for (let number = 1; number <= 4; number++) await page(s, payload([], `next-${number}`));
  assert.deepEqual(s.orchestration.pages.map(p => p.allocation), ['CORE', 'CORE', 'CORE', 'CORE']);
  assert.equal(s.orchestration.newRelevantKeys.length, 0); assert.equal(s.orchestration.stopReason, 'CORE_BUDGET');
  assert.equal(workflowMetrics(s).actualRequests, 4); assert.equal(claimPage(s, now), false);
});

for (const [options, reason] of [
  [{ maxRequests: 1 }, 'REQUEST_BUDGET'], [{ maxCredits: 1 }, 'CREDIT_BUDGET']
]) test(`zero-yield page still respects ${reason}`, async () => {
  const s = state(options); await page(s, payload([], 'valid-next-cursor'));
  assert.equal(s.orchestration.stopReason, reason); assert.equal(claimPage(s, now), false);
  assert.equal(workflowMetrics(s).actualRequests, 1);
});

test('repeated cursor and target stop early while duplicate posts stay deduplicated without blocking later pages', async () => {
  const repeat = state(); await page(repeat, payload(['1301'], 'repeat')); await page(repeat, payload(['1302'], 'repeat'));
  assert.equal(repeat.orchestration.stopReason, 'REPEATED_CURSOR'); assert.equal(repeat.requestLedger.length, 2);
  const duplicate = state(); await page(duplicate, payload(['1301'], 'one')); await page(duplicate, payload(['1301'], 'two'));
  assert.equal(duplicate.orchestration.stopReason, null); assert.equal(duplicate.jobs.length, 1);
  assert.equal(duplicate.orchestration.pages[1].newRelevantVacancies, 0);
  assert.equal(duplicate.orchestration.plan.pageNumber, 3);
  const enough = state({ target: 1, rotatingQuery: 'rotating', adaptiveQueries: ['adaptive'] });
  await page(enough, payload(['1301'], 'unused')); assert.equal(enough.orchestration.stopReason, 'SUFFICIENT_NEW_VACANCIES');
  assert.equal(enough.requestLedger.length, 1);
});

test('4-1-1 uses four core pages, one rotating, one adaptive selected from persisted yield, never seven', async () => {
  const s = state({ rotatingQuery: 'rotate', adaptiveQueries: ['weak', 'strong'] }, {
    evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [], discoveryYieldHistory: [
      { query: 'weak', newRelevantVacancies: 1, creditsDeducted: 1 }, { query: 'strong', newRelevantVacancies: 5, creditsDeducted: 1 }] });
  for (let i=1;i<=6;i++) await page(s, payload([String(1400+i)], `next-${i}`));
  assert.deepEqual(s.orchestration.pages.map(p => p.allocation), ['CORE','CORE','CORE','CORE','ROTATING','ADAPTIVE']);
  assert.deepEqual(s.orchestration.pages.map(p => p.pageNumber), [1,2,3,4,1,1]);
  assert.equal(s.orchestration.pages.at(-1).query, 'strong'); assert.equal(claimPage(s, now), false);
});

test('conditional extras only after poor core yield and only within separate request/credit budgets', async () => {
  const s = state({ rotatingQuery: 'rotate', adaptiveQueries: ['adapt'], maxCredits: 2 });
  await page(s, payload([])); assert.equal(s.orchestration.plan.allocation, 'ROTATING');
  await page(s, payload([])); assert.equal(s.orchestration.stopReason, 'CREDIT_BUDGET');
  assert.equal(s.requestLedger.length, 2); assert.equal(workflowMetrics(s).totalDeductedCredits, 2);
  const one = state({ maxRequests: 1 }); await page(one, payload(['1501'], 'unused'));
  assert.equal(one.orchestration.stopReason, 'REQUEST_BUDGET'); assert.equal(one.requestLedger.length, 1);
});

for (const scenario of [
  { name: 'unknown cost', cost: null, reason: 'PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED' },
  { name: 'overcharge', cost: '2', reason: 'PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED' },
  { name: 'insufficient credits HTTP', status: 402, reason: 'PROVIDER_HTTP_REJECTED' },
  { name: 'insufficient credits JSON', cost: '0', body: { error: { code: 'INSUFFICIENT_CREDITS' } }, reason: 'PROVIDER_ERROR' },
  { name: 'network failure', transport: () => { throw Error('secret provider detail'); }, reason: 'PROVIDER_REQUEST_FAILED' }
]) test(`${scenario.name} stops with no retry and no historical loss`, async () => {
  const s = state(); await page(s, payload(['1601'], 'next')); const old = structuredClone(s.jobs);
  const result = await page(s, scenario.body ?? payload(['1602']), scenario);
  assert.equal(result.calls, 1); assert.equal(s.orchestration.stopReason, scenario.reason);
  assert.equal(s.orchestration.phase, 'CLAIMED'); assert.deepEqual(s.jobs, old);
  assert.throws(() => claimPage(s, now), /AMBIGUOUS_PAGE/u);
  assert.doesNotMatch(JSON.stringify(s), /secret provider detail/u);
});

test('claim and result receipts are mandatory, crashes cannot silently re-fetch or reset state', async () => {
  const s = state(); claimPage(s, now);
  await assert.rejects(performPage(s, {}), /DURABLE_CLAIM_RECEIPT_REQUIRED/u);
  assert.throws(() => beginExecution(structuredClone(s), config, 'new'), /AMBIGUOUS_PAGE/u);
  assert.throws(() => acknowledgePersistence(s, '123'), /AMBIGUOUS_PAGE/u);
  const completed = state(); await page(completed, payload(['1701'], 'next'));
  completed.orchestration.phase = 'COMPLETED_UNCONFIRMED';
  assert.throws(() => beginExecution(completed, config, 'new'), /PERSISTENCE_CONFIRMATION_REQUIRED/u);
  acknowledgePersistence(completed, '999'); beginExecution(completed, config, 'new', { resumeContinuation: true, now });
  assert.equal(completed.orchestration.plan.pageNumber, 2); assert.equal(completed.jobs.length, 1);
  assert.throws(() => acknowledgePersistence(completed, ''), /DURABLE_RESULT_RECEIPT_REQUIRED/u);
  assert.throws(() => beginExecution(completed, { ...config, query: 'changed' }, 'new'), /CONFIG_MISMATCH/u);
});

test('historical states retain identities, raw body/hash/dates and extraction compatibility', async () => {
  const s = state(); await page(s, payload(['1801']));
  const previous = structuredClone(s.jobs[0]);
  const expectedHash = createHash('sha256').update(['','','','',previous.body].join('\n').trim()).digest('hex');
  assert.equal(previous.contentHash, expectedHash); assert.equal(previous.sourceIdentity, 'SOCIALAPIS:1801');
  const legacy = { evidenceKind: 'LIVE_PROVIDER_RESPONSE', jobs: [previous], legacyEvidence: { retain: true } };
  const next = state({ maxRequests: 1 }, legacy); await page(next, payload(['1801']));
  assert.equal(next.jobs.length, 1); assert.equal(next.jobs[0].contentHash, previous.contentHash);
  assert.equal(next.jobs[0].body, previous.body); assert.equal(next.jobs[0].publishedAt, null);
  assert.deepEqual(next.legacyEvidence, legacy.legacyEvidence); assert.deepEqual(next.orchestration.pages[0].newlyInsertedIds, []);
});

test('production six/day counts conservative reservations in Vietnam calendar, unattended gate fails closed', () => {
  assert.equal(calendarDay(new Date('2026-10-21T17:00:00Z')), '2026-10-22');
  assert.equal(calendarDay(new Date('2026-10-21T16:59:59Z')), '2026-10-21');
  assert.throws(() => state({ mode: 'production' }), /MONTHLY_FREE_BALANCE_UNVERIFIED/u);
  const s = beginExecution({ jobs: [], requestLedger: Array.from({length: 6}, () => ({ day: '2026-10-22', actualRequests: null })) },
    { ...config, mode: 'production' }, '100', { productionGuard: () => true });
  assert.equal(claimPage(s, now, { productionGuard: () => true }), false);
  assert.equal(s.orchestration.stopReason, 'DAILY_REQUEST_BUDGET');
});

test('all consent gates, generic profile boundary and invalid budgets fail before transport', async () => {
  for (const maxRequests of [0,7,1.5]) assert.throws(() => state({ maxRequests }), /INVALID_ORCHESTRATION_BUDGET/u);
  assert.throws(() => configuration({ query: 'anything' }), /CANDIDATE_PROFILE_REQUIRED/u);
  const s = state(); assert.equal((await page(s, payload(['1901']), { consent: false })).calls, 0);
  assert.equal(s.orchestration.stopReason, 'SOCIALAPIS_LIVE_APPROVAL_REQUIRED');
  const angular = state({ profile: { ...profile, technologies: ['Angular'] } });
  await page(angular, payload([item('1902', 'Hiring Junior Angular Developer\nLocation: Hanoi\nRequirements: Angular')]));
  assert.equal(angular.orchestration.pages[0].relevantVacancies, 1);
});

test('timeout and concurrent invocation cannot trigger retries or two HTTP calls', async () => {
  const s=state(); claimPage(s,now); let calls=0;
  const previousKey=process.env.SOCIALAPIS_API_KEY; process.env.SOCIALAPIS_API_KEY=key;
  try {
    const options={now,secret:key,receipt:'123',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,timeoutMs:10,
      transport:async()=>{calls++; return new Promise(()=>{});}};
    const first=performPage(s,options);
    await assert.rejects(performPage(s,options), /DURABLE_CLAIM_RECEIPT_REQUIRED/u);
    await first;
    assert.equal(calls,1); assert.equal(s.orchestration.stopReason,'DISCOVERY_TIMEOUT');
    assert.equal(s.orchestration.phase,'CLAIMED'); assert.equal(workflowMetrics(s).totalDeductedCredits,null);
    assert.throws(()=>claimPage(s,now), /AMBIGUOUS_PAGE/u);
  } finally { if(previousKey===undefined) delete process.env.SOCIALAPIS_API_KEY; else process.env.SOCIALAPIS_API_KEY=previousKey; }
});

test('ambiguous reservation consumes conservative daily capacity, day rollover does not send under the old day', async () => {
  const s=state(); claimPage(s,now);
  assert.equal(workflowMetrics(s).actualRequests,null); assert.equal(workflowMetrics(s).uncertainAttempts,1);
  let calls=0;
  await assert.rejects(performPage(s,{receipt:'123',now:new Date('2026-10-22T17:00:00Z'),transport:()=>{calls++;}}), /RESERVATION_DAY_CHANGED_NO_REQUEST/u);
  assert.equal(calls,0); assert.equal(s.orchestration.phase,'CLAIMED');
});

test('partial invalid rows are isolated per page, no malformed candidate is persisted', async () => {
  const s=state(); await page(s,payload(['2001', {basic_info:{post_id:'2002',post_text:'Hiring',url:'https://evil.invalid/posts/2002'}}]));
  assert.equal(s.jobs.length,1); const p=s.orchestration.pages[0];
  assert.equal(p.totalItems,2); assert.equal(p.acceptedItems,1); assert.equal(p.rejectedItems,1);
  assert.equal(p.rejectionReasons[0].reason,'POST_URL_INVALID_OR_UNSUPPORTED');
});

test('fractional deducted credits remain separate from actual requests and alternate usage', async () => {
  const s=state({maxRequests:2,maxCredits:2}); await page(s,payload(['2101'],'next'),{cost:'0.5'});
  await page(s,payload(['2102']),{cost:'0.25'});
  assert.equal(workflowMetrics(s).actualRequests,2); assert.equal(workflowMetrics(s).totalDeductedCredits,0.75);
  assert.equal(s.orchestration.pages[0].creditsUsedAlternateHeader,999);
});

test('adaptive allocation requires verified persisted yield rather than inventing an exploration query', async () => {
  const s=state({adaptiveQueries:['unmeasured']}); await page(s,payload([]));
  assert.equal(s.requestLedger.length,1); assert.equal(s.orchestration.stopReason,'NO_CURSOR');
});

test('profile matcher reuses verified district evidence without requiring literal Hanoi in address', async () => {
  const s=state(); await page(s,payload([item('2201','Hiring Fresher .NET Developer\nRequirements: ASP.NET Core\nNam Từ Liêm')]));
  assert.equal(s.orchestration.pages[0].relevantVacancies,1);
  assert.equal(s.jobs[0].vacancies[0].match.locationVerified,true);
});

test('explicit provider has_next=false and invalid cursor never start a second page', async () => {
  for(const next of [' ', 'bad\ncursor', 'a'.repeat(4097), null]) {
    const s=state(); await page(s,payload(['2301'],next)); assert.equal(s.orchestration.stopReason,'NO_CURSOR');
  }
  const s=state(); const body=payload(['2302'],'present-but-terminal'); body.data.page_info.has_next=false;
  await page(s,body); assert.equal(s.orchestration.stopReason,'NO_CURSOR');
});

async function twoPageCheckpoint(options = {}) {
  const s = state({ maxRequests: 2, maxCredits: 2, ...options });
  await page(s, payload(['3001'], 'cursor-page-2'));
  await page(s, payload(['3002'], 'cursor-page-3'));
  return JSON.parse(JSON.stringify(s)); // Exercise the actual serialized checkpoint.
}
const resume = (s, options = {}, flags = {}) => beginExecution(s, { ...s.orchestration.config, ...options }, '101',
  { resumeContinuation: true, now, ...flags });

test('request-budget checkpoint stores encrypted page 3 and next workflow resumes without repeating pages 1-2', async () => {
  const s = await twoPageCheckpoint(); const oldJobs = structuredClone(s.jobs); const oldLedger = structuredClone(s.requestLedger);
  const logicalId = s.orchestration.logicalExecutionId;
  assert.equal(s.orchestration.status, 'STOPPED_RESUMABLE'); assert.equal(s.orchestration.continuation.plan.pageNumber, 3);
  assert.equal(s.orchestration.pages[1].continuationAvailable, true);
  assert.doesNotMatch(JSON.stringify(s), /cursor-page-/u);
  resume(s); assert.equal(s.orchestration.status, 'RUNNING'); assert.equal(s.orchestration.logicalExecutionId, logicalId);
  assert.equal(workflowMetrics(s).actualRequests, 0); assert.equal(workflowMetrics(s).logicalExecution.actualRequests, 2);
  assert.deepEqual(s.jobs, oldJobs); assert.deepEqual(s.requestLedger, oldLedger);
  const third = await page(s, payload(['3001','3003'], 'cursor-page-4'));
  assert.equal(third.requests[0].url.searchParams.get('end_cursor'), 'cursor-page-3');
  assert.equal(third.calls, 1); assert.equal(s.jobs.length, 3); assert.deepEqual(s.orchestration.pages[2].newlyInsertedIds, ['3003']);
  await page(s, payload(['3004'], 'cursor-page-5'));
  assert.deepEqual(s.orchestration.pages.map(p => p.pageNumber), [1,2,3,4]);
  const metrics = workflowMetrics(s);
  assert.equal(metrics.actualRequests, 2); assert.equal(metrics.totalDeductedCredits, 2);
  assert.equal(metrics.logicalExecution.actualRequests, 4); assert.equal(metrics.logicalExecution.creditsDeducted, 4);
  assert.equal(metrics.workflowRunId, '101'); assert.equal(metrics.logicalExecutionId, logicalId);
  assert.equal(s.orchestration.status, 'STOPPED_TERMINAL'); assert.equal(metrics.continuationAvailable, false);
  assert.throws(() => beginExecution(s, s.orchestration.config, '102', { resumeContinuation: true, now }), /CONTINUATION_UNAVAILABLE/u);
  assert.equal(claimPage(s, now), false);
});

test('credit budget is per workflow, while four-core-page ceiling spans workflows', async () => {
  const s = await twoPageCheckpoint({ maxRequests: 6, maxCredits: 2 });
  assert.equal(s.orchestration.stopReason, 'CREDIT_BUDGET'); assert.equal(s.orchestration.status, 'STOPPED_RESUMABLE');
  resume(s, { maxRequests: 1, maxCredits: 1 }); await page(s, payload(['3103'], 'fourth'));
  assert.equal(workflowMetrics(s).actualRequests, 1); assert.equal(workflowMetrics(s).logicalExecution.creditsDeducted, 3);
  beginExecution(s, { ...s.orchestration.config, maxRequests: 6, maxCredits: 6 }, '102', { resumeContinuation: true, now });
  await page(s, payload(['3104'], 'must-not-be-used'));
  assert.equal(s.orchestration.stopReason, 'CORE_BUDGET'); assert.equal(s.orchestration.status, 'STOPPED_TERMINAL');
  assert.equal(workflowMetrics(s).actualRequests, 1); assert.equal(s.requestLedger.length, 4);
});

test('resume policy mismatch fails closed for query, dates, profile and target, without state mutation', async () => {
  const original = await twoPageCheckpoint();
  for (const changed of [{ query: 'other query' }, { startTime: '2026-10-01' }, { endTime: '2026-10-22' },
    { profile: { ...profile, technologies: ['Angular'] } }, { target: 1 }, { rotatingQuery: 'other' }]) {
    const s = structuredClone(original); const before = JSON.stringify(s);
    assert.throws(() => resume(s, changed), /CONTINUATION_POLICY_MISMATCH/u); assert.equal(JSON.stringify(s), before);
  }
  assert.throws(() => resume(original, {}, { now: new Date('2026-10-23T03:00:00Z') }), /CONTINUATION_EXPIRED/u);
  assert.throws(() => beginExecution(original, original.orchestration.config, '100', { resumeContinuation: true, now }), /RESUME_REQUIRES_NEW_WORKFLOW/u);
});

test('missing, malformed or repeated continuation cannot silently start a new query/page 1', async () => {
  const original = await twoPageCheckpoint();
  for (const mutate of [s => { s.orchestration.continuation = null; }, s => { s.orchestration.continuation.plan.cursor = null; },
    s => { s.orchestration.continuation.plan.cursor.iv = 'invalid'; }, s => { s.orchestration.continuation.plan.pageNumber = 1; },
    s => { s.orchestration.usedCursorHashes.push(s.orchestration.continuation.plan.cursorHash); },
    s => { s.orchestration.pages[0].inputCursorFingerprint = s.orchestration.continuation.plan.cursorHash; }]) {
    const s = structuredClone(original); mutate(s); const before = JSON.stringify(s);
    assert.throws(() => resume(s), /CONTINUATION_(UNAVAILABLE|INVALID)/u); assert.equal(JSON.stringify(s), before);
  }
});

test('authenticated cursor corruption fails before transport, not by refetching page 1', async () => {
  const s = await twoPageCheckpoint();
  s.orchestration.continuation.plan.cursor.tag = Buffer.alloc(16).toString('base64');
  s.orchestration.plan = structuredClone(s.orchestration.continuation.plan);
  resume(s); const result = await page(s, payload(['3203']));
  assert.equal(result.calls, 0); assert.equal(s.orchestration.stopReason, 'CURSOR_CHECKPOINT_UNREADABLE');
  assert.equal(s.jobs.length, 2); assert.equal(s.orchestration.phase, 'CLAIMED');
});

test('ambiguous and unconfirmed checkpoints block explicit resume', async () => {
  const original = await twoPageCheckpoint();
  for (const phase of ['CLAIMED','IN_FLIGHT','COMPLETED_UNCONFIRMED']) {
    const s = structuredClone(original); s.orchestration.phase = phase;
    assert.throws(() => resume(s), /AMBIGUOUS_PAGE|PERSISTENCE_CONFIRMATION_REQUIRED/u);
  }
  const s = structuredClone(original); delete s.orchestration.pages[0].artifactReceipt;
  assert.throws(() => resume(s), /PERSISTENCE_CONFIRMATION_REQUIRED/u);
});

test('Run 7 legacy-style budget stop with page-2 input cursor is NOT a page-3 continuation', async () => {
  const s = await twoPageCheckpoint(); delete s.orchestration.schemaVersion; delete s.orchestration.continuation;
  delete s.orchestration.status; s.orchestration.plan.pageNumber = 2;
  const before = JSON.stringify(s);
  assert.throws(() => resume(s), /LEGACY_CONTINUATION_CURSOR_UNAVAILABLE/u); assert.equal(JSON.stringify(s), before);
  assert.throws(() => beginExecution({ jobs: [] }, config, '101', { resumeContinuation: true, now }), /LEGACY_CONTINUATION_CURSOR_UNAVAILABLE/u);
});

for (const [id, url] of [
  ['4001','https://www.facebook.com/fixture/posts/4001?ref=search'],
  ['4001','https://m.facebook.com/groups/fixture/permalink/4001/'],
  ['4001','https://facebook.com/groups/fixture/posts/4001'],
  ['4001','https://facebook.com/story.php?story_fbid=4001&id=9000&ref=search'],
  ['4001','https://facebook.com/permalink.php?id=9000&story_fbid=4001'],
  ['pfbidFixture123','https://www.facebook.com/fixture/posts/pfbidFixture123']
]) test(`existing supported URL/ID pair normalizes without inferring identity: ${url}`, () => {
  const row = item(id); row.basic_info.url = url;
  const a = new SocialApisCaptureDiscoveryAdapter(payload([row]));
  assert.equal(a.itemStats.acceptedItems, 1); assert.equal(a.itemStats.rejectedItems, 0);
  assert.equal(normalizeSocialApisItem(row).idUrlConflict, false);
});

test('mismatch diagnostics isolate real mismatches, opaque ID aliases and malformed IDs without logging values', async () => {
  const different = item('4101'); different.basic_info.url = 'https://facebook.com/fixture/posts/4102?token=SENSITIVE_QUERY';
  const opaque = item('4103'); opaque.basic_info.url = 'https://facebook.com/fixture/posts/pfbidUnverified';
  const malformed = item('9999_8888');
  const missing = item('4104'); delete missing.basic_info.post_id;
  const multi = item('4105'); multi.basic_info.url = 'https://facebook.com/story.php?story_fbid=4105&story_fbid=4106&id=9000';
  const s = state({maxRequests:1}); await page(s, payload(['4100',different,opaque,malformed,missing,multi]));
  const p = s.orchestration.pages[0]; assert.equal(p.acceptedItems,1); assert.equal(p.rejectedItems,5);
  assert.deepEqual(p.rejectionDiagnostics.map(d=>d.mismatchType), ['DIFFERENT_POST_IDS','DIFFERENT_ID_REPRESENTATIONS_UNVERIFIED','MALFORMED_PROVIDER_ID','MISSING_PROVIDER_ID','NOT_AN_ID_MISMATCH']);
  assert.equal(p.rejectionDiagnostics[0].pageNumber,1); assert.equal(p.rejectionDiagnostics[0].index,1);
  assert.equal(p.rejectionDiagnostics[0].providerIdFingerprint,createHash('sha256').update('4101').digest('hex'));
  assert.equal(p.rejectionDiagnostics[0].urls[0].urlIdFingerprint,createHash('sha256').update('4102').digest('hex'));
  assert.equal(p.rejectionDiagnostics[4].urls[0].ambiguousQuery,true);
  assert.doesNotMatch(JSON.stringify(p.rejectionDiagnostics), /SENSITIVE_QUERY|https:|pfbidUnverified|Hiring|fixture\/posts/u);
});

test('all-invalid provider page keeps rejection diagnostics in durable failed attempt rather than losing evidence', async () => {
  const s=state(); const bad=item('4201'); bad.basic_info.url='https://facebook.com/fixture/posts/4202';
  await page(s,payload([bad]));
  assert.equal(s.orchestration.phase,'TERMINAL_UNCONFIRMED'); assert.equal(s.orchestration.stopReason,'NO_VALID_PROVIDER_ITEMS');
  const attempt=s.requestLedger.at(-1); assert.equal(attempt.itemStats.rejectedItems,1);
  assert.equal(attempt.rejectionDiagnostics[0].pageNumber,1); assert.equal(attempt.rejectionDiagnostics[0].reason,'POST_ID_URL_MISMATCH');
  assert.equal(s.jobs.length,0);
});

const terminalProof = (s, overrides={}) => ({artifactId:'456',claimArtifactId:'123',workflowRunId:s.orchestration.workflowRunId,
  attemptIndex:s.orchestration.attemptIndex,artifactKind:'RESULT',provenance:'ACTIONS_RESULT',
  artifactName:`socialapis-page-${s.orchestration.workflowRunId}-1-result`,
  stateFingerprint:createHash('sha256').update(JSON.stringify(s)).digest('hex'),...overrides});

test('known terminal result reconciles with bound result evidence without rewriting ledger, registry or refetching',async()=>{
  const historical=state().jobs; const s=state();const bad=item('5101');bad.basic_info.url='https://facebook.com/fixture/posts/pfbidUnverified';
  const result=await page(s,payload(Array.from({length:5},()=>structuredClone(bad))));
  assert.equal(result.calls,1);const ledger=structuredClone(s.requestLedger),jobs=structuredClone(s.jobs);
  assert.throws(()=>beginExecution(structuredClone(s),config,'200'),/PERSISTENCE_CONFIRMATION_REQUIRED/u);
  const proof=terminalProof(s);reconcileTerminalFailure(s,proof);
  assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');assert.equal(s.orchestration.status,'STOPPED_TERMINAL');
  assert.equal(s.orchestration.stopReason,'NO_VALID_PROVIDER_ITEMS');assert.deepEqual(s.requestLedger,ledger);assert.deepEqual(s.jobs,jobs);
  assert.deepEqual(jobs,historical);assert.equal(s.requestLedger.at(-1).rejectionDiagnostics.length,5);
  assert.equal(workflowMetrics(s).actualRequests,1);assert.equal(workflowMetrics(s).totalDeductedCredits,1);
  assert.equal(workflowMetrics(s).logicalExecution.actualRequests,1);assert.equal(claimPage(s,now),false);
  await assert.rejects(()=>performPage(s,{receipt:'123',transport:()=>{throw Error('MUST_NOT_REFETCH');}}),/DURABLE_CLAIM_RECEIPT_REQUIRED/u);
  assert.equal(s.terminalReconciliations[0].artifactId,'456');
  beginExecution(s,config,'200');assert.equal(s.orchestration.phase,'READY');assert.deepEqual(s.requestLedger,ledger);
});

test('legacy CLAIMED all-invalid result can reconcile without inventing missing HTTP metadata',async()=>{
  const s=state();const bad=item('5151');bad.basic_info.url='https://facebook.com/fixture/posts/pfbidUnknown';await page(s,payload([bad]));
  s.orchestration.phase='CLAIMED';delete s.requestLedger.at(-1).httpStatus;delete s.requestLedger.at(-1).claimArtifactReceipt;
  const before=structuredClone(s.requestLedger);
  reconcileTerminalFailure(s,terminalProof(s));assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');
  assert.deepEqual(s.requestLedger,before);assert.equal(s.requestLedger.at(-1).httpStatus,undefined);
});

test('legacy result slot counts attempts in this workflow, not the global ledger index',async()=>{
  const s=state();
  for(let i=0;i<6;i++)s.requestLedger.push({workflowRunId:String(90+i),actualRequests:1,creditsDeducted:1});
  const bad=item('5161');bad.basic_info.url='https://facebook.com/fixture/posts/pfbidUnknown';await page(s,payload([bad]));
  s.orchestration.phase='CLAIMED';delete s.requestLedger.at(-1).claimArtifactName;delete s.requestLedger.at(-1).claimArtifactReceipt;
  assert.equal(s.orchestration.attemptIndex,6);
  for(const provenance of ['ACTIONS_RESULT','LOCAL_OBSERVED_RESULT_HASH']) {
    const copy=structuredClone(s);
    assert.throws(()=>reconcileTerminalFailure(copy,terminalProof(copy,{provenance,artifactName:'socialapis-page-100-2-result'})),/RECONCILIATION/u);
    assert.deepEqual(copy,s);
  }
  const ledger=structuredClone(s.requestLedger);
  reconcileTerminalFailure(s,terminalProof(s,{artifactName:'socialapis-page-100-1-result'}));
  assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');assert.deepEqual(s.requestLedger,ledger);
});

test('legacy rotating result requires slot two even though its query page number is one',async()=>{
  const s=state({rotatingQuery:'Python Backend Fresher Hanoi'});
  await page(s,payload(['5170']));
  assert.equal(s.orchestration.plan.allocation,'ROTATING');assert.equal(s.orchestration.plan.pageNumber,1);
  const bad=item('5171');bad.basic_info.url='https://facebook.com/fixture/posts/pfbidUnknown';await page(s,payload([bad]));
  s.orchestration.phase='CLAIMED';delete s.requestLedger.at(-1).claimArtifactName;delete s.requestLedger.at(-1).claimArtifactReceipt;
  const copy=structuredClone(s);
  assert.throws(()=>reconcileTerminalFailure(copy,terminalProof(copy,{artifactName:'socialapis-page-100-1-result'})),/RECONCILIATION/u);
  assert.deepEqual(copy,s);
  const ledger=structuredClone(s.requestLedger),jobs=structuredClone(s.jobs);
  reconcileTerminalFailure(s,terminalProof(s,{artifactName:'socialapis-page-100-2-result'}));
  assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');assert.deepEqual(s.requestLedger,ledger);assert.deepEqual(s.jobs,jobs);
});

test('terminal reconciliation rejects missing, claim-only, wrong-run and stale result evidence',async()=>{
  const s=state();const bad=item('5201');bad.basic_info.url='https://facebook.com/fixture/posts/5202';await page(s,payload([bad]));
  for(const patch of [{artifactId:''},{claimArtifactId:'456'},{workflowRunId:'999'},{attemptIndex:99},{artifactKind:'CLAIM'},{artifactName:undefined},
    {artifactName:'socialapis-page-100-1-claim'},{artifactName:'socialapis-page-999-1-result'},{stateFingerprint:'0'.repeat(64)}]) {
    const copy=structuredClone(s);assert.throws(()=>reconcileTerminalFailure(copy,terminalProof(copy,patch)),/RECONCILIATION/u);assert.deepEqual(copy,s);
  }
});

test('numeric/PFBID aliases remain rejected, exact IDs stay unverified candidates and true URL conflicts stay rejected',()=>{
  const numeric=item('5401');numeric.basic_info.url='https://www.facebook.com/fixture/posts/pfbidUnknownAlias';
  const conflicting={post_id:'5402',text:'Synthetic recruitment',url:'https://www.facebook.com/fixture/posts/5402',post_url:'https://www.facebook.com/fixture/posts/5403'};
  const unsupported=item('5404');unsupported.basic_info.url='https://www.facebook.com/search/posts/?q=Backend';
  const opaque=item('pfbidSupported');
  const adapter=new SocialApisCaptureDiscoveryAdapter(payload([numeric,conflicting,unsupported,opaque]));
  assert.equal(adapter.itemStats.acceptedItems,1);assert.equal(adapter.itemStats.rejectedItems,3);
  assert.deepEqual(adapter.itemStats.rejectionReasons.map(r=>r.reason),['POST_ID_URL_MISMATCH','POST_ID_URL_MISMATCH','POST_URL_INVALID_OR_UNSUPPORTED']);
  assert.equal(adapter.records.values().next().value.id,'pfbidSupported');
  assert.equal(normalizeSocialApisItem(opaque).permalinkVerified,false);
});

test('known terminal ACK needs result provenance; the same bound claim name cannot acknowledge another slot',async()=>{
  const s=state();const bad=item('5501');bad.basic_info.url='https://facebook.com/fixture/posts/5502';await page(s,payload([bad]));
  s.requestLedger.at(-1).claimArtifactName='socialapis-page-100-1-claim';
  assert.throws(()=>acknowledgePersistence(structuredClone(s),'456'),/RECONCILIATION/u);
  assert.throws(()=>acknowledgePersistence(structuredClone(s),'456',terminalProof(s,{artifactName:'socialapis-page-100-2-result'})),/RECONCILIATION/u);
  acknowledgePersistence(s,'456',terminalProof(s));assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');
});

test('unknown charge, timeout, in-flight and incomplete diagnostics remain ambiguous even with a result receipt',async()=>{
  const original=state();const bad=item('5301');bad.basic_info.url='https://facebook.com/fixture/posts/5302';await page(original,payload([bad]));
  for(const change of [s=>{s.requestLedger.at(-1).creditsDeducted=null;},s=>{s.orchestration.stopReason='DISCOVERY_TIMEOUT';},
    s=>{s.orchestration.phase='IN_FLIGHT';},s=>{s.requestLedger.at(-1).actualRequests=null;},s=>{s.requestLedger.at(-1).rejectionDiagnostics=[];}]) {
    const s=structuredClone(original);change(s);const before=structuredClone(s);
    assert.throws(()=>reconcileTerminalFailure(s,terminalProof(s)),/RECONCILIATION/u);assert.deepEqual(s,before);
  }
});
