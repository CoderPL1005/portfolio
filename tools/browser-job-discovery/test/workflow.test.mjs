import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { mkdirSync, mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
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
  const checkpoint = steps.findIndex(step => step.id === 'checkpoint');
  assert.ok(steps.findIndex(step => step.run === 'npm test') < checkpoint);
  assert.ok(job.if.includes("github.ref == 'refs/heads/main'"));
  assert.equal(workflow.concurrency.group, 'socialapis-job-discovery-state');
  const download = steps.findIndex(step => step.uses?.startsWith('actions/download-artifact'));
  const prepare = steps.findIndex(step => step.name === 'Validate and prepare state before provider access');
  const uploads = steps.flatMap((step, i) => step.uses?.startsWith('actions/upload-artifact') ? [i] : []);
  assert.ok(download < uploads[0] && uploads[0] < live && live < uploads[1]);
  assert.equal(uploads.length, 2); assert.ok(download < prepare && prepare < uploads[0]);
  assert.equal(steps[live].env.SOCIALAPIS_API_KEY, '${{ secrets.SOCIALAPIS_API_KEY }}');
  assert.equal(steps[live].env.SOCIALAPIS_MAX_REQUESTS, '1'); assert.equal(steps[live].env.SOCIALAPIS_MAX_CREDITS, '1');
  assert.ok(steps[live].run.includes('src/cli.mjs --source socialapis --live'));
  assert.equal(steps.filter(step => step.env?.SOCIALAPIS_API_KEY).length, 1);
  assert.equal(JSON.stringify(workflow).match(/secrets\.SOCIALAPIS_API_KEY/gu)?.length, 1);
  assert.equal(workflow.env?.SOCIALAPIS_API_KEY, undefined); assert.equal(job.env?.SOCIALAPIS_API_KEY, undefined);
  assert.equal(steps.find(s => s.uses?.startsWith('actions/setup-node')).with['node-version'], '22.12.0');
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
  let repoChecks = 0;
  async function check({ prior = true, artifact = true, initialize = false, count = 2, attempt = '1', resetCap = '2', expired = false, currentIncluded = true } = {}) {
    repoChecks = 0;
    process.env.GITHUB_RUN_ATTEMPT = attempt;
    if (resetCap === undefined) delete process.env.MAX_31_DAY_REQUESTS; else process.env.MAX_31_DAY_REQUESTS = resetCap;
    process.env.INITIALIZE_STATE = String(initialize);
    const outputs = {};
    const github = { rest: { repos: { get: async () => { repoChecks++; return { data: { private: false } }; } }, actions: {
      listWorkflowRuns: async params => params.created ? { data: { total_count: count, workflow_runs: [{ id: currentIncluded ? 200 : 199, conclusion: 'failure' }] } } : { data: {
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
    for (const invalid of ['', ' ', '0', '-1', '1.5', '2.0', '02', '2e0', 'NaN', '3', '20', '2\n']) {
      await assert.rejects(() => check({ resetCap: invalid }), /INVALID_ROLLING_REQUEST_CAP/u);
      assert.equal(repoChecks, 0);
    }
    // Explicitly delete the variable: an absent setting must not fall back.
    delete process.env.MAX_31_DAY_REQUESTS;
    const forbidden = () => assert.fail('invalid configuration reached a GitHub API');
    await assert.rejects(() => execute({ rest: { repos: { get: forbidden } } }, {}, {}), /INVALID_ROLLING_REQUEST_CAP/u);
    await assert.rejects(() => check({ count: 3 }), /ROLLING_REQUEST_CAP_EXCEEDED/u);
    await assert.rejects(() => check({ count: 2, currentIncluded: false }), /ROLLING_REQUEST_CAP_EXCEEDED/u);
    assert.equal((await check({ count: 1, resetCap: '1' })).run_id, '100');
  } finally {
    for (const [key, value] of Object.entries(previousEnv)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; }
  }
});

const liveStep = discoveryWorkflow.jobs.discover.steps.find(step => step.name === 'Discover exactly one page');
const bash = process.platform === 'win32' ? join(process.env.ProgramFiles ?? 'C:\\Program Files', 'Git', 'bin', 'bash.exe') : 'bash';
const toolDirectory = fileURLToPath(new URL('../', import.meta.url));

test('B.1 date inputs are optional/env-only and cap has no implicit fallback', () => {
  const inputs = discoveryWorkflow.on.workflow_dispatch.inputs;
  for (const name of ['start_time', 'end_time']) {
    assert.equal(inputs[name].type, 'string'); assert.equal(inputs[name].default, '');
  }
  assert.equal(inputs.query.default, 'tuyển fresher .NET Hà Nội');
  assert.equal(liveStep.env.DISCOVERY_START_TIME, '${{ inputs.start_time }}');
  assert.equal(liveStep.env.DISCOVERY_END_TIME, '${{ inputs.end_time }}');
  assert.equal(discoveryWorkflow.jobs.discover.steps.find(step => step.id === 'checkpoint').env.MAX_31_DAY_REQUESTS,
    '${{ vars.SOCIALAPIS_MAX_31_DAY_REQUESTS }}');
  assert.equal(liveStep.shell, 'bash'); assert.doesNotMatch(liveStep.run, /\$\{\{|\b(?:for|while|until|curl|wget)\b/gu);
});

test('B.1 actual Bash step forwards empty/single/both dates and treats input as data, not shell', () => {
  const query = 'C# .NET Hà Nội';
  for (const [start, end] of [['', ''], ['2026-10-18', ''], ['', '2026-10-21'], ['2026-10-18', '2026-10-21'],
    ['$(printf UNSAFE_EXPANSION)', '"; printf UNSAFE_EXPANSION; #']]) {
    const output = execFileSync(bash, ['--noprofile', '--norc', '-c', 'node() { printf "%s\\0" "$@"; }\n' + liveStep.run], {
      cwd: toolDirectory, encoding: 'utf8', env: { ...process.env, DISCOVERY_QUERY: query, DISCOVERY_START_TIME: start, DISCOVERY_END_TIME: end }
    });
    const args = output.split('\0'); assert.equal(args.pop(), '');
    assert.deepEqual(args, ['src/cli.mjs', '--source', 'socialapis', '--live', '--query', query,
      ...(start ? ['--start-time', start] : []), ...(end ? ['--end-time', end] : [])]);
  }
});

test('B.1 actual workflow Bash reaches CLI validation and persists the existing complete artifact through mock transport only', () => {
  const root = resolve(toolDirectory, '.debug'); mkdirSync(root, { recursive: true });
  const directory = mkdtempSync(join(root, 'b1-workflow-')); const output = join(directory, 'state.json');
  const payload = { data: { items: [{ basic_info: { post_id: '12345', post_text: 'Hiring Junior .NET Developer in Hanoi',
    url: 'https://www.facebook.com/fixture/posts/12345' }, owner_info: {}, feedback_details: {}, video_details: {} }] } };
  const fake = 'data:text/javascript,' + encodeURIComponent(`import net from 'node:net'; import assert from 'node:assert/strict';
    net.Socket.prototype.connect=function(){throw new Error('NETWORK_FORBIDDEN');}; let calls=0;
    globalThis.fetch=async input=>{assert.equal(++calls,1); const url=new URL(input);
      assert.equal(url.searchParams.get('start_time'),'2026-10-18'); assert.equal(url.searchParams.get('end_time'),'2026-10-21');
      return new Response(${JSON.stringify(JSON.stringify(payload))},{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});};`);
  const script = 'node() { command "$WORKFLOW_TEST_NODE" --import "$WORKFLOW_TEST_IMPORT" "$@" --output "$WORKFLOW_TEST_OUTPUT"; }\n' + liveStep.run;
  const run = (start, end) => execFileSync(bash, ['--noprofile', '--norc', '-c', script], { cwd: toolDirectory, encoding: 'utf8', stdio: 'pipe',
    env: { ...process.env, ...liveStep.env, WORKFLOW_TEST_NODE: process.execPath.replace(/\\/gu, '/'), WORKFLOW_TEST_IMPORT: fake,
      WORKFLOW_TEST_OUTPUT: output, SOCIALAPIS_API_KEY: 'synthetic-b1-workflow-key', DISCOVERY_QUERY: 'synthetic-query',
      DISCOVERY_START_TIME: start, DISCOVERY_END_TIME: end } });
  try {
    writeFileSync(output, JSON.stringify({ evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] }));
    const before = readFileSync(output, 'utf8');
    for (const [start, end, code] of [['2026-02-30', '', 'INVALID_DATE_FILTER'], ['2026-10-22', '2026-10-21', 'INVALID_DATE_RANGE']]) {
      assert.throws(() => run(start, end), error => error.stderr.toString().includes(code));
      assert.equal(readFileSync(output, 'utf8'), before);
    }
    const stdout = run('2026-10-18', '2026-10-21');
    const state = JSON.parse(readFileSync(output, 'utf8'));
    for (const field of ['queryYield', 'providerUsage', 'itemStats', 'storage', 'jobs', 'accumulatedMetrics']) assert.ok(state[field]);
    assert.equal(state.queryYield.requestMetadata.startTime, '2026-10-18');
    assert.equal(state.queryYield.requestMetadata.providerDateFilterBehavior, 'UNVERIFIED');
    assert.equal(state.queryYield.verifiedRecentRelevantVacancies, 0); assert.equal(state.jobs[0].publishedAt, null);
    assert.equal(state.providerUsage.requestCount, 1); assert.equal(state.storage.inserted, 1);
    assert.doesNotMatch(stdout, /synthetic-b1-workflow-key|Hiring Junior/u);
    run('2026-10-18', '2026-10-21');
    const again = JSON.parse(readFileSync(output, 'utf8'));
    assert.equal(again.queryYield.newSourcePosts, 0); assert.equal(again.storage.unchanged, 1);
  } finally {
    assert.ok(resolve(directory).startsWith(root + sep)); rmSync(directory, { recursive: true, force: true });
  }
});
