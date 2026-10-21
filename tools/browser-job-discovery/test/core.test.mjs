import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { extractVacancies, EXTRACTION_VERSION, structuredMetrics } from '../src/vacancy-extraction.mjs';
import { ActivityBudget, SearchPlanner, Stop, facebookAccessGate, requireMode, inspectAccess,
  JobPostExtractor, matchTarget, parseExperience, deduplicate, DiscoveryReporter, runDiscovery } from '../src/core.mjs';

const job = (overrides = {}) => ({ id: 'one', sourceUrl: 'http://fixture.invalid/posts/one', kind: 'group',
  title: 'Developer', employer: 'Synthetic Company', requirements: '.NET Angular. 1+ years experience.',
  location: 'Hanoi', body: 'Synthetic JD', hasImage: false, evidenceKind: 'SYNTHETIC_FIXTURE', ...overrides });

const phaseACases = {
  A: 'Fresher .NET Developer\nCông ty: Synthetic Employer\n.NET Core, ASP.NET Core, Angular\nYêu cầu: 5 tháng đến dưới 1 năm kinh nghiệm\nXuân Phương, Nam Từ Liêm, Hà Nội\nEmail: careers@example.invalid',
  B: 'Fresher Lập trình viên\n## Lập trình Cơ sở dữ liệu (SQL, C#.NET)\nYêu cầu: biết SQL\n## Lập trình Mobile (Flutter)\nYêu cầu: biết Flutter\n315 Trường Chinh, Hà Nội\nOffer từ 10M\nEmail: jobs@example.invalid',
  C: 'Công ty: Synthetic Blue\nHà Nội\n1. QA Engineer — Intern/Fresher\nYêu cầu: test cases\nLương 8–12 triệu\n2. Fresher – Middle .NET Developer\nUp to 35M\nYêu cầu: ASP.NET Core\n3. Data labeling\nInbox',
  D: 'Tuyển Intern/Fresher nghiên cứu bảo mật\nYêu cầu: C/C++, Python\nHà Nội\nNET từ 12–15 triệu',
  E: 'Tuyển .NET Developer Fresher–Middle\nHà Nội\n1. .NET Developer Fresher–Junior\nYêu cầu: .NET 9, Azure, SQL Server, EF Core\n2. .NET Developer Middle\nYêu cầu: 3+ năm kinh nghiệm, Redis, CQRS\n5 Nguyễn Xuân Nham, Hà Nội\nEmail: engineering@example.invalid',
  F: 'Tuyển Fresher PQA\nYêu cầu: kiểm soát quy trình\nHà Nội\nEmail: **hiring**@**example**.invalid',
  G: 'Em tìm việc Fresher .NET tại Hà Nội, nhờ review CV',
  H: 'HCM và Hà Nội tuyển dụng\nKHU VỰC HỒ CHÍ MINH\n1. Fresher .NET Developer\nKHU VỰC HÀ NỘI\n2. Developer Intern/Fresher\nInbox'
};
const extractCase = name => extractVacancies(phaseACases[name], { sourceUrl: 'https://www.facebook.com/example/posts/123', publishedAt: null });

for (const [name, classification] of Object.entries({ A: 'TARGET_MATCH', B: 'TARGET_MATCH', C: 'TARGET_MATCH', D: 'NOT_MATCHED',
  E: 'TARGET_MATCH', F: 'NOT_MATCHED', G: 'NOT_MATCHED', H: 'INSUFFICIENT_DATA' })) {
  test(`Phase A vacancy regression ${name}: ${classification}`, () => {
    const result = extractCase(name);
    assert.equal(result.structuredMatch.classification, classification);
    assert.equal(result.extractionVersion, EXTRACTION_VERSION);
    assert.equal(result.structuredMatch.freshness, 'UNKNOWN');
    assert.ok(result.vacancies.every(v => v.publishedAt === null && v.sourceUrl.endsWith('/123')));
  });
}

