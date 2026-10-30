import test from 'node:test';
import assert from 'node:assert/strict';
import { inspectOutput } from '../validate-output.mjs';
import { validateExtractionShape, POST_FIELDS, VACANCY_FIELDS } from '../../browser-job-discovery/src/ai-extraction-schema.mjs';
import { validateExtractionEvidence } from '../../browser-job-discovery/src/ai-extraction-evidence.mjs';

// Synthetic only. This is NOT a reconstruction of the unavailable live output.
const policy = { realignUniqueQuotes: true, inlineRecruitmentTitles: true };
const unknown = () => ({ value: null, status: 'UNKNOWN', evidence: [] });
const post = () => ({ schemaVersion: 'ai-job-extraction-v1', ...Object.fromEntries(POST_FIELDS.map(k => [k, unknown()])), vacancies: [] });
const role = () => Object.fromEntries(VACANCY_FIELDS.map(k => [k, unknown()]));
function fact(raw, value, quote, from = 0) {
  const start = raw.indexOf(quote, from); assert.ok(start >= 0);
  return { value, status: 'POST_MENTIONED', evidence: [{ start, end: start + quote.length, quote }] };
}
function validate(raw, output, options = policy) {
  validateExtractionShape(output);
  const before = structuredClone(output);
  const result = validateExtractionEvidence(output, raw, options);
  assert.deepEqual(output, before, 'model output must not be mutated');
  return result;
}
const inline = 'ACME SOFTWARE tuyển dụng Intern Backend Developer\n(.NET) tại Hà Nội.\nCông nghệ: C#, ASP.NET Core.\nĐịa điểm: Hà Nội.';
function inlineOutput(raw = inline) {
  const output = post(), vacancy = role();
  output.employer = fact(raw, 'ACME SOFTWARE', 'ACME SOFTWARE');
  vacancy.title = fact(raw, 'Intern Backend Developer', 'Intern Backend Developer');
  vacancy.technologies = fact(raw, ['C#', 'ASP.NET Core'], 'Công nghệ: C#, ASP.NET Core.');
  output.vacancies = [vacancy]; return output;
}

