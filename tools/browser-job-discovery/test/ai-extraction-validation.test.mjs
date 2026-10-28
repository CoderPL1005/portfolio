import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import net from 'node:net';
import { extractRecruitmentPost } from '../src/ai-job-extraction.mjs';
import { extractionJsonSchema } from '../src/ai-extraction-schema.mjs';
import { MockExtractionProvider } from '../src/ai-extraction-mock-provider.mjs';
import { compareExtraction } from '../src/ai-extraction-evaluation.mjs';
import { sourceInput, mockOutput, mockProvider, fact, address, applicationUrl } from './fixtures/ai-extraction-v1.mjs';

const originalConnect = net.Socket.prototype.connect, originalFetch = globalThis.fetch;
test.before(() => {
  net.Socket.prototype.connect = () => { throw Error('REAL_NETWORK_FORBIDDEN'); };
  globalThis.fetch = () => { throw Error('REAL_NETWORK_FORBIDDEN'); };
});
test.after(() => { net.Socket.prototype.connect = originalConnect; globalThis.fetch = originalFetch; });
const extract = output => extractRecruitmentPost(sourceInput, { provider: new MockExtractionProvider(output) });

test('correct literal facts cannot be promoted to independently VERIFIED by the model', async () => {
  const output = mockOutput(); output.employer.status = 'VERIFIED';
  const result = await extract(output);
  assert.equal(result.employer.status, 'POST_MENTIONED');
  assert.equal(result.verificationStatus, 'NOT_CHECKED');
  assert.ok(result.diagnostics.some(d => d.code === 'MODEL_VERIFICATION_DOWNGRADED'));
});

test('cross-vacancy stack and role and common footer cannot silently become one role requirements', async () => {
  const output = mockOutput();
  output.vacancies[1].technologies = fact(['Angular'], 'Frontend: ReactJS, Angular, VueJS, NextJS');
  output.vacancies[1].roleCategory = fact('FRONTEND', 'Frontend:');
  output.vacancies[2].requirements = structuredClone(output.sharedRequirements);
  const result = await extract(output);
  assert.equal(result.vacancies[1].technologies.value, null);
  assert.equal(result.vacancies[1].roleCategory.value, null);
  assert.equal(result.vacancies[2].requirements.value, null);
  assert.equal(result.diagnostics.filter(d => d.code === 'VACANCY_ATTRIBUTION_UNSUPPORTED').length, 3);
});

test('alternative technology list is not sufficient evidence that every item is mandatory', async () => {
  const output = mockOutput(); output.vacancies[1].mandatorySkills = structuredClone(output.vacancies[1].technologies);
  const result = await extract(output);
  assert.equal(result.vacancies[1].mandatorySkills.value, null);
  assert.deepEqual(result.vacancies[1].technologies.value, ['Java', 'Golang', '.NET', 'NodeJS', 'Python', 'PHP']);
  assert.ok(result.diagnostics.some(d => d.code === 'SKILL_OBLIGATION_UNSUPPORTED'));
});

test('role-specific text cannot be claimed as shared requirements', async () => {
  const output = mockOutput(); output.sharedRequirements = fact('Java', 'Backend: Java, Golang, .NET, NodeJS, Python, PHP');
  const result = await extract(output);
  assert.equal(result.sharedRequirements.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'POST_ATTRIBUTION_UNSUPPORTED'));
});

test('application domain is not independent evidence for employer identity', async () => {
  const output = mockOutput(); output.employer = fact('microsoft', applicationUrl);
  const result = await extract(output);
  assert.equal(result.employer.value, null);
  assert.equal(result.applicationUrl.value, applicationUrl);
  assert.ok(result.diagnostics.some(d => d.path === 'employer'));
});

test('invalid evidence offsets, unsupported seniority and CONFLICTING status abstain with diagnostics', async () => {
  const output = mockOutput(); output.employer.evidence[0].end++;
  output.sharedLocation.status = 'CONFLICTING'; output.vacancies[0].seniority = fact('SENIOR', 'Intern');
  const result = await extract(output);
  assert.equal(result.employer.value, null); assert.equal(result.sharedLocation.value, null);
  assert.equal(result.vacancies[0].seniority.value, null);
  assert.equal(result.diagnostics.length, 3);
});

