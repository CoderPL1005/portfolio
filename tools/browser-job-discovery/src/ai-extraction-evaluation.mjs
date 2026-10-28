import { isDeepStrictEqual } from 'node:util';
import { JobPostExtractor } from './core.mjs';
import { extractRecruitmentPost } from './ai-job-extraction.mjs';

// Read-only, caller-provided dataset and golden labels. No fixture loading,
// credentials, disk writes, matching changes, or real provider by default.
export async function compareExtraction({ input, expected, provider }) {
  const baseline = new JobPostExtractor().extract({ id: input.sourcePostId, source: 'FACEBOOK', provider: 'SOCIALAPIS',
    sourceUrl: input.sourceUrl, body: input.rawContent, title: '', employer: '', location: '', requirements: '',
    publishedAt: input.publishedAt, hasImage: false });
  const ai = await extractRecruitmentPost(input, { provider });
  const before = {
    employer: baseline.employer, titles: baseline.vacancies.map(v => v.title), vacancyCount: baseline.vacancies.length,
    roles: baseline.vacancies.map(v => v.primaryRole), seniorities: baseline.vacancies.map(v => v.seniority.map(s => s.toUpperCase())),
    location: baseline.location, technologies: baseline.vacancies.map(v => v.technologies),
    requirements: baseline.sharedRequirements ?? baseline.requirements,
    applicationUrl: baseline.sharedApplicationDetails?.applicationUrl ?? (baseline.vacancies.length === 1 ? baseline.vacancies[0].applicationUrl : null),
    salary: baseline.salary, experienceYears: baseline.vacancies.map(v => v.experience.minimum), publishedAt: baseline.publishedAt
  };
  const after = {
    employer: ai.employer.value, titles: ai.vacancies.map(v => v.title.value), vacancyCount: ai.vacancies.length,
    roles: ai.vacancies.map(v => v.roleCategory.value), seniorities: ai.vacancies.map(v => v.seniority.value === null ? [] : [v.seniority.value]),
    location: ai.sharedLocation.value, technologies: ai.vacancies.map(v => v.technologies.value), requirements: ai.sharedRequirements.value,
    applicationUrl: ai.applicationUrl.value, salary: ai.vacancies.some(v => v.salary.value !== null) ? ai.vacancies.map(v => v.salary.value) : null,
    experienceYears: ai.vacancies.map(v => v.experienceYears.value), publishedAt: ai.publishedAt.value
  };
  const summarize = values => Object.entries(expected).map(([field, golden]) => {
    const actual = values[field];
    const empty = v => v === null || v === undefined || Array.isArray(v) && v.every(empty);
    return { field, expected: golden, actual, outcome: isDeepStrictEqual(actual, golden) ? 'EXACT_MATCH'
      : empty(actual) ? 'MISSING' : 'MISMATCH', unexpectedWhenExpectedUnknown: empty(golden) && !empty(actual) };
  });
  return { evaluationKind: 'CALLER_LABELLED_OFFLINE_COMPARISON', realLlmQuality: 'NOT_EVALUATED',
    sourcePostId: input.sourcePostId, baseline: summarize(before), mockPipeline: summarize(after),
    validationDiagnostics: ai.diagnostics, verificationStatus: ai.verificationStatus };
}