test('Phase A single vacancy extracts supported fields/evidence without contact or employer inference', () => {
  const result = extractCase('A'); const vacancy = result.vacancies[0];
  assert.equal(result.title, 'Fresher .NET Developer'); assert.equal(result.employer, 'Synthetic Employer');
  assert.match(result.location, /Xuân Phương/u); assert.match(result.requirements, /5 tháng/u);
  assert.equal(result.contactEmail, 'careers@example.invalid'); assert.equal(result.contactPhone, null);
  assert.ok(vacancy.technologies.includes('Angular')); assert.equal(vacancy.experience.minimum, 5 / 12);
  assert.equal(vacancy.experience.maximum, 1); assert.equal(vacancy.experience.maximumExclusive, true);
  assert.ok(phaseACases.A.includes(vacancy.evidence.title)); assert.match(vacancy.evidence.body, /ASP.NET Core/u);
  assert.equal(extractCase('F').employer, null); assert.equal(extractCase('F').contactEmail, 'hiring@example.invalid');
});

test('Phase A FSS splits backend and Flutter; does not fabricate an upper salary bound', () => {
  const result = extractCase('B'); assert.equal(result.vacancies.length, 2);
  assert.equal(result.title, null); assert.equal(result.requirements, null);
  assert.equal(result.vacancies[0].match.classification, 'TARGET_MATCH');
  assert.equal(result.vacancies[1].match.classification, 'NOT_MATCHED');
  assert.ok(!result.vacancies[1].technologies.includes('.NET'));
  assert.equal(result.salary.minimum, 10_000_000); assert.equal(result.salary.maximum, null);
  assert.equal(result.salary.scope, 'POST_UNASSIGNED');
  assert.equal(result.vacancies[0].salary, null); assert.equal(result.vacancies[1].salary, null);
  assert.equal(result.contactEmail, 'jobs@example.invalid');
});

test('Phase A BlueOC preserves role-specific level/salary, including Fresher in a broad range', () => {
  const result = extractCase('C'); const vacancy = result.vacancies[1];
  assert.equal(result.vacancies.length, 3); assert.match(vacancy.title, /Fresher – Middle/u);
  assert.equal(vacancy.salary.maximum, 35_000_000); assert.equal(vacancy.salary.minimum, null);
  assert.equal(vacancy.salary.appliesToAllSeniorityLevels, false);
  assert.doesNotMatch(vacancy.requirements, /test cases|8–12/u);
  assert.equal(result.contactMethod, 'Inbox'); assert.equal(result.contactEmail, null);
});

test('Phase A TDA does not transfer Middle requirements or introductory Fresher level', () => {
  const result = extractCase('E'); assert.equal(result.vacancies.length, 2);
  const [fresher, middle] = result.vacancies;
  assert.equal(fresher.match.classification, 'TARGET_MATCH'); assert.ok(fresher.technologies.includes('EF Core'));
  assert.doesNotMatch(fresher.requirements, /3\+|Redis|CQRS/u);
  assert.equal(middle.match.classification, 'NOT_MATCHED'); assert.deepEqual(middle.seniority, ['Middle']);
  assert.equal(middle.experience.minimum, 3); assert.equal(result.contactEmail, 'engineering@example.invalid');
});

test('Phase A multi-city assigns section cities, not the global Hanoi headline', () => {
  const [hcm, hanoi] = extractCase('H').vacancies;
  assert.equal(hcm.match.classification, 'NOT_MATCHED'); assert.equal(hcm.match.reason, 'LOCATION_MISMATCH');
  assert.equal(hanoi.match.classification, 'INSUFFICIENT_DATA');
  assert.ok(!hanoi.technologies.includes('.NET'));
  const ambiguous = extractVacancies('HCM và Hà Nội tuyển dụng\nFresher .NET Developer');
  assert.equal(ambiguous.structuredMatch.classification, 'INSUFFICIENT_DATA');
  assert.equal(ambiguous.vacancies[0].location, null);
  const conflict = extractVacancies('HCM tuyển dụng\n1. Junior .NET Developer\nHà Nội\n2. Intern PQA');
  assert.equal(conflict.vacancies[0].match.classification, 'INSUFFICIENT_DATA');
  assert.equal(conflict.vacancies[0].location, null);
});