test('schema rejects unknown keys, invalid enums, reversed ranges, arrays and markdown envelopes', async () => {
  for (const mutate of [o => o.extra = 'unexpected', o => o.employer.extra = 'unexpected', o => o.vacancies[0].roleCategory.value = 'NO_SUCH_ROLE',
    o => o.vacancies[0].experienceYears.value = { minimum: 3, maximum: 1 }, o => o.schemaVersion = 'unsupported']) {
    const output = mockOutput(); mutate(output); await assert.rejects(() => extract(output), /SCHEMA_INVALID/u);
  }
  for (const output of ['[]', 'null', '```json\n{}\n```']) await assert.rejects(() => extract(output), /SCHEMA|JSON/u);
});

test('word fragments cannot invent Java from JavaScript or shorten an application URL', async () => {
  const rawContent = sourceInput.rawContent.replace('Backend: Java,', 'Backend: JavaScript,');
  const input = { ...sourceInput, rawContent, contentHash: createHash('sha256').update(rawContent).digest('hex') };
  const output = mockOutput();
  // Rebuild exact source spans so this exercises value validation, not bad offsets.
  const visit = x => {
    if (!x || typeof x !== 'object') return;
    if (Array.isArray(x.evidence)) for (const e of x.evidence) {
      e.quote = e.quote.replace('Backend: Java,', 'Backend: JavaScript,');
      e.start = rawContent.indexOf(e.quote); e.end = e.start + e.quote.length;
    }
    for (const value of Object.values(x)) visit(value);
  };
  visit(output); output.applicationUrl.value = applicationUrl.slice(0, -2);
  const result = await extractRecruitmentPost(input, { provider: new MockExtractionProvider(output) });
  assert.equal(result.vacancies[1].technologies.value, null); assert.equal(result.applicationUrl.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'VALUE_NOT_SUPPORTED_BY_EVIDENCE'));
});

test('bad input identity, hash, URL, publication metadata and limits fail before provider invocation', async () => {
  for (const change of [{ contentHash: '0'.repeat(64) }, { sourcePostId: '../unsafe' }, { sourceUrl: 'https://user:pass@example.invalid/' }, { sourceUrl: ['https://example.invalid'] },
    { publishedAt: 'not-a-date' }, { rawContent: '\0' }]) {
    const provider = mockProvider();
    await assert.rejects(() => extractRecruitmentPost({ ...sourceInput, ...change }, { provider }), /INPUT/u);
    assert.equal(provider.calls.length, 0);
  }
  for (const change of [{ timeoutMs: 20001 }, { maxOutputBytes: 65537 }, { maxInputChars: 24001 }]) {
    const provider = mockProvider(); await assert.rejects(() => extractRecruitmentPost(sourceInput, { provider, ...change }), /LIMIT/u);
    assert.equal(provider.calls.length, 0);
  }
});

test('explicit numeric bounds must agree with the evidence, not just contain the same numbers', async () => {
  const line = 'Backend: Java, Golang, .NET, NodeJS, Python, PHP';
  const rawContent = sourceInput.rawContent.replace(line, line + '\nRequirements: 2–5 years experience; Salary: 100–200 USD');
  const input = { ...sourceInput, rawContent, contentHash: createHash('sha256').update(rawContent).digest('hex') };
  const output = mockOutput();
  const rebase = x => {
    if (!x || typeof x !== 'object') return;
    if (Array.isArray(x.evidence)) for (const e of x.evidence) { e.start = rawContent.indexOf(e.quote); e.end = e.start + e.quote.length; }
    for (const value of Object.values(x)) rebase(value);
  };
  rebase(output);
  const quoted = (value, quote) => ({ value, status: 'POST_MENTIONED', evidence: [{ start: rawContent.indexOf(quote), end: rawContent.indexOf(quote) + quote.length, quote }] });
  output.vacancies[1].experienceYears = quoted({ minimum: 2, maximum: 5 }, '2–5 years experience');
  output.vacancies[1].salary = quoted({ minimum: 100, maximum: 200, currency: 'USD' }, 'Salary: 100–200 USD');
  const valid = await extractRecruitmentPost(input, { provider: new MockExtractionProvider(output) });
  assert.deepEqual(valid.vacancies[1].experienceYears.value, { minimum: 2, maximum: 5 });
  assert.deepEqual(valid.vacancies[1].salary.value, { minimum: 100, maximum: 200, currency: 'USD' });
  output.vacancies[1].experienceYears.value = { minimum: 5, maximum: 5 }; output.vacancies[1].salary.value = 200;
  const invalid = await extractRecruitmentPost(input, { provider: new MockExtractionProvider(output) });
  assert.equal(invalid.vacancies[1].experienceYears.value, null); assert.equal(invalid.vacancies[1].salary.value, null);
});

