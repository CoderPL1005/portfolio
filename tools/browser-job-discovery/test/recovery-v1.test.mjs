import test,{mock} from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import fs from 'node:fs';
import fsPromises from 'node:fs/promises';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import {JobPostExtractor} from '../src/core.mjs';
import {createFreshSession} from '../src/fresh-demo.mjs';
import {locateCheckpoint} from '../src/checkpoint-history.mjs';
import {historyFixture,run,artifact} from './fixtures/checkpoint-history.mjs';
import * as inspection from '../src/checkpoint-inspection.mjs';

// Synthetic internal-port evidence only, never a production receipt/capture.
const sha=v=>createHash('sha256').update(v).digest('hex');
const hashInput=j=>sha([j.title,j.employer,j.location,j.requirements,j.body].join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim());
const logical='40482593764',uploader='40488676716';
const pins={claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974};
const revision='81e15d15e379d4ea2c27a654a5966d056404efe5';
const manifest={core:'9f3e07ef95496a52801dcf9886cdff56158ea1075f1124343e64e3ba5a99416e',
  adapter:'9e0227a424ef12ce06f24a05490d5431f7294fc3680a570696fa79fb437e5d0d',
  vacancy:'3f133f47d2d917f0580579f2afac5ce2781080791287ebb71824b74faf5c9b63'};
let network=0,writes=0;const emissions=[];
const denyNetwork=()=>{network++;throw Error('OFFLINE_NETWORK_FORBIDDEN');};
const denyWrite=()=>{writes++;throw Error('OFFLINE_WRITE_FORBIDDEN');};
mock.method(globalThis,'fetch',denyNetwork);mock.method(net.Socket.prototype,'connect',denyNetwork);
for(const api of [http,https])for(const k of ['get','request'])mock.method(api,k,denyNetwork);
for(const k of ['writeFile','appendFile','rename','unlink','rm','mkdir']){
  mock.method(fs,k,denyWrite);mock.method(fs,`${k}Sync`,denyWrite);mock.method(fsPromises,k,denyWrite);
}
for(const k of ['log','error','warn'])mock.method(console,k,(...args)=>emissions.push(args.join(' ')));
test.afterEach(()=>{assert.equal(network,0);assert.equal(writes,0);assert.equal(emissions.splice(0).length,0,'pure recovery policy must not log payloads');});
test.after(()=>mock.restoreAll());

