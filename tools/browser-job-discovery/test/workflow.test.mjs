import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { mkdirSync, mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { parse } from 'yaml';
import {historyFixture,run,artifact} from './fixtures/checkpoint-history.mjs';

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

test('V1 manual workflow has exactly one provider slot, explicit consent and a one-credit ceiling', () => {
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
  assert.equal(inputs.resume_continuation.default, false);
  assert.equal(inputs.reconcile_terminal_failure.default,false);
  assert.equal(inputs.query.default, undefined); assert.equal(inputs.query.required, false);
  assert.ok(Object.keys(inputs).length <= 25);
  assert.equal(job.steps.find(s => s.uses?.startsWith('actions/setup-node')).with['node-version'], '22.12.0');
  assert.doesNotMatch(workflowText + actionText, /ROLLING_REQUEST_CAP|schedule:|curl |wget |continue-on-error|retry:/u);
  const freshInit=job.steps.find(s=>s.run==='node src/cli.mjs --orchestrate fresh-init');
  assert.ok(freshInit.if.includes("inputs.fresh_session_id != ''"));
  const legacyInit=job.steps.find(s=>s.run==='node src/cli.mjs --orchestrate init');
  for(const step of [freshInit,legacyInit]){
    assert.doesNotMatch(JSON.stringify(step.env),/FREE_BALANCE_EVIDENCE|MAX_31_DAY/u);
  }
  assert.doesNotMatch(workflowText,/FREE_BALANCE_EVIDENCE|MAX_31_DAY|verifiedAt|ledgerFingerprint/u);
  assert.equal(job.env?.SOCIALAPIS_API_KEY, undefined); assert.equal(discoveryWorkflow.env?.SOCIALAPIS_API_KEY, undefined);
  assert.equal(job.steps.filter(s => s.env?.SOCIALAPIS_API_KEY).length, 0);
  const calls = job.steps.filter(s => s.uses === './.github/actions/socialapis-discovery-page');
  assert.deepEqual(calls.map(s => s.with.slot), ['1']);
  assert.equal(job.env.DISCOVERY_ONE_REQUEST_V1,'true');
  assert.equal(job.steps.find(s=>s.run==='npm test').env.DISCOVERY_ONE_REQUEST_V1,'false');
  assert.deepEqual(inputs.max_requests.options,['1']);assert.deepEqual(inputs.max_credits.options,['1']);
  assert.ok(calls.every(s => s.with.api_key === '${{ secrets.SOCIALAPIS_API_KEY }}'));
});

test('real composite action persists claim before HTTP, result before ack and exposes secret only to HTTP', () => {
  const steps = action.runs.steps.filter(s=>!['Encrypt claim for automatic sync','Encrypt completed response observations before synchronization','Automatically import saved observations, never refetch provider'].includes(s.name));
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
  assert.ok(init < firstPage); assert.ok(discoveryWorkflow.jobs.discover.steps.slice(init+1,firstPage).some(s=>s.uses==='actions/upload-artifact@v4'));
  assert.equal(discoveryWorkflow.jobs.discover.steps[init].env.DISCOVERY_POLICY, '${{ inputs.policy }}');
  assert.equal(discoveryWorkflow.jobs.discover.steps[init].env.RESUME_CONTINUATION, '${{ inputs.resume_continuation }}');
  assert.doesNotMatch(discoveryWorkflow.jobs.discover.steps[init].run, /\$\{\{/u);
});
test('automatic sync demo validates readiness before HTTP, persists ciphertext before import, and exposes no personal Admin token',()=>{
  const job=discoveryWorkflow.jobs.discover,steps=job.steps;
  assert.equal(discoveryWorkflow.on.workflow_dispatch.inputs.auto_admin_sync.default,true);
  assert.equal(discoveryWorkflow.on.workflow_dispatch.inputs.sync_only.default,false);
  const preflight=steps.findIndex(s=>s.run==='node src/cli.mjs --orchestrate sync-ready');
  const first=steps.findIndex(s=>s.uses==='./.github/actions/socialapis-discovery-page');
  assert.ok(preflight<first);assert.equal(steps[preflight].env.SOCIALAPIS_API_KEY,undefined);
  const calls=steps.filter(s=>s.uses==='./.github/actions/socialapis-discovery-page');
  assert.equal(calls[0].if,'!inputs.sync_only');
  assert.ok(calls.slice(1).every(s=>s.if==='!inputs.sync_only && !inputs.auto_admin_sync'));
  const parts=action.runs.steps;
  const result=parts.findIndex(s=>s.id==='result'),sync=parts.findIndex(s=>s.run==='node src/cli.mjs --orchestrate sync');
  assert.ok(result<sync);assert.match(parts[sync].if,/steps.result.outputs.artifact-id/u);
  assert.equal(parts[sync].env.SOCIALAPIS_API_KEY,undefined);
  assert.equal(parts[sync].env.DISCOVERY_IMPORT_KEY,'${{ inputs.import_key }}');
  for(const step of [...steps,...parts].filter(s=>s.uses==='actions/upload-artifact@v4')){
    assert.match(step.with.path,/\.enc/u);assert.doesNotMatch(step.with.path,/\*|review-final|response\.json/u);
  }
  const replay=steps.find(s=>s.name==='Retry only saved Admin synchronization');
  assert.equal(replay.if,'inputs.auto_admin_sync && inputs.sync_only');assert.equal(replay.env.SOCIALAPIS_API_KEY,undefined);
  assert.equal(replay.env.DISCOVERY_SYNC_ONLY,'true');assert.equal(replay.env.DISCOVERY_SYNC_WORKFLOW_RUN_ID,'${{ inputs.sync_workflow_run_id }}');
  assert.equal(parts[sync].env.DISCOVERY_SYNC_ONLY,'false');assert.equal(parts[sync].env.DISCOVERY_SYNC_WORKFLOW_RUN_ID,'');
  assert.doesNotMatch(workflowText+actionText,/ADMIN_TOKEN|ADMIN_PASSWORD|schedule:/u);
  assert.equal(parts.find(s=>s.run==='node src/cli.mjs --orchestrate page').env.SOCIALAPIS_PRIVATE_CHECKPOINT_CONFIRMED,'${{ env.DISCOVERY_AUTO_ADMIN_SYNC }}');
});

const checkpoint = discoveryWorkflow.jobs.discover.steps.find(s => s.id === 'checkpoint');
async function checkpointTest({ prior = true, initialize = false, count = 2, attempt = '1', privateRepo = false,
  status = 'completed', artifacts = [{ id: 11, name: 'socialapis-discovery-state' }], artifactCount = artifacts.length,
  preHttpFailure = false } = {}) {
  const saved = Object.fromEntries(['GITHUB_RUN_ATTEMPT','INITIALIZE_STATE','GITHUB_WORKSPACE','GITHUB_RUN_NUMBER'].map(k=>[k,process.env[k]]));
  const outputs = {}; process.env.GITHUB_RUN_ATTEMPT = attempt; process.env.INITIALIZE_STATE = String(initialize);
  process.env.GITHUB_WORKSPACE=resolve(directory,'../..');process.env.GITHUB_RUN_NUMBER='200';
  try {
    const runs=[run(200),...(prior?[run(100,{status})]:[]),...(preHttpFailure?[run(99)]:[])];
    const slot=Math.max(0,...artifacts.map(a=>Number(a.name?.match(/-([1-6])-(?:claim|result)$/u)?.[1]??0)));
    const f=historyFixture({runs,runTotal:preHttpFailure?3:count,privateRepo,
      artifacts:{100:preHttpFailure?[]:artifacts.map(a=>({...artifact(a.id,a.name),...a})),99:artifacts.map(a=>({...artifact(a.id,a.name),...a}))},
      modes:{100:preHttpFailure?'PRE_HTTP_FAILURE':slot?'PROVIDER':'PRE_REQUEST_CHECKPOINT'},startedSlots:{100:Array.from({length:slot},(_,i)=>i+1)}});
    const {github}=f;
    if(artifactCount!==artifacts.length){const original=github.rest.actions.listWorkflowRunArtifacts;github.rest.actions.listWorkflowRunArtifacts=async params=>{
      const response=await original(params);response.data.total_count=artifactCount;return response;
    };}
    const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
    await new AsyncFunction('github','context','core',checkpoint.with.script)(github, { repo: { owner:'fixture',repo:'fixture' },runId:200 },
      { setOutput: (key,value) => { outputs[key] = value; } });
    return outputs;
  } finally { for (const [key,value] of Object.entries(saved)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; } }
}

test('checkpoint traversal does not let a proven offline-test failure hide an older durable checkpoint',async()=>{
  assert.equal((await checkpointTest({preHttpFailure:true})).run_id,'99');
});

test('checkpoint accepts historical failed-run checkpoint but never resets, reruns, skips unavailable history', async () => {
  assert.equal(checkpoint.with.retries, 0);
  assert.equal((await checkpointTest()).artifact_name, 'socialapis-discovery-state');
  assert.equal((await checkpointTest({ prior:false,count:1,initialize:true })).initialize, 'true');
  assert.equal((await checkpointTest({ artifacts:[{id:12,name:'socialapis-discovery-checkpoint'}] })).artifact_name, 'socialapis-discovery-checkpoint');
  for (const [options,code] of [
    [{prior:false},'HISTORY_CHANGED_OR_INCOMPLETE'], [{prior:false,count:100,initialize:true},'HISTORY_CHANGED_OR_INCOMPLETE'],
    [{initialize:true},'CANNOT_RESET'], [{attempt:'2'},'RERUN_DISABLED'], [{privateRepo:true},'PUBLIC_REPOSITORY'],
    [{status:'in_progress'},'PREVIOUS_RUN_NOT_COMPLETED'], [{artifacts:[]},'NO_ARTIFACT_HTTP_UNPROVEN'],
    [{artifacts:[{id:11,name:'socialapis-discovery-state',expired:true}]},'CHECKPOINT_MISSING'], [{artifactCount:101},'METADATA_INCOMPLETE']
  ]) await assert.rejects(() => checkpointTest(options), new RegExp(code,'u'));
  const old = process.env.MAX_31_DAY_REQUESTS;
  try { for (const value of ['', 'bad', '0', '200']) { process.env.MAX_31_DAY_REQUESTS = value; assert.equal((await checkpointTest({count:10000})).run_id,'100'); } }
  finally { if (old === undefined) delete process.env.MAX_31_DAY_REQUESTS; else process.env.MAX_31_DAY_REQUESTS=old; }
});

test('latest page reservation wins over earlier result/legacy; expired latest never silently rolls back', async () => {
  const artifacts = [{id:1,name:'socialapis-discovery-state'}, {id:10,name:'socialapis-page-100-1-claim'}, {id:2,name:'socialapis-page-100-1-result'},
    {id:3,name:'socialapis-page-100-2-claim'}];
  assert.equal((await checkpointTest({artifacts})).artifact_id,'3');
  const result = [...artifacts,{id:4,name:'socialapis-page-100-2-result'}];
  assert.equal((await checkpointTest({artifacts:result})).artifact_id,'4');
  assert.equal((await checkpointTest({artifacts:result})).claim_artifact_id,'3');
  await assert.rejects(() => checkpointTest({artifacts:[...artifacts,{id:4,name:'socialapis-page-100-2-result',expired:true}]}), /CHECKPOINT_MISSING/u);
});

test('reconciliation provenance is wired to uploaded result and matching claim, never a browser-supplied identity flag',()=>{
  const init=discoveryWorkflow.jobs.discover.steps.find(s=>s.run==='node src/cli.mjs --orchestrate init');
  assert.equal(init.env.ARTIFACT_NAME,'${{ steps.checkpoint.outputs.artifact_name }}');
  assert.equal(init.env.ARTIFACT_RUN_ID,'${{ steps.checkpoint.outputs.run_id }}');
  assert.equal(init.env.CLAIM_ARTIFACT_RECEIPT,'${{ steps.checkpoint.outputs.claim_artifact_id }}');
  assert.equal(init.env.RECONCILE_TERMINAL_FAILURE,'${{ inputs.reconcile_terminal_failure }}');
  const ack=action.runs.steps.at(-1);
  assert.equal(ack.env.ARTIFACT_NAME,'socialapis-page-${{ github.run_id }}-${{ inputs.slot }}-result');
  assert.equal(ack.env.CLAIM_ARTIFACT_RECEIPT,'${{ steps.reservation.outputs.artifact-id }}');
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

test('persisted failed or unknown-cost page cannot be executed twice with the same claim across CLI processes',()=>{
  for(const response of ["throw Error('synthetic network failure');",
    "return Response.json({error:{code:'INSUFFICIENT_CREDITS'}},{status:402,headers:{'X-SocialAPIs-Credits-Deducted':'0'}});",
    `return Response.json(${JSON.stringify(payload)});`]){
    const t=temp(),counter=join(t.local,'provider-calls.txt');
    const env={DISCOVERY_STATE_FILE:t.output,INITIALIZE_STATE:'true',GITHUB_RUN_ID:'88001',DISCOVERY_QUERY:'Backend Hanoi',
      DISCOVERY_POLICY:JSON.stringify({profile}),DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1',ARTIFACT_RECEIPT:'123',
      SOCIALAPIS_API_KEY:'synthetic-single-claim-key',SOCIALAPIS_LIVE_ENABLED:'true',SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',
      SOCIALAPIS_NO_CASH_CONFIRMED:'true',SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT:'true'};
    const fake=`import{appendFileSync}from'node:fs';globalThis.fetch=async()=>{appendFileSync(${JSON.stringify(counter)},'x');${response}};`;
    try{
      invoke('init',env);invoke('claim',env);invoke('page',env,fake);
      const before=readFileSync(t.output,'utf8');assert.equal(JSON.parse(before).requestLedger[0].actualRequests,1);
      assert.throws(()=>invoke('page',env,fake),error=>error.stderr.toString().includes('DURABLE_CLAIM_ALREADY_USED_OR_AMBIGUOUS'));
      assert.equal(readFileSync(counter,'utf8'),'x');assert.equal(readFileSync(t.output,'utf8'),before);
      assert.throws(()=>invoke('claim',env),error=>error.stderr.toString().includes('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH'));
    }finally{t.dispose();}
  }
});

test('CLI honors explicit dates, falls back to policy when input blank, and reports genuinely unset dates without today defaults',()=>{
  for(const scenario of [
    {policy:{startTime:'2026-10-01',endTime:'2026-10-20'},input:{DISCOVERY_START_TIME:'2026-10-02',DISCOVERY_END_TIME:'2026-10-21'},start:'2026-10-02',end:'2026-10-21',source:'ENV_INPUT'},
    {policy:{startTime:'2026-10-01',endTime:'2026-10-20'},input:{DISCOVERY_START_TIME:'',DISCOVERY_END_TIME:''},start:'2026-10-01',end:'2026-10-20',source:'POLICY'},
    {policy:{},input:{DISCOVERY_START_TIME:'',DISCOVERY_END_TIME:''},start:null,end:null,source:'UNSET'}
  ]) {
    const t=temp();
    try {
      const stdout=invoke('init',{DISCOVERY_STATE_FILE:t.output,INITIALIZE_STATE:'true',GITHUB_RUN_ID:'600',
        DISCOVERY_QUERY:'Backend Fresher Hanoi',DISCOVERY_POLICY:JSON.stringify({profile,...scenario.policy}),...scenario.input});
      const state=JSON.parse(readFileSync(t.output,'utf8')),audit=state.orchestration.dateFilterAudit;
      assert.equal(state.orchestration.config.startTime,scenario.start);assert.equal(state.orchestration.config.endTime,scenario.end);
      assert.equal(audit.startSource,scenario.source);assert.equal(audit.endSource,scenario.source);assert.equal(audit.providerFreshnessVerified,false);
      if(scenario.source!=='ENV_INPUT')assert.equal(audit.startInputState,'BLANK');
      assert.match(stdout,/"dateFilters"/u);assert.equal(state.requestLedger.length,0);
    } finally {t.dispose();}
  }
});

test('actual CLI reconciles only an exclusive private copy of a hash-verified observed result with no network or history loss',()=>{
  const t=temp(),privateDir=mkdtempSync(join(tmpdir(),'socialapis-b42-recovery-'));
  const env={DISCOVERY_STATE_FILE:t.output,INITIALIZE_STATE:'true',DISCOVERY_QUERY:'Backend Fresher Hanoi',
    DISCOVERY_POLICY:JSON.stringify({profile}),GITHUB_RUN_ID:'601',ARTIFACT_RECEIPT:'123',SOCIALAPIS_API_KEY:'synthetic-recovery-key',
    SOCIALAPIS_LIVE_ENABLED:'true',SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT:'true'};
  const invalid={data:{items:Array.from({length:5},(_,index)=>({basic_info:{post_id:String(7000+index),post_text:'Synthetic recruitment',url:`https://www.facebook.com/fixture/posts/pfbidUnknown${index}`}})),page_info:{}}};
  const fake=`globalThis.fetch=async()=>new Response(${JSON.stringify(JSON.stringify(invalid))},{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});`;
  try {
    invoke('init',env);invoke('claim',env);invoke('page',env,fake);
    const bytes=readFileSync(t.output),s=JSON.parse(bytes),attempt=s.requestLedger.at(-1);
    const source=join(privateDir,'original.json'),metadata=join(privateDir,'observed.json'),output=join(privateDir,'working-copy.json');
    writeFileSync(source,bytes);
    const observation={metadataKind:'OBSERVED_FROM_PERSISTED_RESULT_NOT_DISPATCH_INPUTS',workflowRunId:'601',resultArtifactId:456,claimArtifactId:123,
      stateJsonSHA256:createHash('sha256').update(bytes).digest('hex'),resultUploaded:true,
      observedRun:{phase:s.orchestration.phase,status:'STOPPED_TERMINAL',stopReason:'NO_VALID_PROVIDER_ITEMS',logicalExecutionId:s.orchestration.logicalExecutionId,attemptIndex:0},
      effectiveValuesFromPersistedState:{actualRequests:1,creditsDeducted:1,outcome:attempt.outcome},itemStats:attempt.itemStats};
    writeFileSync(metadata,JSON.stringify(observation));
    const recovery={DISCOVERY_RECONCILE_SOURCE:source,DISCOVERY_RECONCILE_METADATA:metadata,DISCOVERY_RECONCILE_OUTPUT:output};
    assert.match(invoke('reconcile',recovery),/"providerRequestsMade":0/u);
    const copy=JSON.parse(readFileSync(output));assert.equal(copy.orchestration.phase,'TERMINAL_RECONCILED');
    assert.deepEqual(copy.requestLedger,s.requestLedger);assert.deepEqual(copy.jobs,s.jobs);assert.deepEqual(readFileSync(source),bytes);
    assert.throws(()=>invoke('reconcile',recovery)); // Never overwrite an existing copy.
    writeFileSync(metadata,JSON.stringify({...observation,stateJsonSHA256:'0'.repeat(64)}));
    assert.throws(()=>invoke('reconcile',{...recovery,DISCOVERY_RECONCILE_OUTPUT:join(privateDir,'bad-copy.json')}),e=>e.stderr.toString().includes('RECONCILIATION_EVIDENCE_INVALID'));
    assert.throws(()=>invoke('reconcile',{...recovery,DISCOVERY_RECONCILE_OUTPUT:source}),e=>e.stderr.toString().includes('PRIVATE_RECOVERY_COPY_REQUIRED'));
    const init={...env,INITIALIZE_STATE:'false',GITHUB_RUN_ID:'602',RECONCILE_TERMINAL_FAILURE:'true',ARTIFACT_RECEIPT:'456',
      ARTIFACT_NAME:'socialapis-page-601-1-result',ARTIFACT_RUN_ID:'601',CLAIM_ARTIFACT_RECEIPT:'123'};
    invoke('init',init);const next=JSON.parse(readFileSync(t.output));
    assert.equal(next.orchestration.phase,'READY');assert.deepEqual(next.requestLedger,s.requestLedger);assert.equal(next.terminalReconciliations.length,1);
  } finally {t.dispose();rmSync(privateDir,{recursive:true,force:true});}
});

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
    assert.throws(() => invoke('init',{...env,GITHUB_RUN_ID:'101',INITIALIZE_STATE:'false'}), e => e.stderr.toString().includes('RESUME_CONTINUATION_REQUIRED'));
    // Use deterministic clock in the subprocess: synthetic cursor timestamp
    // follows the real test clock and is not a fixed production fixture date.
    invoke('init',{...env,GITHUB_RUN_ID:'101',INITIALIZE_STATE:'false',RESUME_CONTINUATION:'true'});
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

test('actual CLI resumes a budget-stopped page-2 checkpoint at page 3 with new per-workflow limits', () => {
  const t=temp(); const counter=join(t.local,'requests.jsonl'); const outputs=join(t.local,'outputs.txt');
  const env={DISCOVERY_STATE_FILE:t.output,INITIALIZE_STATE:'true',DISCOVERY_QUERY:'synthetic resume query',
    DISCOVERY_POLICY:JSON.stringify({profile,target:20}),DISCOVERY_MAX_REQUESTS:'2',DISCOVERY_MAX_CREDITS:'2',
    GITHUB_RUN_ID:'200',GITHUB_OUTPUT:outputs,ARTIFACT_RECEIPT:'123',SOCIALAPIS_API_KEY:'synthetic-resume-key-only',
    SOCIALAPIS_LIVE_ENABLED:'true',SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT:'true'};
  const fake=`import {appendFileSync} from 'node:fs'; import assert from 'node:assert/strict'; globalThis.fetch=async input=>{
    const cursor=new URL(input).searchParams.get('end_cursor'); assert.ok([null,'next-2','next-3'].includes(cursor));
    appendFileSync(${JSON.stringify(counter)},JSON.stringify(cursor)+'\\n');
    const index=cursor===null?1:cursor==='next-2'?2:3; const id=String(5100+index);
    return new Response(JSON.stringify({data:{items:[{basic_info:{post_id:id,post_text:'Hiring Junior .NET Developer in Hanoi',url:'https://facebook.com/fixture/posts/'+id}}],page_info:{cursor:'next-'+(index+1)}}}),{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});
  };`;
  const step = values => { invoke('claim',values);invoke('page',values,fake);invoke('ack',values); };
  try {
    invoke('init',env); step(env); step(env);
    const prior=JSON.parse(readFileSync(t.output,'utf8')); assert.equal(prior.orchestration.status,'STOPPED_RESUMABLE');
    const resumed={...env,INITIALIZE_STATE:'false',RESUME_CONTINUATION:'true',GITHUB_RUN_ID:'201',DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1'};
    invoke('init',resumed); step(resumed);
    const after=JSON.parse(readFileSync(t.output,'utf8'));
    assert.deepEqual(readFileSync(counter,'utf8').trim().split('\n').map(JSON.parse),[null,'next-2','next-3']);
    assert.deepEqual(after.orchestration.pages.map(p=>p.pageNumber),[1,2,3]);
    assert.equal(after.workflowMetrics.actualRequests,1); assert.equal(after.workflowMetrics.logicalExecution.actualRequests,3);
    assert.equal(after.workflowMetrics.logicalExecution.creditsDeducted,3);
    assert.equal(after.orchestration.logicalExecutionId,prior.orchestration.logicalExecutionId);
    assert.deepEqual(after.jobs.slice(0,2),prior.jobs); assert.equal(after.requestLedger.length,3);
    const before=readFileSync(t.output,'utf8');
    assert.throws(()=>invoke('init',{...resumed,GITHUB_RUN_ID:'202',DISCOVERY_QUERY:'different query'}),e=>e.stderr.toString().includes('CONTINUATION_POLICY_MISMATCH'));
    assert.equal(readFileSync(t.output,'utf8'),before);
  } finally { t.dispose(); }
});

test('existing CLI plans from a controlled Portfolio snapshot and uses that profile in actual extraction/matching',()=>{
  const t=temp(),uid=n=>`00000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
  const policy={planQueries:true,target:1,profile:{locations:['Hanoi'],seniorities:['Junior']},portfolioSnapshot:{schemaVersion:1,source:'PORTFOLIO_PUBLIC_API_EXPORT',
    technologies:[{id:uid(1),name:'CSharp',category:'Backend'}],skills:[{id:uid(2),name:'C#',technologyId:uid(1),experienceLevel:'USED'}],experiences:[],projects:[],privateExtra:'DO_NOT_PERSIST'}};
  const env={DISCOVERY_STATE_FILE:t.output,INITIALIZE_STATE:'true',GITHUB_RUN_ID:'300',DISCOVERY_QUERY:'',DISCOVERY_POLICY:JSON.stringify(policy),
    GITHUB_OUTPUT:join(t.local,'outputs.txt'),ARTIFACT_RECEIPT:'123',SOCIALAPIS_API_KEY:'synthetic-profile-planner-key',SOCIALAPIS_LIVE_ENABLED:'true',
    SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT:'true'};
  try{
    invoke('init',env);const s=JSON.parse(readFileSync(t.output,'utf8'));assert.match(s.orchestration.config.query,/C#/u);
    assert.ok(s.queryPlan.candidates.length);assert.doesNotMatch(JSON.stringify(s),/DO_NOT_PERSIST|CSharp.*privateExtra/u);
    const wire={data:{items:[{basic_info:{post_id:'6101',post_text:'Hiring Junior C# Developer in Hanoi',url:'https://facebook.com/fixture/posts/6101'}}]}};
    const fake=`import assert from 'node:assert/strict';globalThis.fetch=async input=>{assert.equal(new URL(input).searchParams.get('query'),${JSON.stringify(s.orchestration.config.query)});return new Response(${JSON.stringify(JSON.stringify(wire))},{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});};`;
    invoke('claim',env);invoke('page',env,fake);invoke('ack',env);
    const after=JSON.parse(readFileSync(t.output,'utf8'));assert.equal(after.orchestration.stopReason,'SUFFICIENT_NEW_VACANCIES');
    assert.equal(after.jobs[0].match.classification,'TARGET_MATCH');assert.equal(after.workflowMetrics.actualRequests,1);
    assert.equal(after.orchestration.pages[0].profileVersion,'portfolio-technology-v1');
  }finally{t.dispose();}
});
