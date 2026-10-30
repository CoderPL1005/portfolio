import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';
import { inspectOutput } from '../validate-output.mjs';
import { POST_FIELDS, VACANCY_FIELDS, extractionJsonSchema } from '../../browser-job-discovery/src/ai-extraction-schema.mjs';

// Additional positive-control fixture; NOT the CLI input or real provider data.
const source = 'ACME SOFTWARE tuyển dụng.\nIntern Backend Developer\nCông nghệ: C#, ASP.NET Core.\nĐịa điểm: Hà Nội.';
const unknown = () => ({ value: null, status: 'UNKNOWN', evidence: [] });
const fact = (value, quote) => ({ value, status: 'POST_MENTIONED', evidence: [{ start: source.indexOf(quote), end: source.indexOf(quote) + quote.length, quote }] });
function payload() {
  const post = Object.fromEntries(POST_FIELDS.map(k => [k, unknown()]));
  const vacancy = Object.fromEntries(VACANCY_FIELDS.map(k => [k, unknown()]));
  vacancy.title = fact('Intern Backend Developer', 'Intern Backend Developer');
  vacancy.technologies = fact(['C#', 'ASP.NET Core'], 'Công nghệ: C#, ASP.NET Core.');
  return { schemaVersion: 'ai-job-extraction-v1', ...post, employer: fact('ACME SOFTWARE', 'ACME SOFTWARE'), vacancies: [vacancy] };
}
test('actual local validators accept supported source spans and preserve unknown salary/date', () => {
  const result = inspectOutput(source, JSON.stringify(payload()));
  assert.equal(result.Schema, 'SCHEMA_PASS'); assert.equal(result.Evidence, 'EVIDENCE_PASS');
  assert.equal(result.Fields.vacancies.length, 1);
  assert.deepEqual(result.Fields.vacancies[0].technologies, ['C#', 'ASP.NET Core']);
  assert.equal(result.Fields.vacancies[0].salary, null); assert.equal(result.Fields.publishedAt, null);
});
test('fabricated facts abstain instead of escaping as extracted values', () => {
  const value = payload(); value.employer = fact('INVENTED_COMPANY', 'ACME SOFTWARE');
  const result = inspectOutput(source, JSON.stringify(value));
  assert.equal(result.Evidence, 'EVIDENCE_PARTIAL'); assert.equal(result.Fields.employer, null);
  assert.ok(!JSON.stringify(result).includes('INVENTED_COMPANY'));
});
test('JSON parsing and canonical schema checks are separate', () => {
  assert.equal(inspectOutput(source, '{invalid').JsonParse, 'JSON_PARSE_FAIL');
  const result = inspectOutput(source, '{}'); assert.equal(result.JsonParse, 'JSON_PARSE_PASS');
  assert.equal(result.Schema, 'SCHEMA_FAIL'); assert.equal(result.Evidence, 'NOT_RUN');
});
test('limits and provider error strings cannot leak through reports', () => {
  for (const output of ['SECRET_ERROR_BODY', 'x'.repeat(65537), '{"error":"SECRET_ERROR_BODY"}']) {
    const result = inspectOutput(source, output);
    assert.notEqual(result.Evidence, 'EVIDENCE_PASS');
    assert.ok(!JSON.stringify(result).includes('SECRET_ERROR_BODY'));
  }
});
test('input and source are not mutated; no network needed', () => {
  const value = JSON.stringify(payload()), before = source;
  const originalFetch = globalThis.fetch;
  globalThis.fetch = () => { throw Error('NETWORK_FORBIDDEN'); };
  try { assert.equal(inspectOutput(source, value).Evidence, 'EVIDENCE_PASS'); }
  finally { globalThis.fetch = originalFetch; }
  assert.equal(source, before); assert.deepEqual(JSON.parse(value), payload());
});
test('embedded production contract matches canonical local validator schema', () => {
  const contract = JSON.parse(readFileSync(new URL('../../../src/Portfolio.Infrastructure/AI/discovery-extraction-contract.json', import.meta.url)));
  assert.deepEqual(contract.schema, extractionJsonSchema());
});
test('bridge CLI handles UTF-8 input and emits only bounded normalized metadata', () => {
  const script = fileURLToPath(new URL('../validate-output.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [script, '--validate'], { input: JSON.stringify({ rawContent: source, output: JSON.stringify(payload()) }), encoding: 'utf8', timeout: 5000 });
  assert.equal(result.status, 0); assert.equal(result.stderr, '');
  assert.ok(Buffer.byteLength(result.stdout) <= 16384);
  const report = JSON.parse(result.stdout); assert.equal(report.Evidence, 'EVIDENCE_PASS');
  assert.ok(!result.stdout.includes('rawContent')); assert.ok(!result.stdout.includes('quote'));
});
test('malformed bridge input cannot print an exception or raw payload', () => {
  const script = fileURLToPath(new URL('../validate-output.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [script, '--validate'], { input: 'PRIVATE_RAW_SECRET', encoding: 'utf8', timeout: 5000 });
  assert.equal(result.status, 1); assert.equal(result.stderr, '');
  assert.deepEqual(JSON.parse(result.stdout), { ErrorCategory: 'LOCAL_VALIDATION_FAILED' });
});
test('harness remains independent of database/worker execution and redirects are disabled', () => {
  const runner = readFileSync(new URL('../SmokeRunner.cs', import.meta.url), 'utf8');
  const program = readFileSync(new URL('../Program.cs', import.meta.url), 'utf8');
  const bridge = readFileSync(new URL('../ValidationBridge.cs', import.meta.url), 'utf8');
  assert.doesNotMatch(runner + program, /DbContext|DiscoveryExecutionService|CreateHostBuilder|WebApplication|SemanticAttempts|ActiveKey/u);
  assert.match(runner, /new GeminiDiscoveryTextExtractor\(http, settings\)/u);
  assert.match(runner, /AllowAutoRedirect = false/u);
  assert.match(bridge, /info.Environment.Clear\(\)/u);
  assert.deepEqual([...program.matchAll(/GetEnvironmentVariable\("([^"]+)"\)/gu)].map(m => m[1]).sort(), ['Gemini__ApiKey', 'Gemini__JobExtractionModel']);
});
