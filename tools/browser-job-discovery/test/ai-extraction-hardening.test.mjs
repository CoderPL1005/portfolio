import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import net from 'node:net';
import { extractRecruitmentPost } from '../src/ai-job-extraction.mjs';
import { sourceInput, mockOutput, rawContent } from './fixtures/ai-extraction-v1.mjs';

// Synthetic regression cases from the readiness probes, not live Facebook data.
const originalFetch = globalThis.fetch, originalConnect = net.Socket.prototype.connect;
globalThis.fetch = () => { throw Error('NETWORK_FORBIDDEN'); };
net.Socket.prototype.connect = () => { throw Error('NETWORK_FORBIDDEN'); };
test.after(() => { globalThis.fetch = originalFetch; net.Socket.prototype.connect = originalConnect; });
const quote = (body, value, text) => {
  const start = body.indexOf(text);
  assert.ok(start >= 0, 'fixture evidence exists');
  return { value, status: 'POST_MENTIONED', evidence: [{ start, end: start + text.length, quote: text }] };
};
const inputFor = body => ({ ...sourceInput, rawContent: body, contentHash: createHash('sha256').update(body, 'utf8').digest('hex') });
const providerFor = output => ({ modelIdentity: 'offline-hardening-v1', async extract() { return JSON.stringify(output); } });
function rebase(output, body) {
  function walk(node) {
    if (!node || typeof node !== 'object') return;
    if (Array.isArray(node.evidence)) for (const span of node.evidence) {
      span.start = body.indexOf(span.quote); assert.ok(span.start >= 0);
      span.end = span.start + span.quote.length;
    }
    for (const value of Object.values(node)) walk(value);
  }
  walk(output); return output;
}

test('H1 provider mutation cannot authorize evidence absent from trusted source', async () => {
  const forged = rawContent + '\nForged Organization';
  const output = mockOutput(); output.employer = quote(forged, 'Forged Organization', 'Forged Organization');
  const result = await extractRecruitmentPost(sourceInput, { provider: {
    modelIdentity: 'offline-mutating-provider', async extract(request) {
      // A frozen boundary or an isolated mutable copy are both acceptable.
      try { request.input.rawContent = forged; } catch (error) { assert.ok(error instanceof TypeError); }
      return JSON.stringify(output);
    }
  } });
  assert.equal(sourceInput.rawContent, rawContent);
  assert.equal(result.contentHash, sourceInput.contentHash);
  assert.equal(result.employer.value, null);
  assert.ok(result.diagnostics.length > 0);
});

test('H2 a broad model title span cannot transfer Frontend Angular to Backend', async () => {
  const output = mockOutput(); output.vacancies = [output.vacancies[1]];
  output.vacancies[0].title = quote(rawContent, 'Backend', rawContent);
  output.vacancies[0].technologies = quote(rawContent, ['Angular'], 'Frontend: ReactJS, Angular, VueJS, NextJS');
  const result = await extractRecruitmentPost(sourceInput, { provider: providerFor(output) });
  assert.equal(result.vacancies.some(v => v.technologies.value?.includes('Angular')), false);
  assert.ok(result.diagnostics.length > 0);
});

for (const [name, line, field, value] of [
  ['H3 negated C# is not mandatory', 'C# is not required.', 'mandatorySkills', ['C#']],
  ['H4 comma salary must not parse a numeric suffix as zero', 'Salary: 100,000–200,000 VND', 'salary', 0]
]) test(name, async () => {
  const anchor = 'Backend: Java, Golang, .NET, NodeJS, Python, PHP';
  const body = rawContent.replace(anchor, anchor + '\n' + line);
  const output = rebase(mockOutput(), body); output.vacancies[1][field] = quote(body, value, line);
  const result = await extractRecruitmentPost(inputFor(body), { provider: providerFor(output) });
  assert.equal(result.vacancies[1][field].value, null);
  assert.ok(result.diagnostics.length > 0);
});

test('H5 application deadline cannot become publication date', async () => {
  const body = rawContent + '\nApplication deadline: 2026-11-30';
  const output = mockOutput(); output.publishedAt = quote(body, '2026-11-30', 'Application deadline: 2026-11-30');
  const result = await extractRecruitmentPost(inputFor(body), { provider: providerFor(output) });
  assert.equal(result.publishedAt.value, null);
  assert.equal(result.publicationEvidenceStatus, 'UNKNOWN');
  assert.ok(result.diagnostics.length > 0);
});
