import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { parse } from 'yaml';

test('cloud workflow is manual-only, read-only, single-session and fixture-only with no credential artifacts', async () => {
  const text = await readFile(new URL('../../../.github/workflows/browser-discovery-offline.yml', import.meta.url), 'utf8');
  const workflow = parse(text);
  assert.deepEqual(Object.keys(workflow.on), ['workflow_dispatch']);
  assert.deepEqual(workflow.permissions, { contents: 'read' });
  assert.equal(workflow.concurrency.group, 'browser-discovery-offline');
  assert.equal(workflow.concurrency['cancel-in-progress'], false);
  assert.deepEqual(Object.keys(workflow.jobs), ['offline-fixtures']);
  assert.equal(workflow.jobs['offline-fixtures']['timeout-minutes'], 10);
  assert.equal(workflow.jobs['offline-fixtures']['runs-on'], 'ubuntu-latest');
  const steps = workflow.jobs['offline-fixtures'].steps;
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'));
  const nodeVersion = steps.find(s => s.uses?.startsWith('actions/setup-node'))?.with['node-version'];
  assert.equal(packageJson.engines.node, `>=${nodeVersion}`);
  assert.equal(workflow.jobs['offline-fixtures'].defaults.run['working-directory'], 'tools/browser-job-discovery');
  assert.ok(steps.some(s => s.run === 'npm ci --ignore-scripts --no-audit --no-fund'));
  assert.ok(steps.some(s => s.run === 'npm test'));
  assert.equal(steps.find(s => s.uses?.startsWith('actions/checkout'))?.with['persist-credentials'], false);
  assert.ok(steps.some(s => s.run === 'npm run test:browser'));
  assert.ok(steps.some(s => s.run === 'npm run simulate'));
  assert.ok(steps.some(s => s.run?.includes('playwright install --with-deps chromium')));
  const reportStep = steps.find(s => s.name === 'Validate and print credential-free JSON report');
  assert.equal(reportStep.shell, 'bash');
  assert.match(reportStep.run, /readFile\('output\/report\.json'/u);
  assert.match(reportStep.run, /assert\.doesNotMatch/u);
  assert.match(reportStep.run, /console\.log\(json\)/u);
  assert.ok(!steps.some(s => /upload-artifact|cache@/u.test(s.uses ?? '')));
  // Mentioning storageState in a deny-pattern is safe; configuring/writing it is not.
  assert.doesNotMatch(text, /secrets\.|storageState\s*[:=(]|--storage-state|schedule:|workflow_run:|repository_dispatch:/u);
});