for (const salaryText of ['Lương NET', 'Thu nhập NET', '15M NET', 'Gross/NET salary', 'NET + bonus']) {
  test(`Phase A salary wording is not a stack: ${salaryText}`, () => {
    const cyber = extractVacancies(`Tuyển Intern bảo mật\nC/C++, Python\nHà Nội\n${salaryText}`);
    assert.equal(cyber.structuredMatch.classification, 'NOT_MATCHED'); assert.ok(!cyber.technologies.includes('.NET'));
    const net = extractVacancies(`Junior .NET Developer\nHanoi\n${salaryText}`);
    assert.equal(net.structuredMatch.classification, 'TARGET_MATCH');
  });
}

test('Phase A English, potential experience, district evidence, Angular-only and discussions', () => {
  assert.equal(extractVacancies('Hiring C# Developer\nRequirements: 1–2 years\nHanoi').structuredMatch.classification, 'POTENTIAL_MATCH');
  assert.equal(extractVacancies('Junior ASP.NET Core Developer\nNam Từ Liêm\nZalo: 0900 000 000').structuredMatch.classification, 'TARGET_MATCH');
  assert.equal(extractVacancies('Junior Angular Developer\nHanoi').structuredMatch.classification, 'NOT_MATCHED');
  assert.equal(extractVacancies('Discussion: how to become a .NET developer\nHanoi').structuredMatch.classification, 'NOT_MATCHED');
  assert.equal(extractVacancies('Hiring Junior Java Developer\nHanoi\nEmail: jobs@example.net').structuredMatch.classification, 'NOT_MATCHED');
  assert.equal(extractVacancies('Junior .NET Developer\nHanoi\nZalo: 0900 000 000').contactPhone, '0900 000 000');
});

test('Phase A hash uses baseline raw input, not extracted fields or extraction version', () => {
  const raw = job({ provider: 'SOCIALAPIS', source: 'FACEBOOK', title: '', employer: '', location: '', requirements: '', body: phaseACases.A });
  const expected = createHash('sha256').update([raw.title, raw.employer, raw.location, raw.requirements, raw.body].join('\n')
    .replace(/\r\n?/gu, '\n').normalize('NFC').trim(), 'utf8').digest('hex');
  const extracted = new JobPostExtractor().extract(raw);
  assert.equal(extracted.contentHash, expected); assert.notEqual(extracted.title, raw.title);
  assert.equal(extracted.body, raw.body); assert.equal(extracted.sourceIdentity, 'SOCIALAPIS:one');
  assert.equal(deduplicate([extracted, { ...extracted, extractionVersion: 'future-version' }]).duplicates.length, 1);
});

