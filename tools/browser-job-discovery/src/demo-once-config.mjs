import { readFileSync, appendFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { resolve } from 'node:path';

export const DEMO_MARKER = '.github/discovery/demo-once-2026-10-26.arm';
export const DEMO_CONFIG = '.github/discovery/demo-once-2026-10-26.config.json';
export const DEMO_TAG = 'discovery-demo-2026-10-26-claimed';

const expected = {
  schemaVersion: 1,
  demoId: 'discovery-demo-2026-10-26',
  query: 'Fresher .NET Hà Nội',
  startTime: '2026-10-26',
  endTime: '2026-10-26',
  maxRequests: 1,
  maxCredits: 1,
  policy: {
    mode: 'development',
    profile: {
      technologies: ['.NET', 'C#', 'ASP.NET Core', 'EF Core'],
      locations: ['Hà Nội'],
      seniorities: ['Intern', 'Fresher', 'Junior']
    }
  }
};

// This is intentionally pinned to a single, pre-authorized development demo.
// Any budget/date/query/policy change needs a separately reviewed implementation.
export function validateDemoConfig(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      JSON.stringify(value) !== JSON.stringify(expected)) {
    throw new Error('ONE_SHOT_DEMO_CONFIG_MISMATCH');
  }
  return {
    confirm_live: 'true',
    confirm_free_no_cash: 'true',
    confirm_artifact_data: 'false',
    initialize_state: 'false',
    auto_admin_sync: 'true',
    sync_only: 'false',
    resume_continuation: 'false',
    reconcile_terminal_failure: 'false',
    query: value.query,
    start_time: value.startTime,
    end_time: value.endTime,
    max_requests: String(value.maxRequests),
    max_credits: String(value.maxCredits),
    policy: JSON.stringify(value.policy)
  };
}

export function isNewDemoMarker(files) {
  return Array.isArray(files) && files.some(file => file?.filename === DEMO_MARKER && file?.status === 'added');
}

// Run by the trigger workflow only after its push-diff and single-attempt guards.
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const file = resolve(process.cwd(), DEMO_CONFIG);
  const inputs = validateDemoConfig(JSON.parse(readFileSync(file, 'utf8')));
  if (!process.env.GITHUB_OUTPUT) throw new Error('GITHUB_OUTPUT_REQUIRED');
  // A single compact JSON line is safely consumed as a step output.
  appendFileSync(process.env.GITHUB_OUTPUT, `inputs=${JSON.stringify(inputs)}\n`, 'utf8');
  console.log(JSON.stringify({demoId:expected.demoId, configValidated:true, maxRequests:1, maxCredits:1, providerRequests:0}));
}
