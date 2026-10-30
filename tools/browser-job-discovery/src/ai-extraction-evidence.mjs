import { POST_FIELDS, VACANCY_FIELDS } from './ai-extraction-schema.mjs';
import { parseExperience } from './core.mjs';

const unknown = () => ({ value: null, status: 'UNKNOWN', evidence: [] });
const fold = x => x.normalize('NFC').toLowerCase();
const literal = (quote, value) => {
  const escaped = fold(value).replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
  return new RegExp(`(?<![\\p{L}\\p{N}])${escaped}(?![\\p{L}\\p{N}])`, 'u').test(fold(quote));
};
const roles = {
  BACKEND: /back[- ]?end|phía máy chủ/iu, FRONTEND: /front[- ]?end/iu, FULLSTACK: /full[- ]?stack/iu,
  MOBILE: /mobile|di động/iu, QA: /\b(?:qa|pqa|tester)\b|kiểm thử/iu, DATA: /\bdata\b|dữ liệu/iu,
  DEVOPS: /\bdevops\b/iu, SECURITY: /security|bảo mật/iu, OTHER: /other|khác/iu
};
const levels = { INTERN: /\bintern(?:ship)?\b|thực tập/iu, FRESHER: /\bfresher\b/iu, JUNIOR: /\bjunior\b/iu,
  MIDDLE: /\b(?:middle|mid[- ]level)\b/iu, SENIOR: /\bsenior\b/iu, LEAD: /\blead\b/iu };
