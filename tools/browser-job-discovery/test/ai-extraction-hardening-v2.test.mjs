import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import net from 'node:net';
import { extractRecruitmentPost } from '../src/ai-job-extraction.mjs';
import { sourceInput, rawContent, mockOutput } from './fixtures/ai-extraction-v1.mjs';

// TEST-ONLY RED. Every variation is synthetic, never a captured post/model response.
// A separate Node test worker owns these hooks; restore them after this file.
const oldFetch = globalThis.fetch, oldConnect = net.Socket.prototype.connect;
test.before(() => {
  globalThis.fetch = () => { throw Error('NETWORK_FORBIDDEN'); };
  net.Socket.prototype.connect = () => { throw Error('NETWORK_FORBIDDEN'); };
});
test.after(() => { globalThis.fetch = oldFetch; net.Socket.prototype.connect = oldConnect; });
const sha = s => createHash('sha256').update(s, 'utf8').digest('hex');
const backendLine = 'Backend: Java, Golang, .NET, NodeJS, Python, PHP';
const withLine = line => rawContent.replace(backendLine, backendLine + '\n' + line);
function fact(body, value, quote) {
  const start = body.indexOf(quote); assert.ok(start >= 0, 'synthetic evidence exists');
  return { value, status: 'POST_MENTIONED', evidence: [{ start, end: start + quote.length, quote }] };
}
function outputFor(body) {
  const output = mockOutput(); output.vacancies = [output.vacancies[1]];
  const walk = node => {
    if (!node || typeof node !== 'object') return;
    if (Array.isArray(node.evidence)) for (const e of node.evidence) {
      e.start = body.indexOf(e.quote); assert.ok(e.start >= 0, 'base evidence still exists'); e.end = e.start + e.quote.length;
    }
    for (const value of Object.values(node)) walk(value);
  };
  walk(output); return output;
}
async function extract(body, output) {
  const input = { ...sourceInput, rawContent: body, contentHash: sha(body) };
  const before = structuredClone(input); let calls = 0;
  const result = await extractRecruitmentPost(input, { provider: {
    modelIdentity: 'synthetic-hardening-v2', async extract() { calls++; return JSON.stringify(output); }
  } });
  assert.equal(calls, 1, 'one offline provider stub invocation, no retries');
  assert.deepEqual(input, before, 'trusted caller source/identity remains unchanged');
  assert.equal(result.contentHash, before.contentHash);
  return result;
}
function abstains(result, field) {
  assert.ok(result.vacancies.every(v => v[field].value === null), `${field} must abstain (dropping unsupported vacancy is also safe)`);
  assert.ok(result.diagnostics.length > 0, 'unsupported attribution is diagnosed');
}
function accepts(result, field, value) {
  assert.equal(result.vacancies.length, 1, 'positive control retains the supported vacancy');
  assert.deepEqual(result.vacancies[0][field].value, value);
  assert.equal(result.vacancies[0][field].status, 'POST_MENTIONED');
}

for (const heading of ['Frontend Engineer:', '2. Frontend Engineer:', 'Lập trình viên Frontend:']) {
  test(`G01 omitted/renamed heading ${heading} cannot lend Angular to Backend`, async () => {
    const line = heading + ' ReactJS, Angular', body = withLine(line), output = outputFor(body);
    output.vacancies[0].technologies = fact(body, ['Angular'], line);
    abstains(await extract(body, output), 'technologies');
  });
}
test('G02 classic source heading blocks cross-vacancy technology even if role is omitted', async () => {
  const body = withLine('Frontend: Angular'), output = outputFor(body);
  output.vacancies[0].technologies = fact(body, ['Angular'], 'Frontend: Angular');
  abstains(await extract(body, output), 'technologies');
});
test('G02 positive own-role technology survives an omitted neighbouring vacancy', async () => {
  const body = withLine('Frontend Engineer: Angular'), output = outputFor(body);
  output.vacancies[0].technologies = fact(body, ['.NET'], backendLine);
  accepts(await extract(body, output), 'technologies', ['.NET']);
});

for (const line of ["C# isn't required.", 'C# isn’t required.', "Candidates needn't know C#; required skills are Java.",
  'Không yêu cầu C#.', 'C# không bắt buộc.']) {
  test(`G03 negative skill: ${line}`, async () => {
    const body = withLine(line), output = outputFor(body);
    output.vacancies[0].mandatorySkills = fact(body, ['C#'], line);
    abstains(await extract(body, output), 'mandatorySkills');
  });
}
for (const line of ['Required: C#.', 'Bắt buộc: C#.']) {
  test(`G04 positive mandatory skill: ${line}`, async () => {
    const body = withLine(line), output = outputFor(body);
    output.vacancies[0].mandatorySkills = fact(body, ['C#'], line);
    accepts(await extract(body, output), 'mandatorySkills', ['C#']);
  });
}
test('G04 positive C# obligation is not erased by a separate negated Java clause', async () => {
  const line = 'Required: C#; Java is not required.', body = withLine(line), output = outputFor(body);
  output.vacancies[0].mandatorySkills = fact(body, ['C#'], line);
  accepts(await extract(body, output), 'mandatorySkills', ['C#']);
});
test('G04 positive optional skill remains optional, not promoted to mandatory', async () => {
  const line = 'Optional: C#.', body = withLine(line), output = outputFor(body);
  output.vacancies[0].optionalSkills = fact(body, ['C#'], line);
  const result = await extract(body, output);
  accepts(result, 'optionalSkills', ['C#']); assert.equal(result.vacancies[0].mandatorySkills.value, null);
});

