// Explicit offline comparison runner; synthetic task example only. No real model.
import { compareExtraction } from '../src/ai-extraction-evaluation.mjs';
import { MockExtractionProvider } from '../src/ai-extraction-mock-provider.mjs';
import { sourceInput, mockOutput, address, applicationUrl } from './fixtures/ai-extraction-v1.mjs';

const expected = { employer: 'Rikkeisoft', titles: ['Frontend', 'Backend', 'Mobile'], vacancyCount: 3,
  roles: ['FRONTEND', 'BACKEND', 'MOBILE'], seniorities: [['INTERN'], ['INTERN'], ['INTERN']], location: address,
  technologies: mockOutput().vacancies.map(v => v.technologies.value), requirements: mockOutput().sharedRequirements.value,
  applicationUrl, salary: null, experienceYears: [null, null, null], publishedAt: null };
const report = await compareExtraction({ input: sourceInput, expected, provider: new MockExtractionProvider(mockOutput()) });
console.log(JSON.stringify({ ...report, datasetKind: 'SYNTHETIC_FROM_USER_EXAMPLE', sampleCount: 1,
  missingRequestedRealObservations: 5, realProviderRequests: 0,
  baselineExactFields: report.baseline.filter(x => x.outcome === 'EXACT_MATCH').length,
  mockExactFields: report.mockPipeline.filter(x => x.outcome === 'EXACT_MATCH').length }, null, 2));