test('Phase A preserves literal email underscores, does not merge separate unnumbered roles or salaries', () => {
  const extracted = extractVacancies('Junior .NET Developer\nHanoi\n.NET Developer Middle\n3+ years\nEmail: hr_team@example.invalid');
  assert.equal(extracted.contactEmail, 'hr_team@example.invalid'); assert.equal(extracted.vacancies.length, 2);
  assert.equal(extractVacancies('Email: hr__team@example.invalid').contactEmail, 'hr__team@example.invalid');
  assert.equal(extracted.vacancies[0].match.classification, 'TARGET_MATCH');
  assert.equal(extracted.vacancies[1].match.classification, 'NOT_MATCHED');
  const salaries = extractVacancies('Junior .NET Developer\nHanoi\nLương 10–15 triệu\nSalary 20–25 triệu').salary;
  assert.equal(salaries.minimum, null); assert.equal(salaries.maximum, null);
  assert.equal(extractVacancies('I am looking for a job Junior .NET Developer in Hanoi').structuredMatch.classification, 'NOT_MATCHED');
  assert.equal(extractVacancies('How much does a Junior .NET Developer earn in Hanoi?').structuredMatch.classification, 'NOT_MATCHED');
  assert.equal(extractVacancies('').structuredMatch.classification, 'INSUFFICIENT_DATA');
  assert.deepEqual(extractVacancies('').vacancies, []);
  const emailOnly = extractVacancies('Hiring .NET Developer\nEmail: junior@hanoi.invalid');
  assert.equal(emailOnly.location, null); assert.deepEqual(emailOnly.seniority, []);
  assert.equal(emailOnly.structuredMatch.classification, 'INSUFFICIENT_DATA');
});

test('Phase A metrics separate relevant vacancies/posts, potential matches and field coverage', () => {
  const multiple = extractVacancies('Hanoi\n1. Junior .NET Developer\n2. Intern C# Developer\nEmail: jobs@example.invalid');
  const potential = extractVacancies('Hiring C# Developer\nHanoi\nRequirements: 1–2 years');
  const result = structuredMetrics([multiple, potential, extractCase('D')].map(value => ({ ...value, match: value.structuredMatch })));
  assert.equal(result.relevantVacancies, 3); assert.equal(result.relevantSourcePosts, 2);
  assert.equal(result.potentialMatches, 1); assert.equal(result.unmatchedPosts, 1);
  assert.deepEqual(result.emailCoverage, { withEmail: 1, totalPosts: 3 }); assert.equal(result.unknownFreshness, 3);
});

for (const optional of ['C# là lợi thế', 'Nice to have: C#', 'Java, React; C# is a plus', 'Ưu tiên biết C#']) {
  test(`Phase A.1 Java/React primary stack is not promoted by optional skill: ${optional}`, () => {
    const result = extractVacancies(`Junior Java/React Developer\nHanoi\nRequirements:\nMust know Java and React\n${optional}`);
    assert.equal(result.structuredMatch.classification, 'NOT_MATCHED');
    assert.ok(result.technologies.includes('C#'));
    assert.ok(!result.vacancies[0].primaryTechnologies.includes('C#'));
    assert.equal(result.vacancies[0].match.backend, false);
  });
}

test('Phase A.1 mandatory backend requirements and genuine .NET/Angular titles retain matches', () => {
  for (const body of ['Junior Developer\nHanoi\nRequirements:\nC#, ASP.NET Core, Angular',
    'Junior .NET Angular Developer\nHanoi\nNice to have: React, C#']) {
    assert.equal(extractVacancies(body).structuredMatch.classification, 'TARGET_MATCH');
  }
  const training = extractVacancies('Junior Java Developer\nHanoi\nBenefits:\nTraining C# and ASP.NET Core');
  assert.equal(training.structuredMatch.classification, 'NOT_MATCHED'); assert.equal(training.requirements, null);
});

test('Phase A.1 FSS shared footer requirements/email/offer are not owned by Flutter', () => {
  const result = extractVacancies('# [Hà Nội] FSS tìm Fresher Lập trình viên:\n## Lập trình SQL, C#.NET\n## Lập trình Mobile Flutter\nĐịa điểm làm việc: 315 Trường Chinh, Hà Nội\nYêu cầu đối với Fresher:\nLà SV năm cuối ngành CNTT\nĐã có thể làm việc full-time\nOffer từ 10M\nEmail: **jobs**@**example**.invalid');
  assert.equal(result.employer, 'FSS'); assert.equal(result.employerStatus, 'POST_MENTIONED_UNVERIFIED');
  assert.equal(result.vacancies.length, 2); assert.equal(result.structuredMatch.classification, 'TARGET_MATCH');
  assert.match(result.sharedRequirements, /SV năm cuối/u); assert.equal(result.requirements, result.sharedRequirements);
  assert.doesNotMatch(result.sharedRequirements, /315|Offer|Email/u);
  assert.equal(result.contactEmail, 'jobs@example.invalid'); assert.equal(result.salary.minimum, 10_000_000);
  assert.equal(result.salary.scope, 'POST_UNASSIGNED');
  for (const vacancy of result.vacancies) {
    assert.equal(vacancy.contactEmail, null); assert.equal(vacancy.requirements, null); assert.equal(vacancy.salary, null);
    assert.ok(vacancy.location.includes('Hà Nội'));
  }
});