for (const line of ['Salary: 100, 000 VND', 'Salary: 100 000 VND', 'Salary: 100,000 VND', 'Salary: 100,,000 VND']) {
  test(`G05 grouped/malformed amount must not support suffix zero: ${line}`, async () => {
    const body = withLine(line), output = outputFor(body);
    output.vacancies[0].salary = fact(body, 0, line);
    abstains(await extract(body, output), 'salary');
  });
}
for (const [line, value] of [
  ['Salary: 100–200 USD', { minimum: 100, maximum: 200, currency: 'USD' }],
  ['Salary: 100.5 USD', 100.5]
]) test(`G06 positive literal salary: ${line}`, async () => {
  const body = withLine(line), output = outputFor(body);
  output.vacancies[0].salary = fact(body, value, line);
  accepts(await extract(body, output), 'salary', value);
});
for (const [line, value] of [['Salary: 20 triệu VND', 20], ['Salary: 100–200 USD', 200]]) {
  test(`G06 unsupported unit or unsupported range scalar: ${line}`, async () => {
    const body = withLine(line), output = outputFor(body);
    output.vacancies[0].salary = fact(body, value, line);
    abstains(await extract(body, output), 'salary');
  });
}

for (const line of ['Published: 2026-10-27', 'Ngày đăng: 2026-10-27']) {
  test(`G07 positive publication marker: ${line}`, async () => {
    const body = rawContent + '\n' + line, output = outputFor(body);
    output.publishedAt = fact(body, '2026-10-27', line);
    const result = await extract(body, output);
    assert.equal(result.publishedAt.value, '2026-10-27'); assert.equal(result.publicationEvidenceStatus, 'POST_MENTIONED');
  });
}
test('G07 explicit application deadline is not publication', async () => {
  const line = 'Application deadline: 2026-11-30', body = rawContent + '\n' + line, output = outputFor(body);
  output.publishedAt = fact(body, '2026-11-30', line);
  const result = await extract(body, output);
  assert.equal(result.publishedAt.value, null); assert.equal(result.publicationEvidenceStatus, 'UNKNOWN');
  assert.ok(result.diagnostics.length > 0);
});
for (const reverse of [false, true]) {
  test(`G08 publication context cannot be borrowed from another date; reverse=${reverse}`, async () => {
    const body = rawContent + '\nPublished: 2026-10-27\nApplications close on: 2026-11-30', output = outputFor(body);
    output.publishedAt = fact(body, '2026-11-30', 'Applications close on: 2026-11-30');
    output.publishedAt.evidence.push(...fact(body, '2026-10-27', 'Published: 2026-10-27').evidence);
    if (reverse) output.publishedAt.evidence.reverse();
    const result = await extract(body, output);
    assert.equal(result.publishedAt.value, null); assert.equal(result.publicationEvidenceStatus, 'UNKNOWN');
    assert.ok(result.diagnostics.length > 0);
  });
}
test('G08 positive publication survives a separate unselected deadline line', async () => {
  const body = rawContent + '\nPublished: 2026-10-27\nApplications close on: 2026-11-30', output = outputFor(body);
  output.publishedAt = fact(body, '2026-10-27', 'Published: 2026-10-27');
  assert.equal((await extract(body, output)).publishedAt.value, '2026-10-27');
});

test('G09 broad title quote cannot expand vacancy scope to the whole post', async () => {
  const output = outputFor(rawContent);
  output.vacancies[0].title = fact(rawContent, 'Backend', rawContent);
  output.vacancies[0].technologies = fact(rawContent, ['Angular'], 'Frontend: ReactJS, Angular, VueJS, NextJS');
  abstains(await extract(rawContent, output), 'technologies');
});
test('G09 shared requirements cannot be borrowed into the last vacancy', async () => {
  const output = mockOutput(); output.vacancies[2].requirements = structuredClone(output.sharedRequirements);
  const result = await extract(rawContent, output);
  assert.equal(result.vacancies[2].requirements.value, null); assert.ok(result.diagnostics.length > 0);
});
test('G09 positive shared footer remains post-owned without losing three real role blocks', async () => {
  const output = mockOutput(), result = await extract(rawContent, output);
  assert.equal(result.vacancies.length, 3);
  assert.equal(result.sharedRequirements.value, output.sharedRequirements.value);
  assert.ok(result.vacancies.every(v => v.requirements.value === null));
});
test('G10 obligation marker for Java cannot authorize unqualified C# in another evidence span', async () => {
  const body = withLine('We also use C#.\nRequired: Java.'), output = outputFor(body);
  output.vacancies[0].mandatorySkills = fact(body, ['C#'], 'We also use C#.');
  output.vacancies[0].mandatorySkills.evidence.push(...fact(body, ['Java'], 'Required: Java.').evidence);
  abstains(await extract(body, output), 'mandatorySkills');
});
test('G10 fabricated evidence offsets cannot authorize a technology', async () => {
  const output = outputFor(rawContent);
  output.vacancies[0].technologies.evidence[0].end++;
  abstains(await extract(rawContent, output), 'technologies');
});