function contentCase(kind='nullable'){
  const input={provider:'SOCIALAPIS',id:'7001',sourceIdentity:'SOCIALAPIS:7001',source:'FACEBOOK',sourceUrl:'https://www.facebook.com/7001',
    title:'',employer:'',location:'',requirements:'',body:kind==='enriched'?
      'Company: SYNTHETICEMPLOYER\nFresher .NET Backend Developer\nHà Nội\nRequirements:\n- Experience with ASP.NET Core':'Unclassified synthetic announcement'};
  const job=kind==='legacy'?{...input,contentHash:hashInput(input)}:new JobPostExtractor().extract(input);
  const evidence={source:'AUTHENTICATED_GITHUB_GET',repository:{id:1349147313,full_name:'CoderPL1005/portfolio'},
    runId:logical,headSha:revision,sourceDigests:structuredClone(manifest),
    records:[{sourceIdentity:job.sourceIdentity,input:structuredClone(input),projection:structuredClone(job),inputHash:hashInput(input),originArtifactId:pins.result}]};
  if(kind==='legacy'){
    evidence.headSha='afc71f0cde25d9aebe04f748edb33f35c330fc9a';
    evidence.sourceDigests={core:'ce6d8aebe486ea362fe7968ae330a69133cf1aa1517a537b0d3e4bcdf2d80175',
      adapter:'61df60d384462b4e9a425e475609cf0b5860a978490c5cf6f96c9f494fe03f35',vacancy:null};
  }
  return {snapshot:{jobs:[job],historicalJobs:[],discoveryReviews:[]},producerEvidence:evidence};
}
function validate(input){
  assert.equal(typeof inspection.validateRecoveryContent,'function','version-aware content verification is missing');
  const before=JSON.stringify(input),r=inspection.validateRecoveryContent(input);assert.equal(JSON.stringify(input),before);return r;
}
function select(input){
  assert.equal(typeof inspection.selectAcknowledgedRecoverySource,'function','evidence-validated checkpoint selection is missing');
  const before=JSON.stringify(input),r=inspection.selectAcknowledgedRecoverySource(input);assert.equal(JSON.stringify(input),before);return r;
}
function blocked(r){assert.equal(r.status,'BLOCKED');assert.equal(typeof r.reason,'string');assert.match(r.reason,/^[A-Z][A-Z0-9_]+$/u);}
function graph(){
  const c=contentCase('enriched');
  const requestLedger=Array.from({length:10},(_,i)=>({workflowRunId:i===9?logical:String(30000000000+i),actualRequests:1,creditsDeducted:1,
    outcome:i===6?'FAILED_OR_AMBIGUOUS':i===9?'COMPLETED_UNCONFIRMED':'CONFIRMED'}));
  Object.assign(requestLedger[9],{logicalExecutionId:'synthetic-current',query:'Synthetic backend',allocation:'CORE',pageNumber:1,
    startTime:'2026-01-01',endTime:'2026-01-02',claimArtifactReceipt:String(pins.claim),claimArtifactName:`socialapis-page-${logical}-1-claim`});
  const result={...c.snapshot,evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',requestLedger,
    terminalReconciliations:[{attemptIndex:6,workflowRunId:requestLedger[6].workflowRunId,actualRequests:1,creditsDeducted:1,
      artifactId:'6001',claimArtifactId:'6002',stateFingerprint:'1'.repeat(64)}],
    orchestration:{workflowRunId:logical,logicalExecutionId:'synthetic-current',attemptIndex:9,phase:'COMPLETED_UNCONFIRMED',
      status:'STOPPED_RESUMABLE',stopReason:'REQUEST_BUDGET',config:{startTime:'2026-01-01',endTime:'2026-01-02'},
      pages:[{workflowRunId:logical,logicalExecutionId:'synthetic-current',query:'Synthetic backend',allocation:'CORE',pageNumber:1,
        actualRequests:1,creditsDeducted:1,outcome:'COMPLETED_UNCONFIRMED'}]}};
  const originalFinal=structuredClone(result),syncFinal=structuredClone(result),claim=structuredClone(result);
  originalFinal.orchestration.phase='READY';originalFinal.requestLedger[9].outcome='CONFIRMED';
  Object.assign(originalFinal.orchestration.pages[0],{outcome:'CONFIRMED',artifactReceipt:String(pins.result)});
  syncFinal.adminSync={mode:'recovery',workflowRunId:logical,executingWorkflowRunId:uploader,observationRunId:logical,
    completedAt:'2026-01-03T00:00:00Z',observationCount:0,summaries:[]};
  claim.orchestration.phase='CLAIMED';claim.orchestration.pages=[];claim.jobs=[];
  Object.assign(claim.requestLedger[9],{actualRequests:null,creditsDeducted:null,outcome:'RESERVED_AMBIGUOUS_UNTIL_COMPLETED'});
  delete claim.requestLedger[9].claimArtifactName;delete claim.requestLedger[9].claimArtifactReceipt;
  const archiveDigests={},artifacts=Object.entries(pins).map(([role,id])=>{
    archiveDigests[id]=`sha256:${sha(role)}`;return {id,name:role==='claim'?`socialapis-page-${logical}-1-claim`:
      role==='result'?`socialapis-page-${logical}-1-result`:'socialapis-discovery-state',expired:false,size_in_bytes:100,
      digest:archiveDigests[id],workflow_run:{id:Number(role==='syncFinal'?uploader:logical),repository_id:1349147313}};
  });
  return {repository:{id:1349147313,full_name:'CoderPL1005/portfolio'},snapshots:{claim,result,originalFinal,syncFinal},
    artifacts,archiveDigests,producerEvidence:c.producerEvidence,
    history:{source:'AUTHENTICATED_GITHUB_GET',complete:true,stable:true,sourceProvenanceVerified:true,
      runs:[{id:uploader,status:'completed',runAttempt:1,conclusion:'success',providerSteps:[{status:'completed',conclusion:'skipped'}]}]}};
}

