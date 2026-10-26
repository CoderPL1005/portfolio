import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {readFileSync,mkdtempSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {join,resolve} from 'node:path';
import {execFileSync} from 'node:child_process';
import {locateCheckpoint,HISTORY_LIMITS,REVIEWED_WORKFLOW_HASHES,workflowHash} from '../src/checkpoint-history.mjs';
import {beginExecution,claimPage,reconcileTerminalFailure} from '../src/pagination-orchestrator.mjs';
import {encryptCheckpoint} from '../src/admin-sync.mjs';
import {historyFixture,run,artifact,jobsFor,source} from './fixtures/checkpoint-history.mjs';

const profile={technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']};
const config={query:'Synthetic backend query',profile,maxRequests:1,maxCredits:1};
const pair=(id=100)=>[artifact(123,`socialapis-page-${id}-1-claim`),artifact(456,`socialapis-page-${id}-1-result`)];
const locate=fixture=>locateCheckpoint({...fixture,attempt:'1'});
function terminalState(){
  const s=beginExecution({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[]},config,'100');
  claimPage(s);s.jobs=Array.from({length:24},(_,i)=>({provider:'SOCIALAPIS',id:String(8000+i),sourceIdentity:`SOCIALAPIS:${8000+i}`,vacancies:[]}));
  s.contentRegistry={syntheticHistoricalIdentity:'preserved'};
  const reasons=Array.from({length:5},(_,index)=>({index,reason:'POST_ID_URL_MISMATCH'}));
  Object.assign(s.requestLedger[0],{actualRequests:1,creditsDeducted:1,outcome:'FAILED_OR_AMBIGUOUS',
    itemStats:{totalItems:5,acceptedItems:0,rejectedItems:5,rejectionReasons:reasons,outcome:'NO_VALID_ITEMS'},
    rejectionDiagnostics:reasons.map(r=>({...r,pageNumber:1}))});
  Object.assign(s.orchestration,{phase:'CLAIMED',status:'STOPPED_TERMINAL',stopReason:'NO_VALID_PROVIDER_ITEMS'});
  return s;
}
test('current workflow source is explicitly reviewed; history helper exposes only bounded GitHub GET ports',()=>{
  assert.ok(REVIEWED_WORKFLOW_HASHES.has(workflowHash(source)));
  assert.equal(HISTORY_LIMITS.pages*HISTORY_LIMITS.perPage,HISTORY_LIMITS.runs);
  const implementation=readFileSync(new URL('../src/checkpoint-history.mjs',import.meta.url),'utf8');
  assert.doesNotMatch(implementation,/fetch\(|createWorkflowDispatch|deleteArtifact|createRef|SOCIALAPIS_API_KEY|writeFile/u);
});
test('consecutive proven pre-HTTP failures are skipped with safe audit metadata, never inferred from missing artifacts',async()=>{
  const f=historyFixture({runs:[run(200),run(199),run(198),run(100)],modes:{199:'PRE_HTTP_FAILURE',198:'PRE_HTTP_FAILURE'},artifacts:{100:pair()}});
  const selected=await locate(f);assert.equal(selected.run_id,'100');assert.equal(selected.artifact_id,'456');
  assert.deepEqual(JSON.parse(selected.skipped_runs).map(s=>s.runId),['199','198']);
  for(const c of f.calls.filter(c=>c.name==='runs')){assert.equal(c.branch,'main');assert.equal(c.event,'workflow_dispatch');}
});
test('missing artifacts plus unknown/skipped/cancelled steps never skip to older state',async()=>{
  for(const mode of ['UNPROVEN','PROVIDER']){
    const f=historyFixture({runs:[run(200),run(199),run(100)],artifacts:{100:pair()},modes:{199:mode}});
    await assert.rejects(()=>locate(f),/NO_ARTIFACT_HTTP_UNPROVEN/u);
    assert.ok(!f.calls.some(c=>c.name==='artifacts'&&c.run_id===100));
  }
});
test('metadata can prove offline failure only for reviewed source and complete matching step/job evidence',async()=>{
  const record=run(199),valid=jobsFor(record,'PRE_HTTP_FAILURE');
  const cases=[
    {sources:{[record.head_sha]:source+'\n# unknown source'}},
    {jobRows:{199:[{...valid[0],steps:valid[0].steps.slice(0,-3)}]}},
    {jobRows:{199:[{...valid[0],run_attempt:2}]}},
    {jobRows:{199:[{...valid[0],steps:valid[0].steps.map(s=>s.conclusion==='skipped'?{...s,conclusion:'cancelled'}:s)}]}},
    {jobRows:{199:[valid[0],{...valid[0],id:999,name:'another job'}]}}
  ];
  for(const options of cases){
    const f=historyFixture({runs:[run(200),record,run(100)],artifacts:{100:pair()},modes:{199:'PRE_HTTP_FAILURE'},...options});
    await assert.rejects(()=>locate(f),/CHECKPOINT_/u);assert.ok(!f.calls.some(c=>c.run_id===100));
  }
});
test('artifact itself never converts a terminal result into success: existing reconciliation validates immutable ledger/provenance',async()=>{
  const f=historyFixture({artifacts:{100:pair()},modes:{100:'PROVIDER'}}),selected=await locate(f);
  const original=terminalState(),s=structuredClone(original);
  const proof={artifactId:selected.artifact_id,claimArtifactId:selected.claim_artifact_id,workflowRunId:selected.run_id,
    artifactName:selected.artifact_name,artifactKind:'RESULT',provenance:'ACTIONS_RESULT',attemptIndex:s.orchestration.attemptIndex,
    stateFingerprint:createHash('sha256').update(JSON.stringify(s)).digest('hex')};
  assert.equal(s.orchestration.phase,'CLAIMED');reconcileTerminalFailure(s,proof);
  assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');assert.equal(s.orchestration.status,'STOPPED_TERMINAL');
  assert.equal(s.orchestration.stopReason,'NO_VALID_PROVIDER_ITEMS');
  assert.deepEqual(s.jobs,original.jobs);assert.deepEqual(s.contentRegistry,original.contentRegistry);assert.deepEqual(s.requestLedger,original.requestLedger);
  assert.equal(s.requestLedger[0].creditsDeducted,1);assert.equal(s.requestLedger[0].rejectionDiagnostics.length,5);
  for(const patch of [{artifactId:'123'},{claimArtifactId:''},{workflowRunId:'101'},{attemptIndex:1},{stateFingerprint:'0'.repeat(64)},
    {artifactName:'socialapis-page-100-2-result'},{artifactKind:'CLAIM'}]){
    const copy=structuredClone(original);assert.throws(()=>reconcileTerminalFailure(copy,{...proof,...patch}),/RECONCILIATION/u);assert.deepEqual(copy,original);
  }
});
test('latest claim without result is restored as ambiguous; recovery cannot reuse older completed response',async()=>{
  const f=historyFixture({runs:[run(200),run(199),run(100)],artifacts:{199:[artifact(124,'socialapis-page-199-1-claim')],100:pair()},modes:{199:'PROVIDER'}});
  const selected=await locate(f);assert.equal(selected.artifact_name,'socialapis-page-199-1-claim');
  assert.equal(selected.claim_artifact_id,'');assert.ok(!f.calls.some(c=>c.run_id===100));
  const s=terminalState();s.requestLedger[0].actualRequests=null;s.requestLedger[0].creditsDeducted=null;
  assert.throws(()=>beginExecution(s,config,'200'),/AMBIGUOUS_PAGE/u);
});
test('expired, incomplete, wrong-run, duplicate and later-unaccounted artifacts stop before any older checkpoint',async()=>{
  for(const [rows,slots] of [
    [[...pair(199).slice(0,1),artifact(456,'socialapis-page-199-1-result',{expired:true})],[1]],
    [[artifact(456,'socialapis-page-199-1-result')],[1]],
    [pair(198),[1]], [pair(199),[1,2]],
    [[...pair(199),artifact(789,'socialapis-page-199-1-result')],[1]],
    [[artifact(123,'socialapis-page-199-2-claim')],[1,2]],
    [[artifact(11,'socialapis-discovery-checkpoint')],[1]]
  ]){
    const f=historyFixture({runs:[run(200),run(199),run(100)],artifacts:{199:rows,100:pair()},modes:{199:'PROVIDER'},startedSlots:{199:slots}});
    await assert.rejects(()=>locate(f),/CHECKPOINT_/u);assert.ok(!f.calls.some(c=>c.run_id===100));
  }
});
test('history and artifact metadata paginate with fixed bounds, without treating another page as an empty history',async()=>{
  const failures=Array.from({length:20},(_,i)=>run(199-i));
  const f=historyFixture({runs:[run(200),...failures,run(100)],modes:Object.fromEntries(failures.map(r=>[r.id,'PRE_HTTP_FAILURE'])),
    artifacts:{100:[...Array.from({length:100},(_,i)=>artifact(1000+i,`unrelated-${i}`)),...pair()]},startedSlots:{100:[1]},});
  const selected=await locate(f);assert.equal(selected.run_id,'100');
  assert.deepEqual(f.calls.filter(c=>c.name==='runs').map(c=>c.page),[1,2]);
  assert.deepEqual(f.calls.filter(c=>c.name==='artifacts'&&c.run_id===100).map(c=>c.page),[1,2]);
});
test('run/page limits, API errors, reruns and changed history fail closed, never initialize empty state',async()=>{
  const runs=[run(200),...Array.from({length:100},(_,i)=>run(199-i))];
  const f=historyFixture({runs,artifacts:{},modes:Object.fromEntries(runs.slice(1).map(r=>[r.id,'PRE_HTTP_FAILURE']))});
  await assert.rejects(()=>locate(f),/HISTORY_LIMIT_REACHED/u);assert.equal(f.calls.filter(c=>c.name==='runs').length,5);
  for(const method of ['runs','artifacts','jobs','source']){
    const bad=historyFixture({failMethod:method});await assert.rejects(()=>locate(bad),e=>e.code==='CHECKPOINT_HISTORY_UNAVAILABLE'&&!e.message.includes('PRIVATE_RESPONSE'));
  }
  const rerun=historyFixture({runs:[run(200),run(100,{run_attempt:2})]});await assert.rejects(()=>locate(rerun),/RUN_PROVENANCE/u);
  const changed=historyFixture({runs:[run(200),run(199),run(199)],modes:{199:'PRE_HTTP_FAILURE'}});await assert.rejects(()=>locate(changed),/HISTORY_CHANGED/u);
  const empty=historyFixture({runs:[run(200),run(199)],artifacts:{},modes:{199:'PRE_HTTP_FAILURE'}});
  await assert.rejects(()=>locate(empty),/MISSING_RESTORE_VERIFIED_BACKUP/u);
  await assert.rejects(()=>locateCheckpoint({...empty,initialize:true}),/CANNOT_RESET/u);
});
test('metadata bounds and pagination changes cannot hide artifacts or jobs from the decision',async()=>{
  const tooMany=historyFixture({artifacts:{100:Array.from({length:301},(_,i)=>artifact(1000+i,`unrelated-${i}`))}});
  await assert.rejects(()=>locate(tooMany),/METADATA_LIMIT_REACHED/u);
  assert.equal(tooMany.calls.filter(c=>c.name==='artifacts').length,3);
  const jobs=jobsFor(run(100));
  const hiddenJob=historyFixture({jobRows:{100:[...jobs,...Array.from({length:100},(_,i)=>({...jobs[0],id:5000+i,name:'unexpected job'}))]}});
  await assert.rejects(()=>locate(hiddenJob),/JOB_EVIDENCE_UNVERIFIED/u);
  assert.deepEqual(hiddenJob.calls.filter(c=>c.name==='jobs').map(c=>c.page),[1,2]);
  const failing=Array.from({length:20},(_,i)=>run(199-i));
  const changed=historyFixture({runs:[run(200),...failing,run(100)],modes:Object.fromEntries(failing.map(r=>[r.id,'PRE_HTTP_FAILURE']))});
  const list=changed.github.rest.actions.listWorkflowRuns;
  changed.github.rest.actions.listWorkflowRuns=async params=>{
    const response=await list(params);if(params.page===2)response.data.total_count++;return response;
  };
  await assert.rejects(()=>locate(changed),/HISTORY_CHANGED_OR_INCOMPLETE/u);
});
test('selected legacy and encrypted result replay through actual CLI with zero network and no historical counter reset',async()=>{
  const selected=await locate(historyFixture({artifacts:{100:pair()},modes:{100:'PROVIDER'}}));
  const directory=fileURLToPath(new URL('../',import.meta.url));mkdirSync(join(directory,'.debug'),{recursive:true});
  const local=mkdtempSync(join(directory,'.debug','history-recovery-')),key='a'.repeat(64),original=terminalState();
  const bytes=Buffer.from(JSON.stringify(original)),guard='data:text/javascript,'+encodeURIComponent(`import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};globalThis.fetch=()=>{throw Error('PROVIDER_FORBIDDEN')};`);
  try{
    for(const encrypted of [false,true]){
      const path=join(local,encrypted?'encrypted.json':'legacy.json');writeFileSync(path+(encrypted?'.enc':''),encrypted?encryptCheckpoint(bytes,key):bytes);
      const env={...process.env,DISCOVERY_AUTO_ADMIN_SYNC:'false',DISCOVERY_STATE_FILE:path,DISCOVERY_CHECKPOINT_KEY:key,INITIALIZE_STATE:'false',GITHUB_RUN_ID:'200',
        DISCOVERY_POLICY:JSON.stringify({profile}),DISCOVERY_QUERY:config.query,RECONCILE_TERMINAL_FAILURE:'true',RESUME_CONTINUATION:'false',
        ARTIFACT_RECEIPT:selected.artifact_id,ARTIFACT_NAME:selected.artifact_name,ARTIFACT_RUN_ID:selected.run_id,CLAIM_ARTIFACT_RECEIPT:selected.claim_artifact_id};
      const invoke=action=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate',action],{cwd:directory,env,stdio:'pipe',encoding:'utf8'});
      invoke('unseal');assert.deepEqual(readFileSync(path),bytes);invoke('init');
      const after=JSON.parse(readFileSync(path,'utf8'));assert.deepEqual(after.jobs,original.jobs);assert.deepEqual(after.requestLedger,original.requestLedger);
      assert.deepEqual(after.contentRegistry,original.contentRegistry);assert.equal(after.terminalReconciliations[0].creditsDeducted,1);
      assert.equal(after.orchestration.workflowRunId,'200');assert.equal(after.orchestration.phase,'READY');
    }
  }finally{rmSync(local,{recursive:true,force:true});}
});
test('optional existing Run 40216232001 evidence reconciles only in memory and preserves original bytes',
  {skip:!process.env.DISCOVERY_RECOVERY_TEST_EVIDENCE},()=>{
    const directory=resolve(process.env.DISCOVERY_RECOVERY_TEST_EVIDENCE);
    const path=join(directory,'state','socialapis-discovery.json'),bytes=readFileSync(path),observed=JSON.parse(readFileSync(join(directory,'run-40216232001-observed.json'),'utf8'));
    assert.equal(createHash('sha256').update(bytes).digest('hex'),observed.stateJsonSHA256);
    assert.equal(observed.metadataKind,'OBSERVED_FROM_PERSISTED_RESULT_NOT_DISPATCH_INPUTS');
    assert.equal(observed.resultUploaded,true);assert.equal(observed.workflowRunId,'40216232001');
    const original=JSON.parse(bytes),s=structuredClone(original);
    reconcileTerminalFailure(s,{artifactId:observed.resultArtifactId,claimArtifactId:observed.claimArtifactId,workflowRunId:observed.workflowRunId,
      artifactName:'socialapis-page-40216232001-1-result',artifactKind:'RESULT',provenance:'ACTIONS_RESULT',
      attemptIndex:s.orchestration.attemptIndex,stateFingerprint:createHash('sha256').update(JSON.stringify(s)).digest('hex')});
    assert.equal(s.orchestration.phase,'TERMINAL_RECONCILED');assert.equal(s.orchestration.stopReason,'NO_VALID_PROVIDER_ITEMS');
    assert.deepEqual(s.jobs,original.jobs);assert.equal(s.jobs.length,24);assert.deepEqual(s.requestLedger,original.requestLedger);
    assert.equal(s.requestLedger.at(-1).actualRequests,1);assert.equal(s.requestLedger.at(-1).creditsDeducted,1);
    assert.equal(s.requestLedger.at(-1).rejectionDiagnostics.length,5);assert.deepEqual(readFileSync(path),bytes);
  });