test('provider error is sanitized and no default real provider, fallback, or retry exists', async () => {
  const provider = mockProvider(); provider.extract = async request => { provider.calls.push(request); throw Error('PRIVATE_PROVIDER_TOKEN_RAW_RESPONSE'); };
  await assert.rejects(() => extractRecruitmentPost(sourceInput, { provider }), e => e.message === 'AI_EXTRACTION_PROVIDER_FAILED');
  assert.equal(provider.calls.length, 1);
  await assert.rejects(() => extractRecruitmentPost(sourceInput), /PROVIDER_REQUIRED/u);
});

test('timeout aborts provider signal once and a late provider resolution cannot change result', async () => {
  let calls = 0, aborted = false;
  const provider = { modelIdentity: 'offline-hanging', extract: (_request, { signal }) => {
    calls++; return new Promise(resolve => signal.addEventListener('abort', () => { aborted = true; resolve(JSON.stringify(mockOutput())); }, { once: true }));
  } };
  await assert.rejects(() => extractRecruitmentPost(sourceInput, { provider, timeoutMs: 10 }), /TIMEOUT/u);
  assert.equal(aborted, true); assert.equal(calls, 1);
});

test('schema guidance is fresh per call; output limits measure UTF-8 bytes, not JS string length', async () => {
  const a = extractionJsonSchema(); a.properties.employer.properties.evidence.items.properties.start.type = 'string';
  assert.equal(extractionJsonSchema().properties.employer.properties.evidence.items.properties.start.type, 'integer');
  const provider = { modelIdentity: 'offline-large', extract: async () => 'ệ'.repeat(22000) };
  await assert.rejects(() => extractRecruitmentPost(sourceInput, { provider }), /OUTPUT_TOO_LARGE/u);
});

test('content changes affect cache identity but input publication metadata is never verified automatically', async () => {
  const provider = new MockExtractionProvider(mockOutput());
  const a = await extractRecruitmentPost(sourceInput, { provider });
  const rawContent = sourceInput.rawContent + '\n';
  const b = await extractRecruitmentPost({ ...sourceInput, rawContent, contentHash: createHash('sha256').update(rawContent).digest('hex'), publishedAt: '2026-10-27' }, { provider });
  assert.notEqual(a.cacheKey, b.cacheKey); assert.equal(b.publishedAt.value, null); assert.equal(b.publicationEvidenceStatus, 'UNKNOWN');
  assert.equal(provider.callCount, 2); // Cache identity only, no implicit shared cache.
});

test('comparison reports baseline and mock contract correctness separately; no real-model quality claim', async () => {
  const expected = { employer: 'Rikkeisoft', titles: ['Frontend', 'Backend', 'Mobile'], vacancyCount: 3,
    roles: ['FRONTEND', 'BACKEND', 'MOBILE'], seniorities: [['INTERN'], ['INTERN'], ['INTERN']], location: address,
    technologies: mockOutput().vacancies.map(v => v.technologies.value), requirements: mockOutput().sharedRequirements.value,
    applicationUrl, salary: null, experienceYears: [null, null, null], publishedAt: null };
  const report = await compareExtraction({ input: sourceInput, expected, provider: new MockExtractionProvider(mockOutput()) });
  assert.equal(report.realLlmQuality, 'NOT_EVALUATED');
  assert.ok(report.mockPipeline.every(f => f.outcome === 'EXACT_MATCH'));
  assert.equal(report.baseline.find(f => f.field === 'vacancyCount').actual, 1);
  assert.equal(report.baseline.find(f => f.field === 'vacancyCount').outcome, 'MISMATCH');
  assert.equal(report.validationDiagnostics.length, 0);
});
