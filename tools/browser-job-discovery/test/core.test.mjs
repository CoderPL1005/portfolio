import test from 'node:test';
import assert from 'node:assert/strict';
import { ActivityBudget, SearchPlanner, Stop, facebookAccessGate, requireMode, inspectAccess,
  JobPostExtractor, matchTarget, parseExperience, deduplicate, DiscoveryReporter, runDiscovery } from '../src/core.mjs';

const job = (overrides = {}) => ({ id: 'one', sourceUrl: 'http://fixture.invalid/posts/one', kind: 'group',
  title: 'Developer', employer: 'Synthetic Company', requirements: '.NET Angular. 1+ years experience.',
  location: 'Hanoi', body: 'Synthetic JD', hasImage: false, evidenceKind: 'SYNTHETIC_FIXTURE', ...overrides });

test('planner uses target inputs only, deduplicates and never accepts an employer/URL seed', () => {
  const target = { technologies: ['.NET', 'C#', 'ASP.NET Core', 'Angular', 'TypeScript', 'Extra'],
    locations: ['Hà Nội', 'Hanoi'], seniorities: ['Junior', 'Intern'], employer: 'DTMED', url: 'https://facebook.com/seed' };
  const queries = new SearchPlanner().plan(target);
  assert.equal(queries.length, 5);
  assert.ok(queries.every(q => !/DTMED|facebook|https/u.test(q)));
  assert.equal(queries[0], '.NET Junior Hà Nội');
  assert.throws(() => new SearchPlanner().plan({ ...target, technologies: [] }), /INVALID_TARGET/);
});

for (const [kind, cap] of [['searches', 5], ['candidates', 20]]) {
  test(`budget enforces ${cap} ${kind} including failed operation attempts`, () => {
    const budget = new ActivityBudget();
    for (let i = 0; i < cap; i++) budget.consume(kind);
    assert.throws(() => budget.consume(kind), /BUDGET_EXCEEDED/);
    assert.equal(budget.snapshot()[kind], cap);
  });
}
test('budget permits one session, idempotent release, deadline and no larger limits', () => {
  let now = 0; const budget = new ActivityBudget({}, () => now);
  const release = budget.acquireSession();
  assert.throws(() => budget.acquireSession(), /SESSION_BUDGET_EXCEEDED/);
  release(); release(); assert.equal(budget.snapshot().sessions, 0);
  now = 600_000; assert.throws(() => budget.consume('searches'), /TIME_BUDGET_EXCEEDED/);
  for (const limits of [{ searches: 6 }, { candidates: 21 }, { durationMs: 600_001 }, { sessions: 2 }, { searches: 0 }]) {
    assert.throws(() => new ActivityBudget(limits), /INVALID_BUDGET/);
  }
});

for (const snapshot of [
  { url: 'https://example.invalid/checkpoint' }, { url: 'https://example.invalid', marker: 'denied' },
  { url: 'https://example.invalid', hasCaptcha: true }, { url: 'https://example.invalid', body: 'Truy cập bị từ chối' }
]) test(`access restrictions stop: ${JSON.stringify(snapshot)}`, () => assert.throws(() => inspectAccess(snapshot), /ACCESS_RESTRICTED/));
for (const snapshot of [
  { url: 'https://example.invalid/login' }, { url: 'https://example.invalid', hasPassword: true },
  { url: 'https://example.invalid', body: 'Đăng nhập để tiếp tục' }
]) test(`login requirements stop: ${JSON.stringify(snapshot)}`, () => assert.throws(() => inspectAccess(snapshot), /LOGIN_REQUIRED/));

test('technical readiness and login readiness cannot grant Facebook authorization', () => {
  const gate = facebookAccessGate({ technicalReady: true, sessionReady: true, authorized: true });
  assert.equal(gate.allowed, false); assert.equal(gate.authorization, 'FACEBOOK_ACCESS_UNVERIFIED');
  assert.equal(gate.liveDiscovery, 'LIVE_UNSEEDED_DISCOVERY_NOT_RUN');
  assert.throws(() => requireMode('facebook'), /FACEBOOK_LIVE_COLLECTION_DISABLED/);
});