test('CONTROL: actual producer keeps input hash while nullable/enriched persisted fields differ',()=>{
  for(const kind of ['nullable','enriched']){
    const c=contentCase(kind),j=c.snapshot.jobs[0];assert.equal(j.contentHash,hashInput(c.producerEvidence.records[0].input));
    if(kind==='nullable')assert.equal(j.title,null);else assert.notEqual(j.contentHash,hashInput(j));
    const legacy=inspection.diagnoseContentValidation(c.snapshot,'result');assert.equal(legacy.valid,false,'old diagnostic policy remains unchanged');
  }
});
test('CONTROL: current metadata locator selects RESULT even when a final name exists',async()=>{
  const f=historyFixture({runs:[run(200),run(100)],modes:{100:'PROVIDER'},artifacts:{100:[
    artifact(123,'socialapis-page-100-1-claim'),artifact(456,'socialapis-page-100-1-result'),artifact(789,'socialapis-discovery-state')]}});
  const selected=await locateCheckpoint({...f,attempt:'1'});assert.equal(selected.artifact_id,'456');assert.ok(f.calls.every(c=>['repository','runs','artifacts','jobs','source'].includes(c.name)));
});
test('CONTROL: synthetic graph passes all old non-content invariants and stays NO under old policy',()=>{
  const r=inspection.compareAcknowledgedCheckpoints(graph());assert.equal(r.ok,true);assert.equal(r.eligibility,'NO');
  for(const [key,value] of Object.entries(r.checks))assert.equal(value,key!=='contentHashesValid',key);
});
for(const kind of ['nullable','enriched','legacy'])test(`T01/T02: verified producer contract accepts ${kind} without changing persisted hash`,()=>{
  const c=contentCase(kind);assert.equal(validate(c).status,'VERIFIED');
});
for(const [field,value] of [['body','Altered body'],['contentHash','0'.repeat(64)],['sourceIdentity','SOCIALAPIS:7999'],
  ['title','Fabricated Senior Role'],['requirements','Fabricated requirement'],['body',null]])
