import { createHash } from 'node:crypto';

// SYNTHETIC_FROM_USER_EXAMPLE. Not a captured Facebook post or model output.
export const rawContent = [
  'Rikkeisoft tuyển Developer Intern 2026',
  'Rikkei Tech Talent Program 2026',
  'Frontend: ReactJS, Angular, VueJS, NextJS',
  'Backend: Java, Golang, .NET, NodeJS, Python, PHP',
  'Mobile: iOS, Android, Flutter, React Native',
  'Yêu cầu chung: Sinh viên đại học hoặc mới tốt nghiệp; OOP, DSA, SQL, API; ưu tiên dự án cá nhân.',
  'Địa chỉ: Số 3 Dương Đình Nghệ, Cầu Giấy, Hà Nội',
  'Ứng tuyển: https://forms.cloud.microsoft/r/GDpr0Lyxyr'
].join('\n');
export const sourceInput = {
  sourcePostId: 'synthetic-rikkeisoft-01', sourceUrl: null, rawContent,
  contentHash: createHash('sha256').update(rawContent, 'utf8').digest('hex'), publishedAt: null
};
export const address = 'Số 3 Dương Đình Nghệ, Cầu Giấy, Hà Nội';
export const applicationUrl = 'https://forms.cloud.microsoft/r/GDpr0Lyxyr';
export function fact(value, quote, status = 'POST_MENTIONED') {
  const start = rawContent.indexOf(quote);
  if (start < 0) throw Error('Invalid synthetic test evidence');
  return { value, status, evidence: [{ start, end: start + quote.length, quote }] };
}
export const unknown = () => ({ value: null, status: 'UNKNOWN', evidence: [] });

// Expected values originate from the task example, NOT the legacy extractor.
export function mockOutput() {
  return {
    schemaVersion: 'ai-job-extraction-v1',
    employer: fact('Rikkeisoft', 'Rikkeisoft'),
    recruitmentProgram: fact('Rikkei Tech Talent Program 2026', 'Rikkei Tech Talent Program 2026'),
    sharedLocation: fact(address, address),
    sharedRequirements: fact('Sinh viên đại học hoặc mới tốt nghiệp; OOP, DSA, SQL, API; ưu tiên dự án cá nhân.',
      'Sinh viên đại học hoặc mới tốt nghiệp; OOP, DSA, SQL, API; ưu tiên dự án cá nhân.'),
    sharedBenefits: unknown(),
    applicationUrl: fact(applicationUrl, applicationUrl),
    recruitmentStatus: fact('ANNOUNCED_UNVERIFIED', 'tuyển Developer Intern 2026'),
    vacancies: [
      ['Frontend', 'FRONTEND', ['ReactJS', 'Angular', 'VueJS', 'NextJS']],
      ['Backend', 'BACKEND', ['Java', 'Golang', '.NET', 'NodeJS', 'Python', 'PHP']],
      ['Mobile', 'MOBILE', ['iOS', 'Android', 'Flutter', 'React Native']]
    ].map(([title, role, technologies]) => ({
      title: fact(title, title + ':'), roleCategory: fact(role, title + ':'),
      seniority: fact('INTERN', 'Intern'),
      technologies: fact(technologies, `${title}: ${technologies.join(', ')}`),
      // A list of alternative stacks is not a claim that all are mandatory.
      mandatorySkills: unknown(), optionalSkills: unknown(), requirements: unknown(),
      experienceYears: unknown(), salary: unknown(), location: unknown(), applicationUrl: unknown()
    })),
    publishedAt: unknown()
  };
}

// Test double only; no SDK, credentials, HTTP, or claim of model quality.
export function mockProvider(output = mockOutput()) {
  const calls = [];
  return { modelIdentity: 'offline-mock-v1', calls,
    async extract(request) { calls.push(structuredClone(request)); return JSON.stringify(output); }
  };
}