test('Phase A.1 TDA email footer is post-owned, not exclusively Middle-owned', () => {
  const result = extractVacancies('TDA đang tuyển .NET Developer\nHanoi\n1. .NET Developer Fresher–Junior\nYêu cầu: OOP và SQL\n2. .NET Developer Middle\nYêu cầu: 3+ năm kinh nghiệm\nSend CV: engineering@example.invalid\nZalo: 0900 000 000');
  assert.equal(result.employer, 'TDA'); assert.equal(result.vacancies.length, 2);
  assert.equal(result.contactEmail, 'engineering@example.invalid'); assert.equal(result.contactPhone, '0900 000 000');
  assert.ok(result.vacancies.every(v => v.contactEmail === null && v.contactPhone === null));
  assert.match(result.vacancies[0].requirements, /OOP/u); assert.match(result.vacancies[1].requirements, /3\+/u);
  assert.doesNotMatch(result.vacancies[1].requirements, /Send CV|Zalo/u);
});

test('Phase A.1 one requirements section following a bare role list stays post-owned', () => {
  const result = extractVacancies('Hanoi\n1. Fresher .NET Developer\n2. Fresher Flutter Developer\nRequirements:\nMust know SQL\nContact: jobs@example.invalid');
  assert.equal(result.sharedRequirements, 'Must know SQL');
  assert.equal(result.requirements, 'Must know SQL');
  assert.ok(result.vacancies.every(v => v.requirements === null && v.contactEmail === null));
  assert.equal(result.structuredMatch.classification, 'TARGET_MATCH');
});

test('Phase A.1 BlueOC no technical requirements are fabricated from its footer', () => {
  const result = extractVacancies('BlueOC đang tìm kiếm đồng đội\nHà Nội\n1. QA Engineer Intern/Fresher\n2. Fresher – Middle .NET Developer\nUp to 35M\nInbox');
  assert.equal(result.employer, 'BlueOC'); assert.equal(result.employerStatus, 'POST_MENTIONED_UNVERIFIED');
  assert.equal(result.structuredMatch.classification, 'TARGET_MATCH');
  assert.equal(result.vacancies[1].requirements, null); assert.equal(result.vacancies[1].salary.maximum, 35_000_000);
  assert.equal(result.vacancies[1].contactMethod, null); assert.equal(result.contactMethod, 'Inbox');
});

test('Phase A.1 LUVINA PQA requirement/benefit mentions do not create extra vacancies', () => {
  const result = extractVacancies('LUVINA đang tuyển Fresher PQA\nHanoi\nYêu cầu:\n- Có hiểu biết về quy trình PQA\n1. Tối thiểu 6 tháng kinh nghiệm PQA\n- Ưu tiên hiểu công việc của Developer\nQuyền lợi:\n- Được đào tạo bởi Senior QA Engineer\n- Hỗ trợ Developer trong dự án\nEmail: hiring@example.invalid');
  assert.equal(result.vacancies.length, 1); assert.match(result.vacancies[0].title, /Fresher PQA/u);
  assert.equal(result.structuredMatch.classification, 'NOT_MATCHED');
  assert.match(result.requirements, /quy trình PQA/u); assert.doesNotMatch(result.requirements, /đào tạo|Hỗ trợ|Email/u);
});

