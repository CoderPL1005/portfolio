import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { beginExecution, claimPage, performPage, acknowledgePersistence, calendarDay, configuration, workflowMetrics } from '../src/pagination-orchestrator.mjs';

const key = 'synthetic-pagination-test-key';
const profile = { technologies: ['.NET', 'ASP.NET Core'], locations: ['Hanoi', 'Hà Nội'], seniorities: ['Junior', 'Fresher', 'Intern'] };
const config = { query: 'C# .NET tuyển dụng Hà Nội', profile, maxRequests: 6, maxCredits: 6, target: 20 };
const now = new Date('2026-10-22T03:00:00Z');
const item = (id, body = 'Hiring Junior .NET Developer\nLocation: Hanoi\nRequirements: ASP.NET Core') => ({ basic_info: {
  post_id: id, post_text: body, url: `https://www.facebook.com/fixture/posts/${id}` }, owner_info: {}, feedback_details: {}, video_details: {} });
const payload = (ids, cursor = null) => ({ data: { items: ids.map(id => typeof id === 'string' ? item(id) : id), page_info: { cursor } } });
function state(options = {}, previous = { evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] }) {
  return beginExecution(structuredClone(previous), { ...config, ...options }, '100');
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
  for (let i=0;i<3;i++) { repeated = state({ maxRequests: 1 }, repeated); await page(repeated, payload(['1101'], 'next-1')); }
  assert.equal(paginated.jobs.length, 3); assert.equal(repeated.jobs.length, 1);
  assert.equal(paginated.orchestration.newRelevantKeys.length, 3); assert.equal(repeated.orchestration.newRelevantKeys.length, 0);
});

for (const scenario of [
  { name: 'no cursor', body: payload(['1201']), reason: 'NO_CURSOR' },
  { name: 'empty next page', body: payload([], 'next'), reason: 'EMPTY_PAGE' },
  { name: 'zero relevant vacancies', body: payload([item('1201', 'Hiring Senior Java Developer in Hanoi')], 'next'), reason: 'LOW_MARGINAL_YIELD' }
]) test(`stop: ${scenario.name}`, async () => {
  const s = state(); await page(s, scenario.body);
  assert.equal(s.orchestration.stopReason, scenario.reason); assert.equal(claimPage(s, now), false);
});

test('repeated cursor, duplicate posts and sufficient-result early stop do not spend unused credits', async () => {
  const repeat = state(); await page(repeat, payload(['1301'], 'repeat')); await page(repeat, payload(['1302'], 'repeat'));
  assert.equal(repeat.orchestration.stopReason, 'REPEATED_CURSOR'); assert.equal(repeat.requestLedger.length, 2);
  const duplicate = state(); await page(duplicate, payload(['1301'], 'one')); await page(duplicate, payload(['1301'], 'two'));
  assert.equal(duplicate.orchestration.stopReason, 'LOW_MARGINAL_YIELD'); assert.equal(duplicate.jobs.length, 1);
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
  acknowledgePersistence(completed, '999'); beginExecution(completed, config, 'new');
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