test('V2 repairs a unique exact quote, records correction and keeps source untouched', () => {
  const raw = 'Công ty ACME SOFTWARE tuyển dụng.', output = post();
  output.employer = fact(raw, 'ACME SOFTWARE', 'ACME SOFTWARE'); output.employer.evidence[0].end++;
  const result = validate(raw, output);
  assert.equal(result.employer.value, 'ACME SOFTWARE');
  assert.equal(raw.slice(result.employer.evidence[0].start, result.employer.evidence[0].end), 'ACME SOFTWARE');
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_OFFSETS_REALIGNED_EXACT'));
});
test('legacy strict policy continues rejecting wrong offsets', () => {
  const raw = 'ACME SOFTWARE tuyển dụng.', output = post();
  output.employer = fact(raw, 'ACME SOFTWARE', 'ACME SOFTWARE'); output.employer.evidence[0].end++;
  assert.equal(validate(raw, output, {}).employer.value, null);
});
for (const quote of ['Hà Nội', 'tuyển dụng', 'thực tập sinh', '😀', '𠮷 Hà Nội']) {
  test(`UTF-16 exact evidence remains valid: ${quote}`, () => {
    const raw = `😀 Kiểm tra: ${quote}.`, output = post();
    output.employer = fact(raw, quote, quote, 2);
    assert.equal(validate(raw, output).employer.value, quote);
  });
}
test('UTF-8 byte offsets are recognized only after unique exact grounding', () => {
  const raw = '😀 tuyển dụng tại Hà Nội.', output = post();
  output.sharedLocation = fact(raw, 'Hà Nội', 'Hà Nội');
  const e = output.sharedLocation.evidence[0];
  e.start = Buffer.byteLength(raw.slice(0, e.start)); e.end = e.start + Buffer.byteLength(e.quote);
  const result = validate(raw, output);
  assert.equal(result.sharedLocation.value, 'Hà Nội');
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_UTF8_OFFSETS_REALIGNED_EXACT'));
});
test('ambiguous repeated and overlapping quotes are never realigned', () => {
  for (const [raw, quote] of [['Hà Nội và Hà Nội', 'Hà Nội'], ['aaa', 'aa']]) {
    const output = post(); output.employer = fact(raw, quote, quote);
    output.employer.evidence[0].start = -1;
    const result = validate(raw, output); assert.equal(result.employer.value, null);
    assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_QUOTE_AMBIGUOUS'));
  }
});
test('correct offsets disambiguate a repeated quote without correction', () => {
  const raw = 'Hà Nội; địa điểm: Hà Nội', output = post();
  output.sharedLocation = fact(raw, 'Hà Nội', 'Hà Nội', 5);
  const result = validate(raw, output); assert.equal(result.sharedLocation.value, 'Hà Nội');
  assert.equal(result.diagnostics.length, 0);
});
test('quote not in source is diagnosed, never searched by claimed value', () => {
  const output = post(); output.employer = { value: 'ACME', status: 'POST_MENTIONED', evidence: [{ start: 0, end: 4, quote: 'FAKE' }] };
  const result = validate('ACME tuyển dụng.', output);
  assert.equal(result.employer.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_QUOTE_NOT_FOUND'));
});
test('missing evidence does not get invented from the value', () => {
  const output = post(); output.employer = { value: 'ACME', status: 'POST_MENTIONED', evidence: [] };
  const result = validate('ACME tuyển dụng.', output);
  assert.equal(result.employer.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_MISSING'));
});
test('exact quote repair cannot authorize an invented value', () => {
  const raw = 'ACME SOFTWARE tuyển dụng.', output = post();
  output.employer = fact(raw, 'INVENTED', 'ACME SOFTWARE'); output.employer.evidence[0].start++;
  const result = validate(raw, output); assert.equal(result.employer.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'VALUE_NOT_SUPPORTED_BY_EVIDENCE'));
});
test('Unicode normalization is not used to manufacture an exact quote', () => {
  const raw = 'Ha\u0300 No\u0323i', output = post();
  output.sharedLocation = { value: 'Hà Nội', status: 'POST_MENTIONED', evidence: [{ start: 0, end: raw.length, quote: 'Hà Nội' }] };
  assert.equal(validate(raw, output).sharedLocation.value, null);
});
test('a quote splitting a surrogate pair is rejected', () => {
  const output = post(); output.employer = { value: '\ud83d', status: 'POST_MENTIONED', evidence: [{ start: 0, end: 1, quote: '\ud83d' }] };
  const result = validate('😀', output); assert.equal(result.employer.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_SURROGATE_BOUNDARY_INVALID'));
});
test('explicit inline recruitment title and its own stack survive without reformatting source', () => {
  const result = validate(inline, inlineOutput());
  assert.equal(result.vacancies.length, 1);
  assert.equal(result.vacancies[0].title.value, 'Intern Backend Developer');
  assert.deepEqual(result.vacancies[0].technologies.value, ['C#', 'ASP.NET Core']);
});
test('same-line recruitment, stack suffix and location are narrowly supported', () => {
  const raw = inline.replace('Developer\n(.NET)', 'Developer (.NET)');
  assert.equal(validate(raw, inlineOutput(raw)).vacancies.length, 1);
});
test('application/contact footer verbs are not mistaken for another recruiting subject', () => {
  for (const footer of ['Ứng tuyển theo hướng dẫn của công ty.', 'Liên hệ tuyển dụng theo hướng dẫn.']) {
    const raw = inline + '\n' + footer, output = inlineOutput(raw);
    output.sharedLocation = fact(raw, 'Hà Nội', 'Địa điểm: Hà Nội.');
    const result = validate(raw, output);
    assert.equal(result.sharedLocation.value, 'Hà Nội');
    assert.equal(result.vacancies.length, 1);
  }
});
for (const sentence of [
  'ACME mentions Intern Backend Developer.',
  'ACME không tuyển dụng Intern Backend Developer.',
  'Nếu bạn thấy ACME tuyển dụng Intern Backend Developer.',
  'ACME tuyển dụng Intern Backend Developer để hướng dẫn nhân viên.',
  'ACME đã đóng tuyển Intern Backend Developer.'
]) test(`arbitrary/narrative/non-active inline mention is not a vacancy: ${sentence}`, () => {
  const output = post(), vacancy = role(); vacancy.title = fact(sentence, 'Intern Backend Developer', 'Intern Backend Developer'); output.vacancies = [vacancy];
  assert.equal(validate(sentence, output).vacancies.length, 0);
});
test('an omitted neighbouring inline vacancy remains an independent boundary', () => {
  const raw = inline + '\nBETA tuyển dụng Fresher Frontend Developer.\nCông nghệ: Angular.';
  const output = inlineOutput(raw);
  output.vacancies[0].technologies = fact(raw, ['Angular'], 'Công nghệ: Angular.');
  const result = validate(raw, output); assert.equal(result.vacancies.length, 1);
  assert.equal(result.vacancies[0].technologies.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'VACANCY_ATTRIBUTION_UNSUPPORTED'));
});
test('realigned cross-vacancy quote still fails attribution', () => {
  const raw = inline + '\nBETA tuyển dụng Fresher Frontend Developer.\nCông nghệ: Angular.';
  const output = inlineOutput(raw); output.vacancies[0].technologies = fact(raw, ['Angular'], 'Công nghệ: Angular.');
  output.vacancies[0].technologies.evidence[0].start = 0;
  const result = validate(raw, output); assert.equal(result.vacancies.length, 1);
  assert.equal(result.vacancies[0].technologies.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'VACANCY_ATTRIBUTION_UNSUPPORTED'));
});
test('broad multiline title quotes cannot swallow another role', () => {
  const output = inlineOutput(); output.vacancies[0].title = fact(inline, 'Intern Backend Developer', inline);
  assert.equal(validate(inline, output).vacancies.length, 0);
});
for (const [field, value, line] of [
  ['mandatorySkills', ['C#'], "C# isn't required."],
  ['salary', 0, 'Salary: 100,000 VND']
]) test(`realignment does not bypass semantic validation: ${field}`, () => {
  const raw = inline + '\n' + line, output = inlineOutput(raw);
  output.vacancies[0][field] = fact(raw, value, line);
  output.vacancies[0][field].evidence[0].start = 0;
  const result = validate(raw, output);
  assert.equal(result.vacancies[0][field].value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'EVIDENCE_OFFSETS_REALIGNED_EXACT'));
});
test('realignment cannot turn an application deadline into publication date', () => {
  const line = 'Application deadline: 2026-11-30', raw = inline + '\n' + line, output = inlineOutput(raw);
  output.publishedAt = fact(raw, '2026-11-30', line); output.publishedAt.evidence[0].start = 0;
  const result = validate(raw, output);
  assert.equal(result.publishedAt.value, null);
  assert.ok(result.diagnostics.some(d => d.code === 'VALUE_NOT_SUPPORTED_BY_EVIDENCE'));
});
for (const heading of ['BETA tuyển dụng Python Backend Developer.', 'BETA không tuyển dụng Fresher Frontend Developer.']) {
  test(`unsupported or negated hiring block cannot lend its technology: ${heading}`, () => {
    const raw = inline + '\n' + heading + '\nCông nghệ: Django.';
    const output = inlineOutput(raw); output.vacancies[0].technologies = fact(raw, ['Django'], 'Công nghệ: Django.');
    const result = validate(raw, output);
    assert.equal(result.vacancies.length, 1);
    assert.equal(result.vacancies[0].technologies.value, null);
    assert.ok(result.diagnostics.some(d => d.code === 'VACANCY_ATTRIBUTION_UNSUPPORTED'));
  });
}
test('model missing facts are distinct from evidence rejections', () => {
  const result = inspectOutput(inline, JSON.stringify(post()));
  assert.equal(result.Evidence, 'EVIDENCE_ABSTAINED');
  assert.ok(result.MissingFacts, 'missing facts summary must be implemented');
  assert.ok(result.MissingFacts.total > 0);
  assert.equal(result.Diagnostics.rejectionCount, 0);
});
test('partial extraction exposes sanitized field paths, not raw quotes', () => {
  const output = inlineOutput(); output.sharedLocation = { value: 'PRIVATE_UNSUPPORTED_PLACE', status: 'POST_MENTIONED', evidence: [] };
  const result = inspectOutput(inline, JSON.stringify(output));
  assert.equal(result.Evidence, 'EVIDENCE_PARTIAL');
  assert.equal(result.Fields.employer, 'ACME SOFTWARE');
  assert.ok(result.Diagnostics, 'safe diagnostics summary must be implemented');
  assert.ok(result.Diagnostics.items.some(d => d.path === 'sharedLocation' && d.code === 'EVIDENCE_MISSING'));
  assert.ok(!JSON.stringify(result).includes('PRIVATE_UNSUPPORTED_PLACE'));
  assert.ok(!JSON.stringify(result.Diagnostics).includes('quote'));
});
test('diagnostic bounds explicitly report truncation and count all rejections', () => {
  const output = post();
  output.vacancies = Array.from({ length: 30 }, () => { const v = role(); v.title = { value: 'PRIVATE_TITLE', status: 'POST_MENTIONED', evidence: [] }; return v; });
  const result = inspectOutput(inline, JSON.stringify(output));
  assert.equal(result.Evidence, 'EVIDENCE_FAIL');
  assert.ok(result.Diagnostics, 'bounded diagnostics summary must be implemented');
  assert.equal(result.Diagnostics.items.length, 16); assert.ok(result.Diagnostics.total > 16);
  assert.equal(result.Diagnostics.truncated, true);
  assert.ok(!JSON.stringify(result).includes('PRIVATE_TITLE'));
});
