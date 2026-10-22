import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { mkdirSync, mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { parse } from 'yaml';

const workflowText = await readFile(new URL('../../../.github/workflows/socialapis-job-discovery.yml', import.meta.url), 'utf8');
const discoveryWorkflow = parse(workflowText);
const actionText = await readFile(new URL('../../../.github/actions/socialapis-discovery-page/action.yml', import.meta.url), 'utf8');
const action = parse(actionText);
const directory = fileURLToPath(new URL('../', import.meta.url));

test('offline browser workflow remains manual/read-only/standard and has no provider secrets', async () => {
  const text = await readFile(new URL('../../../.github/workflows/browser-discovery-offline.yml', import.meta.url), 'utf8');
  const workflow = parse(text); const job = workflow.jobs['offline-fixtures'];
  assert.deepEqual(Object.keys(workflow.on), ['workflow_dispatch']); assert.deepEqual(workflow.permissions, { contents: 'read' });
  assert.equal(workflow.concurrency.group, 'browser-discovery-offline'); assert.equal(workflow.concurrency['cancel-in-progress'], false);
  assert.equal(job['runs-on'], 'ubuntu-latest'); assert.equal(job['timeout-minutes'], 10);
  for (const command of ['npm ci --ignore-scripts --no-audit --no-fund','npm test','npm run test:browser','npm run simulate']) assert.ok(job.steps.some(s => s.run === command));
  assert.ok(job.steps.some(s => s.run?.includes('playwright install --with-deps chromium')));
  assert.equal(job.steps.find(s => s.uses?.startsWith('actions/checkout')).with['persist-credentials'], false);
  const pkg = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'));
  assert.equal(pkg.engines.node, `>=${job.steps.find(s => s.uses?.startsWith('actions/setup-node')).with['node-version']}`);
  const report = job.steps.find(s => s.name === 'Validate and print credential-free JSON report');
  assert.match(report.run, /assert\.doesNotMatch/u); assert.match(report.run, /console\.log\(json\)/u);
  assert.doesNotMatch(text, /secrets\.|upload-artifact|cache@|schedule:|workflow_run:|repository_dispatch:/u);
});

test('manual workflow explicitly consents to up to six requests, scoped key and no schedule/monthly dispatch cap', () => {
  const job = discoveryWorkflow.jobs.discover; const inputs = discoveryWorkflow.on.workflow_dispatch.inputs;
  assert.deepEqual(Object.keys(discoveryWorkflow.on), ['workflow_dispatch']);
  assert.deepEqual(discoveryWorkflow.permissions, { contents: 'read', actions: 'read' });
  assert.equal(discoveryWorkflow.concurrency.group, 'socialapis-job-discovery-state');
  assert.equal(discoveryWorkflow.concurrency['cancel-in-progress'], false);
  assert.equal(job['runs-on'], 'ubuntu-latest'); assert.ok(job['timeout-minutes'] <= 10);
  assert.ok(job.if.includes("github.ref == 'refs/heads/main'"));
  for (const name of ['confirm_live','confirm_free_no_cash','confirm_artifact_data']) {
    assert.equal(inputs[name].default, false); assert.ok(job.if.includes(`inputs.${name}`));
  }
  assert.equal(inputs.max_requests.default, '1'); assert.equal(inputs.max_credits.default, '1'); assert.equal(inputs.policy.required, true);
  assert.equal(inputs.query.default, undefined); assert.equal(inputs.query.required, true);
  assert.ok(Object.keys(inputs).length <= 25);
  assert.equal(job.steps.find(s => s.uses?.startsWith('actions/setup-node')).with['node-version'], '22.12.0');
  assert.doesNotMatch(workflowText + actionText, /MAX_31_DAY_REQUESTS|ROLLING_REQUEST_CAP|vars\.|schedule:|curl |wget |continue-on-error|retry:/u);
  assert.equal(job.env?.SOCIALAPIS_API_KEY, undefined); assert.equal(discoveryWorkflow.env?.SOCIALAPIS_API_KEY, undefined);
  assert.equal(job.steps.filter(s => s.env?.SOCIALAPIS_API_KEY).length, 0);
  const calls = job.steps.filter(s => s.uses === './.github/actions/socialapis-discovery-page');
  assert.deepEqual(calls.map(s => s.with.slot), ['1','2','3','4','5','6']);
  assert.ok(calls.every(s => s.with.api_key === '${{ secrets.SOCIALAPIS_API_KEY }}'));
});

test('real composite action persists claim before HTTP, result before ack and exposes secret only to HTTP', () => {
  const steps = action.runs.steps;
  assert.equal(action.runs.using, 'composite');
  assert.deepEqual(steps.map(s => s.id ?? s.name), ['claim','reservation','One HTTP attempt, never retry','result','Confirm persistence or fail closed']);
  assert.equal(steps[0].run, 'node src/cli.mjs --orchestrate claim');
  assert.equal(steps[2].run, 'node src/cli.mjs --orchestrate page');
  assert.equal(steps[4].run, 'node src/cli.mjs --orchestrate ack');
  assert.equal(steps[2].env.ARTIFACT_RECEIPT, '${{ steps.reservation.outputs.artifact-id }}');
  assert.equal(steps[4].env.ARTIFACT_RECEIPT, '${{ steps.result.outputs.artifact-id }}');
  assert.equal(steps.filter(s => s.env?.SOCIALAPIS_API_KEY).length, 1);
  assert.equal(steps[2].env.SOCIALAPIS_API_KEY, '${{ inputs.api_key }}');
  for (const s of steps.slice(1)) assert.match(s.if, /steps.claim.outputs.active == 'true'/u);
  for (const s of [steps[1], steps[3]]) {
    assert.equal(s.uses, 'actions/upload-artifact@v4'); assert.match(s.with.name, /inputs.slot/u);
    assert.equal(s.with['if-no-files-found'], 'error'); assert.equal(s.with['include-hidden-files'], true); assert.equal(s.with['retention-days'], 90);
  }
  const init = discoveryWorkflow.jobs.discover.steps.findIndex(s => s.run === 'node src/cli.mjs --orchestrate init');
  const firstPage = discoveryWorkflow.jobs.discover.steps.findIndex(s => s.uses === './.github/actions/socialapis-discovery-page');
  assert.ok(init < firstPage); assert.equal(discoveryWorkflow.jobs.discover.steps[init+1].uses, 'actions/upload-artifact@v4');
  assert.equal(discoveryWorkflow.jobs.discover.steps[init].env.DISCOVERY_POLICY, '${{ inputs.policy }}');
  assert.doesNotMatch(discoveryWorkflow.jobs.discover.steps[init].run, /\$\{\{/u);
});

const checkpoint = discoveryWorkflow.jobs.discover.steps.find(s => s.id === 'checkpoint');
async function checkpointTest({ prior = true, initialize = false, count = 2, attempt = '1', privateRepo = false,
  status = 'completed', artifacts = [{ id: 11, name: 'socialapis-discovery-state' }], artifactCount = artifacts.length } = {}) {
  const saved = { GITHUB_RUN_ATTEMPT: process.env.GITHUB_RUN_ATTEMPT, INITIALIZE_STATE: process.env.INITIALIZE_STATE };
  const outputs = {}; process.env.GITHUB_RUN_ATTEMPT = attempt; process.env.INITIALIZE_STATE = String(initialize);
  try {
    const github = { rest: { repos: { get: async () => ({ data: { private: privateRepo } }) }, actions: {
      listWorkflowRuns: async options => {
        assert.equal(options.per_page, 2); assert.equal(options.branch, 'main'); assert.equal(options.created, undefined);
        return { data: { total_count: count, workflow_runs: [{ id: 200 }, ...(prior ? [{ id: 100, status }] : [])] } };
      },
      listWorkflowRunArtifacts: async options => { assert.equal(options.run_id, 100); return { data: { artifacts, total_count: artifactCount } }; }
    } } };
    const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
    await new AsyncFunction('github','context','core',checkpoint.with.script)(github, { repo: { owner:'fixture',repo:'fixture' },runId:200 },
      { setOutput: (key,value) => { outputs[key] = value; } });
    return outputs;
  } finally { for (const [key,value] of Object.entries(saved)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; } }
}

test('checkpoint accepts historical failed-run checkpoint but never resets, reruns, skips unavailable history', async () => {
  assert.equal(checkpoint.with.retries, 0);
  assert.equal((await checkpointTest()).artifact_name, 'socialapis-discovery-state');
  assert.equal((await checkpointTest({ prior:false,count:1,initialize:true })).initialize, 'true');
  assert.equal((await checkpointTest({ artifacts:[{id:12,name:'socialapis-discovery-checkpoint'}] })).artifact_name, 'socialapis-discovery-checkpoint');
  for (const [options,code] of [
    [{prior:false},'FIRST_RUN_EXPLICIT'], [{prior:false,count:100,initialize:true},'FIRST_RUN_EXPLICIT'],
    [{initialize:true},'CANNOT_RESET'], [{attempt:'2'},'RERUN_DISABLED'], [{privateRepo:true},'PUBLIC_REPOSITORY'],
    [{status:'in_progress'},'PREVIOUS_RUN_NOT_COMPLETED'], [{artifacts:[]},'CHECKPOINT_MISSING'],
    [{artifacts:[{name:'socialapis-discovery-state',expired:true}]},'CHECKPOINT_MISSING'], [{artifactCount:101},'ARTIFACT_HISTORY']
  ]) await assert.rejects(() => checkpointTest(options), new RegExp(code,'u'));
  const old = process.env.MAX_31_DAY_REQUESTS;
  try { for (const value of ['', 'bad', '0', '200']) { process.env.MAX_31_DAY_REQUESTS = value; assert.equal((await checkpointTest({count:10000})).run_id,'100'); } }
  finally { if (old === undefined) delete process.env.MAX_31_DAY_REQUESTS; else process.env.MAX_31_DAY_REQUESTS=old; }
});

test('latest page reservation wins over earlier result/legacy; expired latest never silently rolls back', async () => {
  const artifacts = [{id:1,name:'socialapis-discovery-state'}, {id:2,name:'socialapis-page-100-1-result'},
    {id:3,name:'socialapis-page-100-2-claim'}];
  assert.equal((await checkpointTest({artifacts})).artifact_id,'3');
  const result = [...artifacts,{id:4,name:'socialapis-page-100-2-result'}];
  assert.equal((await checkpointTest({artifacts:result})).artifact_id,'4');
  await assert.rejects(() => checkpointTest({artifacts:[...artifacts,{id:4,name:'socialapis-page-100-2-result',expired:true}]}), /CHECKPOINT_MISSING/u);
});

const profile = { technologies:['.NET'],locations:['Hanoi'],seniorities:['Junior'] };
const payload = { data:{ items:[{basic_info:{post_id:'12345',post_text:'Hiring Junior .NET Developer in Hanoi',url:'https://www.facebook.com/fixture/posts/12345'}}],page_info:{cursor:'private-next-token'} } };
function temp() {
  const root = resolve(directory,'.debug'); mkdirSync(root,{recursive:true});
  const local = mkdtempSync(join(root,'b2-workflow-'));
  return { local, output:join(local,'state.json'), dispose() { assert.ok(resolve(local).startsWith(root+sep)); rmSync(local,{recursive:true,force:true}); } };
}
function invoke(action, env, fake) {
  const guard = 'data:text/javascript,' + encodeURIComponent(`import net from 'node:net'; net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN');}; ${fake ?? ''}`);
  return execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate',action],{cwd:directory,encoding:'utf8',stdio:'pipe',env:{...process.env,...env}});
}

test('actual existing CLI init/claim/page/ack persists full registry, prints exposure and never leaks cursor/key', () => {
  const t = temp(); const counter = join(t.local,'calls.txt'); const outputs = join(t.local,'outputs.txt');
  const env = { DISCOVERY_STATE_FILE:t.output, INITIALIZE_STATE:'true', DISCOVERY_QUERY:'C# .NET Hà Nội',
    DISCOVERY_POLICY:JSON.stringify({profile,target:10}),DISCOVERY_MAX_REQUESTS:'2',DISCOVERY_MAX_CREDITS:'2',
    DISCOVERY_START_TIME:'2026-10-16',DISCOVERY_END_TIME:'2026-10-22',GITHUB_RUN_ID:'100',GITHUB_OUTPUT:outputs,
    SOCIALAPIS_LIVE_ENABLED:'true',SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',
    SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT:'true',SOCIALAPIS_API_KEY:'synthetic-cli-pagination-key',ARTIFACT_RECEIPT:'123' };
  const fake = `import {appendFileSync} from 'node:fs'; import assert from 'node:assert/strict'; globalThis.fetch=async(input,options)=>{
    appendFileSync(${JSON.stringify(counter)},'x'); const url=new URL(input); assert.equal(url.searchParams.get('query'),'C# .NET Hà Nội');
    assert.equal(url.searchParams.get('start_time'),'2026-10-16'); assert.equal(options.redirect,'manual');
    return new Response(${JSON.stringify(JSON.stringify(payload))},{headers:{'X-SocialAPIs-Credits-Deducted':'1'}}); };`;
  try {
    const init = invoke('init',env); assert.match(init, /"maximumHttpRequests":2/u); assert.match(init, /"maximumContractCreditExposure":2/u);
    const before = readFileSync(t.output,'utf8');
    assert.throws(() => invoke('init',{...env,DISCOVERY_START_TIME:'2026-02-30'}), e => e.stderr.toString().includes('INVALID_DATE_FILTER'));
    assert.equal(readFileSync(t.output,'utf8'),before);
    invoke('claim',env); assert.match(readFileSync(outputs,'utf8'), /active=true/u);
    const claimed = readFileSync(t.output,'utf8');
    assert.throws(() => execFileSync(process.execPath,['src/cli.mjs','--source','socialapis','--prepare-state','--output',t.output],{
      cwd:directory,encoding:'utf8',stdio:'pipe'}), e => e.stderr.toString().includes('ORCHESTRATED_STATE_REQUIRES_ORCHESTRATION'));
    assert.equal(readFileSync(t.output,'utf8'),claimed);
    assert.throws(() => invoke('page',{...env,ARTIFACT_RECEIPT:''},fake), e => e.stderr.toString().includes('DURABLE_CLAIM'));
    const stdout = invoke('page',env,fake); const completed = JSON.parse(readFileSync(t.output,'utf8'));
    assert.equal(completed.jobs.length,1); assert.equal(completed.orchestration.phase,'COMPLETED_UNCONFIRMED');
    assert.equal(completed.jobs[0].publishedAt,null); assert.equal(completed.workflowMetrics.actualRequests,1);
    assert.doesNotMatch(stdout + JSON.stringify(completed), /synthetic-cli-pagination-key|private-next-token/u);
    assert.throws(() => invoke('claim',env), e => e.stderr.toString().includes('PERSISTENCE_CONFIRMATION_REQUIRED'));
    invoke('ack',env); assert.equal(readFileSync(counter,'utf8'),'x');
    // A new process can resume only after durable acknowledgment, with page 2.
    invoke('init',{...env,GITHUB_RUN_ID:'101',INITIALIZE_STATE:'false'});
    assert.equal(JSON.parse(readFileSync(t.output,'utf8')).orchestration.plan.pageNumber,2);
  } finally { t.dispose(); }
});

test('legacy single-request CLI still preserves historical jobs on insufficient or unknown credits', () => {
  const t=temp();
  const historical=[{provider:'SOCIALAPIS',id:'98765',sourceIdentity:'SOCIALAPIS:98765',body:'SYNTHETIC_HISTORY',contentHash:'a'.repeat(64),publishedAt:null}];
  try {
    for (const [status,headers,body,code] of [[402,{}, {},'PROVIDER_HTTP_REJECTED'],
      [200,{'X-SocialAPIs-Credits-Deducted':'0'},{error:{code:'INSUFFICIENT_CREDITS'}},'PROVIDER_ERROR'],
      [200,{'X-SocialAPIs-Credits-Used':'6'},payload,'PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED']]) {
      writeFileSync(t.output,JSON.stringify({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:historical}));
      const fake='data:text/javascript,'+encodeURIComponent(`import net from 'node:net'; net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN');}; let calls=0;
        globalThis.fetch=async()=>{if(++calls>1) throw Error('UNEXPECTED_RETRY'); return new Response(${JSON.stringify(JSON.stringify(body))},{status:${status},headers:${JSON.stringify(headers)}});};`);
      assert.throws(() => execFileSync(process.execPath,['--import',fake,'src/cli.mjs','--source','socialapis','--live','--output',t.output],{
        cwd:directory,encoding:'utf8',stdio:'pipe',env:{...process.env,SOCIALAPIS_API_KEY:'synthetic-development-key',SOCIALAPIS_LIVE_ENABLED:'true',
          SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true'}}), e => {
        assert.match(e.stderr.toString(),new RegExp(code,'u')); assert.equal(JSON.parse(e.stdout.toString()).providerUsage.requestCount,1); return true;
      });
      assert.deepEqual(JSON.parse(readFileSync(t.output,'utf8')).jobs,historical);
    }
  } finally { t.dispose(); }
});