test(`T03: genuine ${field} tamper is rejected`,()=>{const c=contentCase('enriched');c.snapshot.jobs[0][field]=value;assert.equal(validate(c).status,'INTEGRITY_VIOLATION');});
for(const mode of ['missing','boolean','unknown-revision','wrong-source','missing-binding','marker-only'])test(`T04: ${mode} cannot authorize historical content`,()=>{
  const c=contentCase('nullable');
  if(mode==='missing')delete c.producerEvidence;
  if(mode==='boolean')c.producerEvidence={verified:true};
  if(mode==='unknown-revision')c.producerEvidence.headSha='f'.repeat(40);
  if(mode==='wrong-source')c.producerEvidence.sourceDigests.core='0'.repeat(64);
  if(mode==='missing-binding')c.producerEvidence.records=[];
  if(mode==='marker-only')c.producerEvidence={extractionVersion:'vacancy-aware-v1.1'};
  blocked(validate(c));
});
test('T04: wrong preimage cannot be legitimized by matching enriched field types',()=>{
  const c=contentCase('enriched');c.producerEvidence.records[0].input.body='Different synthetic source';
  assert.notEqual(validate(c).status,'VERIFIED');
});
test('T05: complete authenticated graph selects acknowledged original final, not sync-only uploader',()=>{
  const r=select(graph());assert.equal(r.status,'SELECTED');assert.equal(String(r.artifactId),String(pins.originalFinal));assert.equal(r.workflowRunId,logical);
});
for(const defect of ['missing-history','incomplete-history','unstable-history','later-provider','receipt','ledger','content','accounting','phase','missing-producer'])
test(`T05: ${defect} blocks selection rather than falling back`,()=>{
  const g=graph();
  if(defect==='missing-history')delete g.history;
  if(defect==='incomplete-history')g.history.complete=false;
  if(defect==='unstable-history')g.history.stable=false;
  if(defect==='later-provider')g.history.runs[0].providerSteps[0].conclusion='success';
  if(defect==='receipt')g.snapshots.originalFinal.orchestration.pages[0].artifactReceipt='9999';
  if(defect==='ledger')g.snapshots.originalFinal.requestLedger[1].outcome='UNKNOWN';
  if(defect==='content')g.snapshots.syncFinal.jobs[0].body+=' changed';
  if(defect==='accounting')g.snapshots.originalFinal.requestLedger[9].creditsDeducted=0;
  if(defect==='phase')g.snapshots.originalFinal.orchestration.phase='COMPLETED_UNCONFIRMED';
  if(defect==='missing-producer')delete g.producerEvidence;
  blocked(select(g));
});
test('T06: filename/eligibility boolean alone cannot select a recovery source',()=>{
  blocked(select({eligibility:'YES',acknowledged:true,artifactId:pins.originalFinal,name:'socialapis-discovery-state'}));
});
test('T07: selected acknowledged state satisfies unchanged Fresh guards in dry-run with preserved accounting',()=>{
  const g=graph(),selected=select(g);assert.equal(selected.status,'SELECTED');const previous=g.snapshots.originalFinal,before=JSON.stringify(previous);
  const next=createFreshSession(previous,{query:'Synthetic backend',profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']},maxRequests:1,maxCredits:1},
    {sessionId:'fresh-recovery-synthetic-20261028-01',workflowRunId:'99999999999',activationSha:'a'.repeat(40),freeConfirmed:true,noCashConfirmed:true,
      proof:{provenance:'ACTIONS_CHECKPOINT',artifactKind:'CHECKPOINT',artifactName:'socialapis-discovery-state',artifactId:String(pins.originalFinal),
        artifactUploadRunId:logical,workflowRunId:logical,stateFingerprint:sha(JSON.stringify(previous))}});
  assert.equal(JSON.stringify(previous),before);assert.deepEqual(next.requestLedger,previous.requestLedger);
  assert.deepEqual(next.historicalJobs,previous.jobs);assert.deepEqual(next.discoveryReviews,previous.discoveryReviews);
  assert.equal(next.freshSession.historicalRequests,10);assert.equal(next.freshSession.historicalCredits,10);assert.equal(next.orchestration.phase,'READY');
});
test('T07: unresolved historical accounting still fails Fresh with zero provider calls',()=>{
  const previous=graph().snapshots.originalFinal;previous.requestLedger[0].outcome='UNKNOWN';
  assert.throws(()=>createFreshSession(previous,{query:'Synthetic',profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']},maxRequests:1,maxCredits:1},
    {sessionId:'fresh-recovery-synthetic-20261028-02',workflowRunId:'99999999999',activationSha:'a'.repeat(40),freeConfirmed:true,noCashConfirmed:true,
      proof:{provenance:'ACTIONS_CHECKPOINT',artifactKind:'CHECKPOINT',artifactName:'socialapis-discovery-state',artifactId:String(pins.originalFinal),
        artifactUploadRunId:logical,workflowRunId:logical,stateFingerprint:sha(JSON.stringify(previous))}}),/FRESH_AUDIT_AMBIGUOUS/u);
});
