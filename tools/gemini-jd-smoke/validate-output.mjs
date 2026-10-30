import { pathToFileURL } from 'node:url';
import net from 'node:net';
import { validateExtractionShape, POST_FIELDS, VACANCY_FIELDS } from '../browser-job-discovery/src/ai-extraction-schema.mjs';
import { validateExtractionEvidence, EVIDENCE_POLICY_V2 } from '../browser-job-discovery/src/ai-extraction-evidence.mjs';

const infoCodes = new Set(['EVIDENCE_OFFSETS_REALIGNED_EXACT', 'EVIDENCE_UTF8_OFFSETS_REALIGNED_EXACT']);
const categories = {
  EVIDENCE_OFFSETS_REALIGNED_EXACT: 'OFFSET_CORRECTION', EVIDENCE_UTF8_OFFSETS_REALIGNED_EXACT: 'UTF8_COMPATIBLE_OFFSET_CORRECTION',
  EVIDENCE_MISSING: 'MISSING_QUOTATION', EVIDENCE_QUOTE_NOT_FOUND: 'QUOTE_NOT_IN_SOURCE', EVIDENCE_QUOTE_AMBIGUOUS: 'AMBIGUOUS_QUOTE',
  EVIDENCE_SURROGATE_BOUNDARY_INVALID: 'INVALID_UTF16_BOUNDARY', EVIDENCE_SPAN_INVALID: 'INVALID_OFFSETS_OR_QUOTATION',
  VALUE_NOT_SUPPORTED_BY_EVIDENCE: 'UNSUPPORTED_VALUE', VACANCY_WITHOUT_SUPPORTED_TITLE: 'UNSUPPORTED_TITLE',
  VACANCY_ATTRIBUTION_UNSUPPORTED: 'VACANCY_ATTRIBUTION', POST_ATTRIBUTION_UNSUPPORTED: 'POST_ATTRIBUTION',
  SKILL_OBLIGATION_UNSUPPORTED: 'UNSUPPORTED_REQUIREMENT', MODEL_VERIFICATION_DOWNGRADED: 'UNVERIFIED_CLAIM',
  CONFLICTING_EVIDENCE: 'CONFLICT', CONFLICTING_OR_UNKNOWN_FACT: 'CONFLICT', UNKNOWN_FACT_NORMALIZED: 'UNKNOWN_FACT', DUPLICATE_VACANCY: 'DUPLICATE'
};
function safePath(path) {
  const [field, rest] = path.split('.evidence[');
  const vacancy = field.match(/^vacancies\[([0-9]{1,2})\](?:\.([A-Za-z]+))?$/u);
  const valid = POST_FIELDS.includes(field) || vacancy && Number(vacancy[1]) < 30 && (!vacancy[2] || VACANCY_FIELDS.includes(vacancy[2]));
  return valid && (rest === undefined || /^[0-7]\]$/u.test(rest)) ? path : 'UNKNOWN_FIELD';
}
function diagnosticsSummary(diagnostics) {
  const informationCount = diagnostics.filter(d => infoCodes.has(d.code)).length;
  return { total: diagnostics.length, informationCount, rejectionCount: diagnostics.length - informationCount,
    truncated: diagnostics.length > 16, items: diagnostics.slice(0, 16).map(d => ({ path: safePath(d.path),
      code: Object.hasOwn(categories, d.code) ? d.code : 'UNKNOWN_REJECTION',
      category: categories[d.code] ?? 'UNKNOWN_REJECTION', severity: infoCodes.has(d.code) ? 'INFO' : 'REJECTION' })) };
}