const experienceCases = [
  ['0–1 năm', 0, 1], ['0–2 years', 0, 2], ['6 tháng kinh nghiệm trở lên', 0.5, null],
  ['từ 1 năm kinh nghiệm', 1, null], ['at least 1 year', 1, null], ['1+ years', 1, null],
  ['1–2 years', 1, 2], ['up to 2 years', null, 2], ['2+ years', 2, null], ['3–5 years', 3, 5],
  ['no experience required', 0, null], ['không yêu cầu kinh nghiệm', 0, null], ['', null, null],
  ['Nice to have: 5 years experience.', null, null]
];
for (const [input, minimum, maximum] of experienceCases) {
  test(`experience preserves exact bounds: ${input || 'unknown'}`, () => {
    const actual = parseExperience(input);
    assert.equal(actual.minimum, minimum); assert.equal(actual.maximum, maximum); assert.equal(actual.conflict, false);
  });
}
test('contradictory mandatory experience fails closed', () => {
  assert.equal(parseExperience('0–1 years. At least 5 years.').conflict, true);
  assert.equal(matchTarget(job({ requirements: '.NET Angular. 0–1 years. At least 5 years.' })).reason, 'EXPERIENCE_CONFLICT');
});

const matchingCases = [
  ['junior', { title: 'Junior .NET Angular Developer', requirements: 'C# Angular' }, 'EXACT_ENTRY_LEVEL', 'EXPLICIT_SENIORITY'],
  ['0–1', { requirements: '.NET Angular. 0–1 years' }, 'POTENTIAL_ENTRY_LEVEL', 'EXPLICIT_LOW_MINIMUM'],
  ['0–2', { requirements: '.NET Angular. 0–2 years' }, 'POTENTIAL_ENTRY_LEVEL', 'EXPLICIT_LOW_MINIMUM'],
  ['6 months', { requirements: '.NET Angular. 6+ months' }, 'POTENTIAL_ENTRY_LEVEL', 'EXPLICIT_LOW_MINIMUM'],
  ['lower bound', {}, 'STRETCH_OPPORTUNITY', 'LOWER_BOUND_ONLY'],
  ['range', { requirements: '.NET Angular. 1–2 years' }, 'STRETCH_OPPORTUNITY', 'EXPLICIT_RANGE'],
  ['senior mentoring juniors', { title: 'Senior .NET Angular Developer', requirements: 'Mentor junior colleagues.' }, 'NOT_MATCHED', 'EXPERIENCE_NOT_IN_TARGET'],
  ['Junior conflict', { title: 'Junior .NET Angular Developer', requirements: '5+ years' }, 'NOT_MATCHED', 'EXPERIENCE_CONFLICT'],
  ['missing location', { location: '' }, 'NOT_MATCHED', 'LOCATION_UNVERIFIED'],
  ['remote not Hanoi', { location: 'Remote Vietnam' }, 'NOT_MATCHED', 'LOCATION_MISMATCH'],
  ['jQuery not Angular', { requirements: '.NET jQuery. 1+ years' }, 'NOT_MATCHED', 'TECHNOLOGY_MISMATCH'],
  ['AngularJS not Angular', { requirements: '.NET AngularJS. 1+ years' }, 'NOT_MATCHED', 'TECHNOLOGY_MISMATCH'],
  ['unknown experience', { requirements: '.NET Angular' }, 'NOT_MATCHED', 'SENIORITY_UNVERIFIED'],
  ['upper bound only', { requirements: '.NET Angular. up to 2 years' }, 'NOT_MATCHED', 'SENIORITY_UNVERIFIED'],
  ['network not .NET', { requirements: 'network Angular. 1+ years' }, 'NOT_MATCHED', 'TECHNOLOGY_MISMATCH']
];
for (const [name, input, classification, reason] of matchingCases) test(`matching V2: ${name}`, () => {
  const actual = matchTarget(job(input)); assert.equal(actual.classification, classification); assert.equal(actual.reason, reason);
  assert.equal(actual.eligibleForProductionIngestion, false);
});
test('backend profile and freshness retain missing evidence and original publication', () => {
  const input = job({ title: 'ASP.NET Internship', requirements: 'ASP.NET', publishedAt: '2021-05-11T00:00:00Z', modifiedAt: '2026-10-20T00:00:00Z' });
  const result = matchTarget(input, { profile: 'backend', today: new Date('2026-10-20T00:00:00Z') });
  assert.equal(result.classification, 'EXACT_ENTRY_LEVEL'); assert.equal(result.freshness, 'OLDER');
  assert.equal(matchTarget(job({ relativeTime: '1h ago' })).freshness, 'UNKNOWN');
  assert.equal(matchTarget(job({ publishedAt: 'invalid' })).freshness, 'UNKNOWN');
});
test('extractor hashes actual content, marks image-only posts without fabricating OCR', () => {
  const extractor = new JobPostExtractor(); const first = extractor.extract(job());
  assert.match(first.contentHash, /^[a-f0-9]{64}$/u);
  assert.equal(extractor.extract(job()).contentHash, first.contentHash);
  assert.notEqual(extractor.extract(job({ body: 'different content' })).contentHash, first.contentHash);
  assert.equal(extractor.extract(job({ hasImage: true, body: '', requirements: '', title: 'Recruitment image' })).extractionStatus, 'IMAGE_REQUIRES_EXTRACTION');
  assert.throws(() => extractor.extract(job({ body: 'x'.repeat(24_001) })), /POST_TOO_LARGE/);
});
test('dedup uses identity or actual normalized content; not just title/company', () => {
  const extract = input => new JobPostExtractor().extract(job(input));
  const rows = [extract({}), extract({}), extract({ id: 'alias', sourceUrl: 'http://fixture.invalid/alias' }),
    extract({ id: 'other', body: 'different vacancy' })];
  const result = deduplicate(rows); assert.equal(result.unique.length, 2); assert.equal(result.duplicates.length, 2);
});