test('Phase A.1 requirements exclude post introductions, office, benefits and closing messages', () => {
  const result = extractVacancies('Junior .NET Developer\nChúng mình đang tìm kiếm đồng đội ở Hà Nội\n📍 Địa điểm: Cầu Giấy, Hà Nội\nBạn phù hợp nếu:\n- Có kiến thức C# và SQL\nKhi tham gia cùng chúng mình, bạn sẽ có cơ hội:\n- Tham gia dự án quốc tế\n- Hỗ trợ máy tính\nCảm ơn mọi người!');
  assert.equal(result.requirements, '- Có kiến thức C# và SQL');
  const noRequirements = extractVacancies('Junior .NET Developer\nHanoi\nBenefits:\nFree lunch\nEmail: jobs@example.invalid');
  assert.equal(noRequirements.requirements, null);
  assert.equal(extractVacancies('Email: tda@example.invalid').employer, null);
  assert.equal(extractVacancies('Chúng mình đang tìm kiếm Junior .NET Developer\nHanoi').employer, null);
  const numbered = extractVacancies('Hanoi tuyển dụng\n1. Junior .NET Developer (0–2 years experience)\n2. Middle .NET Developer (3+ years experience)');
  assert.equal(numbered.vacancies.length, 2); assert.equal(numbered.vacancies[0].match.classification, 'TARGET_MATCH');
});

for (const subject of ['Nếu bạn', 'Bạn']) {
  test(`Phase A.1.1 candidate-facing subject is not an employer: ${subject}`, () => {
    const body = `Fresher .NET Developer\nHanoi\n${subject} đang tìm kiếm cơ hội phát triển với .NET.`;
    assert.equal(extractVacancies(body).employer, null);
    for (const [name, phrase] of [['BlueOC', 'đang tìm kiếm'], ['TDA', 'đang tuyển'], ['FSS', 'tìm Fresher']]) {
      const result = extractVacancies(`${name} ${phrase} đồng đội\n${body}`);
      assert.equal(result.employer, name); assert.equal(result.employerStatus, 'POST_MENTIONED_UNVERIFIED');
    }
    assert.equal(extractVacancies(`${body}\nEmail: nccplus@example.invalid`).employer, null);
  });
}

test('Phase A.1.1 PQA personality requirement is not a vacancy heading', () => {
  const result = extractVacancies('LUVINA đang tuyển Fresher PQA\nHanoi\nYêu cầu:\nCẩn thận, trách nhiệm, thích công việc PQA.\nQuyền lợi:\nSenior Developer mentoring staff.');
  assert.equal(result.vacancies.length, 1); assert.match(result.vacancies[0].title, /Fresher PQA/u);
  assert.match(result.vacancies[0].requirements, /Cẩn thận, trách nhiệm/u);
  assert.equal(result.structuredMatch.classification, 'NOT_MATCHED');
});

test('Phase A.1.1 mentoring sentences do not split real unnumbered or numbered role lists', () => {
  for (const prefix of ['', '1. ']) {
    const second = prefix ? '2. ' : '';
    const result = extractVacancies(`Hanoi\n${prefix}Junior .NET Developer\nRequirements: SQL\nSenior Developer mentoring staff\n${second}Intern C# Developer\nRequirements: OOP\nSenior Developer sẽ hướng dẫn các bạn.`);
    assert.equal(result.vacancies.length, 2);
    assert.deepEqual(result.vacancies.map(v => v.match.classification), ['TARGET_MATCH', 'TARGET_MATCH']);
  }
  const titles = extractVacancies('Hanoi\nFull-stack .NET Developer, Junior\nRequirements: SQL\nSoftware C# Developer, Intern\nRequirements: OOP');
  assert.equal(titles.vacancies.length, 2);
  assert.ok(titles.vacancies.every(v => v.match.classification === 'TARGET_MATCH'));
});

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