// No provider adapter, file persistence, registry or backend is loaded.
export function inspectOutput(rawContent, output) {
  const report = { JsonParse: 'NOT_RUN', Schema: 'NOT_RUN', Evidence: 'NOT_RUN', Coverage: 'NOT_EVALUATED', ErrorCategory: null, Fields: null,
    Diagnostics: diagnosticsSummary([]), MissingFacts: { total: 0, truncated: false, paths: [] } };
  if (typeof rawContent !== 'string' || !rawContent || rawContent.length > 24000 || typeof output !== 'string' || Buffer.byteLength(output) > 65536)
    return { ...report, ErrorCategory: 'LOCAL_INPUT_INVALID' };
  let parsed;
  try { parsed = JSON.parse(output); report.JsonParse = 'JSON_PARSE_PASS'; }
  catch { return { ...report, JsonParse: 'JSON_PARSE_FAIL', ErrorCategory: 'OUTPUT_JSON_INVALID' }; }
  try { validateExtractionShape(parsed); report.Schema = 'SCHEMA_PASS'; }
  catch { return { ...report, Schema: 'SCHEMA_FAIL', ErrorCategory: 'OUTPUT_SCHEMA_INVALID' }; }
  const missing = POST_FIELDS.filter(k => parsed[k].value === null);
  if (parsed.vacancies.length === 0) missing.push('vacancies');
  parsed.vacancies.forEach((v, i) => { for (const k of VACANCY_FIELDS) if (v[k].value === null) missing.push(`vacancies[${i}].${k}`); });
  report.MissingFacts = { total: missing.length, truncated: missing.length > 16, paths: missing.slice(0, 16) };
  const normalized = validateExtractionEvidence(parsed, rawContent, EVIDENCE_POLICY_V2);
  report.Diagnostics = diagnosticsSummary(normalized.diagnostics);
  report.Coverage = normalized.vacancies.length ? 'VACANCY_FACTS_PRESENT_NOT_COMPLETENESS_PROOF'
    : Object.values(normalized).some(f => f && typeof f === 'object' && !Array.isArray(f) && f.value !== null)
      ? 'POST_FACTS_ONLY_NO_VACANCY_CONFIRMED' : 'NO_SUPPORTED_FACTS';
  const hasFacts = report.Coverage !== 'NO_SUPPORTED_FACTS';
  report.Evidence = report.Diagnostics.rejectionCount ? hasFacts ? 'EVIDENCE_PARTIAL' : 'EVIDENCE_FAIL' : hasFacts ? 'EVIDENCE_PASS' : 'EVIDENCE_ABSTAINED';
  report.ErrorCategory = report.Diagnostics.rejectionCount ? 'UNSUPPORTED_EVIDENCE_ABSTAINED' : !hasFacts ? 'MODEL_FACTS_MISSING' : null;
  // Display only normalized supported values/unknowns, never provider text/quotes.
  report.Fields = { employer: normalized.employer.value, location: normalized.sharedLocation.value,
    publishedAt: normalized.publishedAt.value, applicationUrl: normalized.applicationUrl.value,
    vacancies: normalized.vacancies.map(v => Object.fromEntries(Object.entries(v).map(([name, fact]) => [name, fact.value]))) };
  return report;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  net.Socket.prototype.connect = () => { throw Error('NETWORK_FORBIDDEN'); };
  globalThis.fetch = () => { throw Error('NETWORK_FORBIDDEN'); };
  try {
    let result;
    if (process.argv[2] === '--preflight') result = { ready: true };
    else if (process.argv[2] === '--validate') {
      let input = '';
      process.stdin.setEncoding('utf8');
      for await (const chunk of process.stdin) { input += chunk; if (Buffer.byteLength(input) > 128 * 1024) throw Error(); }
      const payload = JSON.parse(input);
      result = inspectOutput(payload.rawContent, payload.output);
    } else throw Error();
    const json = JSON.stringify(result);
    if (Buffer.byteLength(json) > 16 * 1024) throw Error();
    process.stdout.write(json);
  } catch {
    process.stdout.write('{"ErrorCategory":"LOCAL_VALIDATION_FAILED"}');
    process.exitCode = 1;
  }
}