function harness({ error = null, budget = new ActivityBudget(), stall = false } = {}) {
  const calls = { open: 0, close: 0, search: 0, read: 0 }; let release;
  const provider = { async open(b, signal) {
    calls.open++; release = b.acquireSession();
    signal.addEventListener('abort', () => release(), { once: true });
    return { page: {}, async close() { calls.close++; release(); } };
  } };
  return { calls, args: { sessionProvider: provider, budget,
    adapter: { async search(_, __, b) {
      calls.search++; b.consume('searches'); if (error) throw new Stop(error);
      if (stall) await new Promise(resolve => setTimeout(resolve, 30));
      b.check(); return ['http://fixture.invalid/posts/one'];
    }, async read(_, url, b) { calls.read++; b.consume('candidates'); return job({ sourceUrl: url }); } },
    planner: { plan: () => ['.NET Junior Hanoi', 'Angular Junior Hanoi'] },
    extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(), target: {} } };
}
for (const code of ['ACCESS_RESTRICTED', 'LOGIN_REQUIRED', 'NAVIGATION_FAILED']) test(`orchestrator stops immediately without retry on ${code}`, async () => {
  const { calls, args } = harness({ error: code }); const result = await runDiscovery(args);
  assert.equal(result.stopCode, code); assert.equal(calls.search, 1); assert.equal(calls.read, 0); assert.equal(calls.close, 1);
  assert.equal(result.budget.sessions, 0);
});
test('elapsed budget interrupts run, releases session and never retries', async () => {
  const { calls, args } = harness({ budget: new ActivityBudget({ durationMs: 10 }), stall: true });
  const report = await runDiscovery(args); assert.equal(report.stopCode, 'TIME_BUDGET_EXCEEDED');
  assert.equal(calls.read, 0); assert.equal(calls.close, 1); assert.equal(report.budget.sessions, 0);
});
test('orchestrator deduplicates repeated candidate URLs across queries', async () => {
  const { calls, args } = harness(); const report = await runDiscovery(args);
  assert.equal(calls.read, 1); assert.equal(report.metrics.uniquePosts, 1); assert.equal(report.gates.allowed, false);
  assert.equal(report.evidenceKind, 'OFFLINE_FIXTURE_ONLY');
});

test('orchestrator enforces reduced search and candidate caps before extra operations', async () => {
  const searchHarness = harness({ budget: new ActivityBudget({ searches: 1 }) });
  const searchReport = await runDiscovery(searchHarness.args);
  assert.equal(searchReport.stopCode, 'SEARCHES_BUDGET_EXCEEDED');
  assert.equal(searchReport.budget.searches, 1); assert.equal(searchReport.budget.candidates, 1);
  const candidateHarness = harness({ budget: new ActivityBudget({ candidates: 1 }) });
  candidateHarness.args.adapter.search = async (_, __, budget) => {
    budget.consume('searches'); return ['http://fixture.invalid/one', 'http://fixture.invalid/two'];
  };
  const candidateReport = await runDiscovery(candidateHarness.args);
  assert.equal(candidateReport.stopCode, 'CANDIDATES_BUDGET_EXCEEDED');
  assert.equal(candidateReport.budget.candidates, 1); assert.equal(candidateReport.budget.sessions, 0);
});
