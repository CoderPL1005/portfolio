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

const discoveryWorkflow = parse(await readFile(new URL('../../../.github/workflows/socialapis-job-discovery.yml', import.meta.url), 'utf8'));
test('SocialAPIs workflow is manual/standard/read-only, restores and checkpoints before ONE provider call', () => {
  const workflow = discoveryWorkflow; const job = workflow.jobs.discover; const steps = job.steps;
  assert.deepEqual(Object.keys(workflow.on), ['workflow_dispatch']);
  assert.deepEqual(workflow.permissions, { contents: 'read', actions: 'read' });
  assert.equal(workflow.concurrency['cancel-in-progress'], false); assert.equal(job['runs-on'], 'ubuntu-latest');
  assert.ok(job['timeout-minutes'] <= 10);
  for (const name of ['confirm_live', 'confirm_free_no_cash', 'confirm_artifact_data']) {
    assert.equal(workflow.on.workflow_dispatch.inputs[name].default, false); assert.ok(job.if.includes(`inputs.${name}`));
  }
  const live = steps.findIndex(step => step.name === 'Discover exactly one page');
  const download = steps.findIndex(step => step.uses?.startsWith('actions/download-artifact'));
  const uploads = steps.flatMap((step, i) => step.uses?.startsWith('actions/upload-artifact') ? [i] : []);
  assert.ok(download < uploads[0] && uploads[0] < live && live < uploads[1]);
  assert.equal(steps[live].env.SOCIALAPIS_API_KEY, '${{ secrets.SOCIALAPIS_API_KEY }}');
  assert.equal(steps[live].env.SOCIALAPIS_MAX_REQUESTS, '1'); assert.equal(steps[live].env.SOCIALAPIS_MAX_CREDITS, '1');
  assert.ok(steps[live].run.includes('src/cli.mjs --source socialapis --live'));
  assert.equal(steps.filter(step => step.env?.SOCIALAPIS_API_KEY).length, 1);
  for (const index of uploads) {
    assert.equal(steps[index].with.path, 'tools/browser-job-discovery/.debug/socialapis-discovery.json');
    assert.equal(steps[index].with['retention-days'], 90); assert.equal(steps[index].with['if-no-files-found'], 'error');
  }
  assert.equal(steps.find(step => step.id === 'checkpoint').with.retries, 0);
});

test('actual checkpoint script fails closed for lost state, reset, rerun and quota; selects prior artifact', async () => {
  const script = discoveryWorkflow.jobs.discover.steps.find(step => step.id === 'checkpoint').with.script;
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  const execute = new AsyncFunction('github', 'context', 'core', script);
  const previousEnv = Object.fromEntries(['GITHUB_RUN_ATTEMPT', 'MAX_31_DAY_REQUESTS', 'INITIALIZE_STATE'].map(key => [key, process.env[key]]));
  async function check({ prior = true, artifact = true, initialize = false, count = 2, attempt = '1', resetCap = '20', expired = false } = {}) {
    process.env.GITHUB_RUN_ATTEMPT = attempt; process.env.MAX_31_DAY_REQUESTS = resetCap; process.env.INITIALIZE_STATE = String(initialize);
    const outputs = {};
    const github = { rest: { repos: { get: async () => ({ data: { private: false } }) }, actions: {
      listWorkflowRuns: async params => params.created ? { data: { total_count: count, workflow_runs: [{ id: 200 }] } } : { data: {
        total_count: prior ? 2 : 1, workflow_runs: [{ id: 200 }, ...(prior ? [{ id: 100, status: 'completed' }] : [])] } },
      listWorkflowRunArtifacts: async () => ({ data: { artifacts: artifact ? [{ name: 'socialapis-discovery-state', expired }] : [] } })
    } } };
    await execute(github, { repo: { owner: 'fixture', repo: 'fixture' }, runId: 200 }, { setOutput: (key, value) => { outputs[key] = value; } });
    return outputs;
  }
  try {
    assert.equal((await check()).run_id, '100'); assert.equal((await check()).artifact_name, 'socialapis-discovery-state');
    assert.equal((await check({ prior: false, initialize: true })).initialize, 'true');
    await assert.rejects(() => check({ prior: false }), /FIRST_RUN_EXPLICIT_INITIALIZATION_REQUIRED/u);
    await assert.rejects(() => check({ artifact: false }), /CHECKPOINT_MISSING_OR_EXPIRED/u);
    await assert.rejects(() => check({ expired: true }), /CHECKPOINT_MISSING_OR_EXPIRED/u);
    await assert.rejects(() => check({ initialize: true }), /CANNOT_RESET_EXISTING_DISCOVERY_HISTORY/u);
    await assert.rejects(() => check({ count: 21 }), /ROLLING_REQUEST_CAP_EXCEEDED/u);
    await assert.rejects(() => check({ attempt: '2' }), /RERUN_DISABLED/u);
    await assert.rejects(() => check({ resetCap: '1000' }), /INVALID_ROLLING_REQUEST_CAP/u);
  } finally {
    for (const [key, value] of Object.entries(previousEnv)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; }
  }
});