const safeUrl = s => {
  try { const u = new URL(s); return ['https:', 'http:'].includes(u.protocol) && !u.username && !u.password && s.length <= 2048 && !/[\s\u0000-\u001f]/u.test(s); }
  catch { return false; }
};
// Offsets stay in the original UTF-16 source. Clauses are never manufactured by
// joining model-selected quotes; a marker must support the value in its clause.
function clauses(text) {
  return [...text.matchAll(/[^;\r\n!?]+/gu)].flatMap(match => {
    const parts = []; let start = 0;
    for (const boundary of match[0].matchAll(/\.\s+(?=\S)/gu)) {
      parts.push({ start: match.index + start, end: match.index + boundary.index + 1,
        text: match[0].slice(start, boundary.index + 1) });
      start = boundary.index + boundary[0].length;
    }
    parts.push({ start: match.index + start, end: match.index + match[0].length, text: match[0].slice(start) });
    return parts;
  });
}
const negated = text => /\b(?:not|never|no|without|\w+n['’]t)\b|không|chưa|chẳng/iu.test(text);
const obligation = {
  mandatorySkills: /^(?:requirements?|required|mandatory|must(?:\s+(?:have|know))?|bắt buộc|yêu cầu)(?:\s+skills?)?\s*:?\s+/iu,
  optionalSkills: /^(?:optional|preferred|nice.to.have|ưu tiên|lợi thế)\s*:?\s+/iu
};
function skillObligationSupported(name, values, evidence, rawContent) {
  const sourceClauses = clauses(rawContent);
  return values.every(skill => {
    const escaped = skill.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
    const pattern = new RegExp(`(?<![\\p{L}\\p{N}])${escaped}(?![\\p{L}\\p{N}])`, 'giu');
    const claims = evidence.flatMap(e => [...e.quote.matchAll(pattern)].map(m => ({ start: e.start + m.index, end: e.start + m.index + m[0].length })));
    const relevant = sourceClauses.filter(c => claims.some(m => m.start >= c.start && m.end <= c.end));
    // Conflicting/negated evidence for the same skill cannot be outvoted by a
    // positive quote. A negative clause about another skill is independent.
    if (!relevant.length || relevant.some(c => negated(c.text) || rawContent[c.end] === '?')) return false;
    return relevant.some(c => {
      const text = c.text.trim();
      if (name === 'mandatorySkills' && /optional|preferred|nice.to.have|ưu tiên|lợi thế/iu.test(text)) return false;
      const prefix = text.match(obligation[name]);
      if (prefix) {
        const list = text.slice(prefix[0].length).replace(/\.$/u, '').trim();
        // Only a direct list supports this prefix grammar. Alternatives and
        // narrative/modal clauses are ambiguous, not mandatory list entries.
        if (/\b(?:or|but|however|although|we|they|use|uses|used|also|may|might|can|could|should|would)\b|hoặc|nhưng/iu.test(list)) return false;
        if (!/^(?:[\p{L}\p{N}#+.\/-]+(?:[ \t]+[\p{L}\p{N}#+.\/-]+){0,2})(?:[ \t]*(?:,|&|\band\b|và)[ \t]*[\p{L}\p{N}#+.\/-]+(?:[ \t]+[\p{L}\p{N}#+.\/-]+){0,2})*$/iu.test(list)) return false;
        return list.split(/\s*(?:,|&|\band\b|\bvà\b)\s*/iu).some(entry => fold(entry.trim()) === fold(skill));
      }
      return [...c.text.matchAll(pattern)].some(m => {
        const after = c.text.slice(m.index + m[0].length);
        return (name === 'mandatorySkills'
          ? /^\s+(?:(?:is|are)\s+)?(?:required|mandatory|bắt buộc)\b/iu
          : /^\s+(?:(?:is|are)\s+)?(?:optional|preferred|nice.to.have|là lợi thế|được ưu tiên)\b/iu).test(after);
      });
    });
  });
}
function publicationSupported(value, evidence, rawContent) {
  if (!/^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z)?$/u.test(value) ||
    !Number.isFinite(Date.parse(value)) || new Date(value).toISOString().slice(0, 10) !== value.slice(0, 10)) return false;
  const marker = /(?:^|[^\p{L}\p{N}])(?:published|posted)(?:\s+(?:at|on))?\s*:?\s*|(?:^|[^\p{L}\p{N}])(?:publication date|ngày đăng|đăng ngày)\s*:?\s*/giu;
  return clauses(rawContent).some(c => {
    if (negated(c.text) || /deadline|clos(?:e|es|ed|ing)|due|expir|hạn/iu.test(c.text)) return false;
    return [...c.text.matchAll(marker)].some(m => {
      const start = c.start + m.index + m[0].length;
      return rawContent.slice(start, start + value.length) === value &&
        !/[\dT:.Z+-]/u.test(rawContent[start + value.length] ?? '') &&
        evidence.some(e => e.start <= start && e.end >= start + value.length);
    });
  });
}
function salaryTokens(text) {
  // Entire expressions only: no suffix of grouped/malformed amounts, no locale
  // inference, no multipliers, NET/GROSS conversion or unsupported units.
  return clauses(text).flatMap(c => {
    const match = c.text.trim().match(/^(?:(?:salary|lương)\s*:\s*)?(\d+(?:\.\d+)?)(?:[ \t]*[-–][ \t]*(\d+(?:\.\d+)?))?[ \t]+(VND|USD|EUR)\.?$/iu);
    return match ? [{ low: Number(match[1]), high: match[2] === undefined ? null : Number(match[2]), currency: match[3].toUpperCase() }] : [];
  });
}
function sourceHeading(line) {
  const text = line.trim().replace(/^(?:[-*#]+\s*|\d+[.)]\s*)/u, '');
  const colon = text.indexOf(':'), title = (colon < 0 ? text : text.slice(0, colon)).trim();
  if (!title || title.length > 120 || /[;!?]/u.test(title) ||
    /^(?:requirements?|required|must|optional|preferred|yêu cầu|bắt buộc|ưu tiên)/iu.test(title)) return false;
  const role = Object.values(roles).some(pattern => pattern.test(title));
  const profession = /\b(?:developer|engineer|designer|tester|analyst)\b|lập trình viên|kỹ sư/iu.test(title);
  // Bare headings are restricted to short title-like forms, not sentences about
  // a developer mentoring staff. Colon-labelled roles also cut omitted blocks.
  return colon >= 0 ? role || profession :
    /^(?:(?:intern|fresher|junior|middle|senior|lead)\s+)?(?:front[- ]?end|back[- ]?end|full[- ]?stack|mobile|qa|pqa|devops|data|security)(?:\s+(?:developer|engineer))?$/iu.test(title) ||
    /^(?:[\p{L}\p{N}#+.\/-]+\s+){0,4}(?:developer|engineer|designer|tester|analyst)$/iu.test(title);
}
function supportedValue(name, value, quotes, sourceContext = quotes.join('\n')) {
  if (name === 'applicationUrl') return safeUrl(value) && quotes.some(q => (q.match(/https?:\/\/[^\s<>"']+/gu) ?? []).includes(value));
  // Contact handles/URL path tokens are not evidence of employer, stack, role,
  // seniority or publication dates. Do not infer identity from an email domain.
  quotes = quotes.map(q => q.replace(/https?:\/\/\S+/gu, '').replace(/[\p{L}\p{N}._+%-]+@[\p{L}\p{N}.-]+\.[a-z]{2,}/giu, ''));
  const quote = quotes.join('\n');
  if (name === 'roleCategory' || name === 'seniority') return (name === 'roleCategory' ? roles : levels)[value]?.test(quote) ?? false;
  if (name === 'recruitmentStatus') return value === 'CLOSED' ? /đóng tuyển|ngừng tuyển|closed|no longer accepting/iu.test(quote)
    : value === 'ANNOUNCED_UNVERIFIED' && /tuyển|hiring|recruit|vacanc/iu.test(quote);
  if (name === 'experienceYears') {
    const stated = parseExperience(quote);
    if (stated.conflict || !stated.evidence.length) return false;
    return typeof value === 'number' ? value === stated.minimum && stated.maximum === null
      : value.minimum === stated.minimum && value.maximum === stated.maximum;
  }
  if (name === 'salary') {
    // Intentionally narrow literal-unit grammar. No inferred exchange rates,
    // NET/GROSS, million multipliers or fabricated bounds. Unsupported -> unknown.
    // Inspect source lines too: a clipped "000 VND" quote must not validate
    // a suffix of an unsupported comma-formatted amount.
    const tokens = salaryTokens(sourceContext), amounts = quotes.flatMap(salaryTokens);
    if (tokens.length !== 1 || amounts.length !== 1 || JSON.stringify(tokens[0]) !== JSON.stringify(amounts[0])) return false;
    const { low, high, currency } = amounts[0];
    return typeof value === 'number' ? high === null && value === low
      : value.minimum === low && value.maximum === (high ?? low) && (!value.currency || value.currency.toUpperCase() === currency);
  }
  if (Array.isArray(value)) return value.length > 0 && value.every(s => quotes.some(q => literal(q, s)));
  return quotes.some(q => literal(q, value));
}

// Explicit opt-in for the smoke validator. Existing worker callers remain strict.
export const EVIDENCE_POLICY_V2 = Object.freeze({ realignUniqueQuotes: true, inlineRecruitmentTitles: true });
const utf16Boundary = (raw, index) => index >= 0 && index <= raw.length && !(index > 0 && index < raw.length &&
  raw.charCodeAt(index - 1) >= 0xd800 && raw.charCodeAt(index - 1) <= 0xdbff && raw.charCodeAt(index) >= 0xdc00 && raw.charCodeAt(index) <= 0xdfff);
const exactSpan = (raw, e) => Number.isInteger(e.start) && Number.isInteger(e.end) && e.start >= 0 && e.end > e.start && e.end <= raw.length &&
  utf16Boundary(raw, e.start) && utf16Boundary(raw, e.end) && raw.slice(e.start, e.end) === e.quote;

function realignEvidence(output, raw, report, failed) {
  const copy = structuredClone(output);
  function ground(fact, path) {
    if (fact.value === null) return;
    if (!fact.evidence.length) { report(path, 'EVIDENCE_MISSING'); failed.add(path); return; }
    fact.evidence = fact.evidence.map((e, index) => {
      if (exactSpan(raw, e)) return e;
      const start = raw.indexOf(e.quote), diagnosticPath = `${path}.evidence[${index}]`;
      let error;
      if (start < 0) error = 'EVIDENCE_QUOTE_NOT_FOUND';
      else if (raw.indexOf(e.quote, start + 1) >= 0) error = 'EVIDENCE_QUOTE_AMBIGUOUS';
      else if (!utf16Boundary(raw, start) || !utf16Boundary(raw, start + e.quote.length)) error = 'EVIDENCE_SURROGATE_BOUNDARY_INVALID';
      if (error) { report(diagnosticPath, error); failed.add(path); return e; }
      // No fuzzy search, normalization, quote/value synthesis or byte slicing.
      // The exact quote determines canonical UTF-16 offsets; all semantic and
      // vacancy-attribution checks still run after this correction.
      const encoder = new TextEncoder(), byteStart = encoder.encode(raw.slice(0, start)).length;
      const byteEnd = byteStart + encoder.encode(e.quote).length;
      report(diagnosticPath, e.start === byteStart && e.end === byteEnd
        ? 'EVIDENCE_UTF8_OFFSETS_REALIGNED_EXACT' : 'EVIDENCE_OFFSETS_REALIGNED_EXACT');
      return { start, end: start + e.quote.length, quote: e.quote };
    });
  }
  for (const key of POST_FIELDS) ground(copy[key], key);
  copy.vacancies.forEach((v, i) => { for (const key of VACANCY_FIELDS) ground(v[key], `vacancies[${i}].${key}`); });
  return copy;
}

function inlineRecruitment(line) {
  // A narrow anchored hiring statement, not an arbitrary role mention. Company
  // text is context only: this does not establish employer identity/verification.
  const prefix = line.match(/^[\p{Lu}\p{N}][\p{L}\p{N} .&'-]{0,79}?[ \t]+(?:đang[ \t]+)?(?:tuyển dụng|tuyển|is hiring|hiring)[ \t]+/u);
  if (!prefix) return null;
  // Application/contact instructions have no recruiting subject. They are
  // neither a vacancy nor an independent role boundary in a shared footer.
  if (/^(?:ứng|liên hệ|cách|hồ sơ|yêu cầu|quyền lợi|apply|contact|application)(?:[ \t]|$)/iu.test(line) ||
    /(?:^|\s)(?:nếu|bạn|ví|dụ|example|candidate)(?:\s|$)/iu.test(prefix[0])) return null;
  const safePrefix = !negated(prefix[0]) && !/(?:^|\s)(?:previously|former|từng|đã)(?:\s|$)/iu.test(prefix[0]);
  const rest = line.slice(prefix[0].length);
  const role = rest.match(/^(?:(?:Intern|Fresher|Junior|Middle|Senior|Lead)[ \t]+)?(?:Backend|Frontend|Full[- ]?stack|Mobile|QA|PQA|DevOps|Data|Security)[ \t]+(?:Developer|Engineer|Tester|Analyst)(?:[ \t]+(?:Intern|Fresher))?(?=$|[\s(.!])/iu);
  if (!role) return { title: null, offset: prefix[0].length, eligible: false };
  const suffix = rest.slice(role[0].length);
  const eligible = safePrefix && !negated(line) && !/đóng|ngừng|closed|paused/iu.test(line) &&
    /^[ \t]*(?:\([A-Za-z0-9#.+ /-]{1,40}\))?(?:[ \t]+(?:tại|in)[ \t]+[\p{L}\p{N}][\p{L}\p{N} ,'-]{0,79})?[.!]?[ \t]*$/iu.test(suffix);
  // Even an ambiguous suffix cuts the preceding role's scope. It cannot make
  // the ambiguous role itself eligible or lend its technology to another role.
  return { title: role[0], offset: prefix[0].length, eligible };
}

export function validateExtractionEvidence(output, rawContent, policy = {}) {
  const diagnostics = [];
  const report = (path, code) => diagnostics.push({ path, code });
  const failed = new Set();
  if (policy.realignUniqueQuotes === true) output = realignEvidence(output, rawContent, report, failed);
  // Only supported title anchors may establish another vacancy's boundaries.
  const titleAnchor = fact => {
    if (typeof fact.value !== 'string' || !fact.evidence.length) return undefined;
    const e = fact.evidence[0];
    if (fact.evidence.some(s => /[\r\n]/u.test(s.quote) || !exactSpan(rawContent, s))) return undefined;
    const lineStart = rawContent.lastIndexOf('\n', e.start - 1) + 1;
    const lineEnd = rawContent.indexOf('\n', lineStart);
    const end = lineEnd < 0 ? rawContent.length : lineEnd, line = rawContent.slice(lineStart, end);
    if (policy.inlineRecruitmentTitles === true) {
      const inline = inlineRecruitment(line);
      if (inline) {
        const start = lineStart + inline.offset;
        return inline.eligible && fold(fact.value) === fold(inline.title) &&
          fact.evidence.every(s => s.start >= lineStart && s.end <= end) && e.start <= start && e.end >= start + inline.title.length ? start : undefined;
      }
    }
    if (!sourceHeading(line)) return undefined;
    const offset = fold(e.quote).indexOf(fold(fact.value));
    if (offset < 0 || !/^(?:\s*(?:\d+[.)]|[-*#])?\s*)$/u.test(rawContent.slice(lineStart, e.start + offset))) return undefined;
    return lineStart;
  };
  const modelBounds = output.vacancies.filter(v => v.title.value !== null && v.title.evidence.length &&
    v.title.evidence.every(e => exactSpan(rawContent, e)) &&
    supportedValue('title', v.title.value, v.title.evidence.map(e => e.quote)))
    .map(v => titleAnchor(v.title)).filter(n => n !== undefined);
  // Source headings remain boundaries even when the model omits a vacancy.
  const sourceBounds = [...rawContent.matchAll(/[^\r\n]+/gu)].flatMap(m => {
    const inline = policy.inlineRecruitmentTitles === true ? inlineRecruitment(m[0]) : null;
    return inline ? [m.index + inline.offset] : sourceHeading(m[0]) ? [m.index] : [];
  });
  const bounds = [...new Set([...modelBounds, ...sourceBounds])].sort((a, b) => a - b);
  // Recognize only explicitly common sections. An ambiguous last-role footer
  // is not automatically attributed to that role or silently shared with all.
  const sharedStart = [...rawContent.matchAll(/^(?:yêu cầu chung|shared requirements|quyền lợi chung|shared benefits|địa chỉ chung|ứng tuyển chung)[^\n]*/gimu)]
    .map(m => m.index).find(n => n > (bounds.at(-1) ?? -1)) ?? rawContent.length;
  function validate(fact, name, path, scope) {
    if (fact.value === null) {
      if (fact.status !== 'UNKNOWN' || fact.evidence.length) report(path, fact.status === 'CONFLICTING' ? 'CONFLICTING_EVIDENCE' : 'UNKNOWN_FACT_NORMALIZED');
      return unknown();
    }
    if (fact.status === 'CONFLICTING' || fact.status === 'UNKNOWN') { report(path, 'CONFLICTING_OR_UNKNOWN_FACT'); return unknown(); }
    if (failed.has(path)) return unknown();
    if (!fact.evidence.length || fact.evidence.some(e => !exactSpan(rawContent, e))) {
      report(path, 'EVIDENCE_SPAN_INVALID'); return unknown();
    }
    const sourceContext = [...new Set(fact.evidence.map(e => {
      const start = rawContent.lastIndexOf('\n', e.start - 1) + 1;
      const end = rawContent.indexOf('\n', e.end);
      return rawContent.slice(start, end < 0 ? rawContent.length : end);
    }))].join('\n');
    if (!(name === 'publishedAt' ? publicationSupported(fact.value, fact.evidence, rawContent)
      : supportedValue(name, fact.value, fact.evidence.map(e => e.quote), sourceContext))) { report(path, 'VALUE_NOT_SUPPORTED_BY_EVIDENCE'); return unknown(); }
    if (scope && name !== 'title' && fact.evidence.some(e =>
      !(e.start >= scope.start && e.end <= scope.end) && !(name === 'seniority' && e.end <= (bounds[0] ?? 0)))) {
      report(path, 'VACANCY_ATTRIBUTION_UNSUPPORTED'); return unknown();
    }
    if (!scope && bounds.length > 1 && ['sharedRequirements', 'sharedLocation', 'sharedBenefits', 'applicationUrl'].includes(name) &&
      fact.evidence.some(e => e.start >= bounds[0] && e.start < sharedStart)) {
      report(path, 'POST_ATTRIBUTION_UNSUPPORTED'); return unknown();
    }
    if (['mandatorySkills', 'optionalSkills'].includes(name) && !skillObligationSupported(name, fact.value, fact.evidence, rawContent)) {
      report(path, 'SKILL_OBLIGATION_UNSUPPORTED'); return unknown();
    }
    if (fact.status === 'VERIFIED') report(path, 'MODEL_VERIFICATION_DOWNGRADED');
    // Literal support is NOT external verification, freshness or authorization.
    return { value: structuredClone(fact.value), status: 'POST_MENTIONED', evidence: structuredClone(fact.evidence) };
  }
  const result = { schemaVersion: output.schemaVersion };
  for (const k of POST_FIELDS) result[k] = validate(output[k], k, k);
  const seen = new Set(); result.vacancies = [];
  for (const [i, v] of output.vacancies.entries()) {
    const path = `vacancies[${i}]`, title = validate(v.title, 'title', `${path}.title`);
    const start = titleAnchor(title);
    if (start === undefined) { report(path, 'VACANCY_WITHOUT_SUPPORTED_TITLE'); continue; }
    const scope = { start, end: Math.min(bounds.find(n => n > start) ?? rawContent.length, sharedStart) };
    const vacancy = { title };
    for (const k of VACANCY_FIELDS.filter(k => k !== 'title')) vacancy[k] = validate(v[k], k, `${path}.${k}`, scope);
    const identity = JSON.stringify([fold(title.value), vacancy.roleCategory.value, vacancy.location.value,
      [...(vacancy.technologies.value ?? [])].map(fold).sort()]);
    if (seen.has(identity)) { report(path, 'DUPLICATE_VACANCY'); continue; }
    seen.add(identity); result.vacancies.push(vacancy);
  }
  return { ...result, diagnostics };
}
