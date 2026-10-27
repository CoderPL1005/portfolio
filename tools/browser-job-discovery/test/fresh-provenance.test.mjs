import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createFreshSession} from '../src/fresh-demo.mjs';
import {beginExecution,claimPage,acknowledgePersistence} from '../src/pagination-orchestrator.mjs';

// Synthetic reproduction of the operator-reported run/artifact topology only.
// These are NOT the downloaded private checkpoint or verified live receipts.
const logicalRun='40482593764',uploadRun='40488676716',artifactId='12725991974';
const config={query:'Synthetic Backend Hanoi',profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']},maxRequests:1,maxCredits:1};
const hash=s=>createHash('sha256').update(JSON.stringify(s)).digest('hex');
function checkpoint(){
  const s=beginExecution({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[]},config,logicalRun);
  claimPage(s);
  Object.assign(s.requestLedger[0],{actualRequests:1,creditsDeducted:1,outcome:'COMPLETED_UNCONFIRMED'});
  s.orchestration.pages.push({workflowRunId:logicalRun,pageNumber:1,query:config.query,outcome:'COMPLETED_UNCONFIRMED'});
  s.orchestration.phase='COMPLETED_UNCONFIRMED';
  acknowledgePersistence(s,'12723771007');
  s.jobs=[{provider:'SOCIALAPIS',sourceIdentity:'SOCIALAPIS:synthetic-history',id:'synthetic-history',vacancies:[]}];
  const items=Array.from({length:5},(_,itemIndex)=>({itemIndex,providerPostId:String(7000+itemIndex),
    sourceUrl:`https://www.facebook.com/fixture/posts/${7000+itemIndex}`,rawContent:'Synthetic recruitment observation',
    contentHash:createHash('sha256').update('Synthetic recruitment observation').digest('hex'),publishedAt:null}));
  s.discoveryReviews=[{schemaVersion:1,provider:'SOCIALAPIS',runId:logicalRun,query:config.query,pageNumber:1,items}];
  s.adminSync={mode:'recovery',workflowRunId:logicalRun,observationRunId:logicalRun,executingWorkflowRunId:uploadRun,
    completedAt:'2026-10-27T00:00:00Z',observationCount:5,
    summaries:[{runId:logicalRun,pageNumber:1,inserted:0,unchanged:5}]};
  return s;
}
// Explicit checkpoint-proof boundary approved in the PEWF test plan.
const proofFor=s=>({artifactId,artifactName:'socialapis-discovery-state',artifactKind:'CHECKPOINT',provenance:'ACTIONS_CHECKPOINT',
  workflowRunId:logicalRun,artifactUploadRunId:uploadRun,stateFingerprint:hash(s)});
const options=s=>({sessionId:'fresh-provenance-offline-v1',workflowRunId:'40540000001',activationSha:'a'.repeat(40),
  freeConfirmed:true,noCashConfirmed:true,proof:proofFor(s)});

test('fresh accepts confirmed logical discovery preserved in sync-only-uploaded checkpoint',()=>{
  const s=checkpoint(),bytes=JSON.stringify(s),next=createFreshSession(s,config,options(s));
  assert.equal(next.freshSession.sourceWorkflowRunId,logicalRun);
  assert.equal(next.freshSession.sourceArtifactUploadRunId,uploadRun);
  assert.equal(String(next.freshSession.sourceArtifactId),artifactId);
  assert.equal(next.freshSession.auditSourceFingerprint,hash(s));
  assert.deepEqual(next.requestLedger,s.requestLedger);assert.deepEqual(next.historicalJobs,s.jobs);
  assert.deepEqual(next.discoveryReviews,s.discoveryReviews);assert.deepEqual(next.jobs,[]);
  assert.equal(next.freshSession.historicalRequests,1);assert.equal(next.freshSession.historicalCredits,1);
  assert.equal(JSON.stringify(s),bytes,'source checkpoint is immutable');
});

test('CONTROL: direct original result proof remains accepted without a sync-only uploader',()=>{
  const s=checkpoint();delete s.adminSync;
  const o=options(s);o.proof={artifactId:'12723771007',artifactName:`socialapis-page-${logicalRun}-1-result`,
    artifactKind:'RESULT',provenance:'ACTIONS_RESULT',workflowRunId:logicalRun,stateFingerprint:hash(s)};
  const next=createFreshSession(s,config,o);
  assert.deepEqual(next.requestLedger,s.requestLedger);assert.equal(next.freshSession.sourceWorkflowRunId,logicalRun);
});

for(const [name,mutate] of [
  ['tampered fingerprint',(_s,p)=>p.stateFingerprint='0'.repeat(64)],
  ['wrong logical run',(_s,p)=>p.workflowRunId='40479573219'],
  ['wrong uploader',(_s,p)=>p.artifactUploadRunId='40535825604'],
  ['missing artifact receipt',(_s,p)=>delete p.artifactId],
  ['claim mislabeled as checkpoint',(_s,p)=>p.artifactName=`socialapis-page-${uploadRun}-1-claim`],
  ['missing sync provenance',(s)=>delete s.adminSync],
  ['mismatched saved uploader',(s)=>s.adminSync.executingWorkflowRunId='40537064451'],
  ['unknown credit',(s)=>s.requestLedger[0].creditsDeducted=null],
  ['unconfirmed provider attempt',(s)=>{s.orchestration.phase='CLAIMED';s.requestLedger[0].outcome='FAILED_OR_AMBIGUOUS';}]
])test(`checkpoint evidence fails closed: ${name}`,()=>{
  const s=checkpoint(),p=proofFor(s);
  // Prove this proof type and unchanged fixture are supported before corrupting
  // one property. A generic unsupported-proof rejection cannot pass this test.
  const valid=createFreshSession(s,config,options(s));
  assert.equal(valid.freshSession.sourceArtifactUploadRunId,uploadRun);
  assert.equal(valid.freshSession.sourceWorkflowRunId,logicalRun);
  mutate(s,p);
  if(!name.includes('fingerprint'))p.stateFingerprint=hash(s);
  const bytes=JSON.stringify(s);
  assert.throws(()=>createFreshSession(s,config,{...options(s),proof:p}));
  assert.equal(JSON.stringify(s),bytes,'no reconciliation or mutation on rejection');
});

test('saved sync receipts must match bundle scope/counts and cannot ACK an unconfirmed page',()=>{
  for(const mutate of [s=>s.adminSync.summaries[0].unchanged=4,s=>s.adminSync.observationCount=4,
    s=>s.adminSync.observationRunId='unrelated',s=>s.adminSync.completedAt='invalid',
    s=>{s.orchestration.phase='COMPLETED_UNCONFIRMED';s.requestLedger[0].outcome='COMPLETED_UNCONFIRMED';}]){
    const s=checkpoint();assert.doesNotThrow(()=>createFreshSession(s,config,options(s)));
    mutate(s);const bytes=JSON.stringify(s);
    assert.throws(()=>createFreshSession(s,config,options(s)));
    assert.equal(JSON.stringify(s),bytes);
  }
});

test('fresh-init CLI uses uploader receipt separately from saved logical run, zero provider calls',()=>{
  const root=fileURLToPath(new URL('../',import.meta.url));mkdirSync(join(root,'.debug'),{recursive:true});
  const dir=mkdtempSync(join(root,'.debug','fresh-provenance-test-')),file=join(dir,'state.json'),s=checkpoint();
  const guard='data:text/javascript,'+encodeURIComponent("import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};globalThis.fetch=()=>{throw Error('NETWORK_FORBIDDEN')};");
  const env={...process.env,DISCOVERY_ONE_REQUEST_V1:'false',DISCOVERY_STATE_FILE:file,DISCOVERY_AUTO_ADMIN_SYNC:'true',
    DISCOVERY_FRESH_SESSION_ID:'fresh-provenance-cli-v1',DISCOVERY_FRESH_ACTIVATION_SHA:'a'.repeat(40),GITHUB_RUN_ID:'40540000002',
    DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1',SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',
    DISCOVERY_POLICY:JSON.stringify({profile:config.profile}),DISCOVERY_QUERY:config.query,DISCOVERY_START_TIME:'',DISCOVERY_END_TIME:'',
    INITIALIZE_STATE:'false',RESUME_CONTINUATION:'false',DISCOVERY_IMPORT_KEY:'b'.repeat(64),DISCOVERY_CHECKPOINT_KEY:'a'.repeat(64),
    DISCOVERY_ADMIN_ORIGIN:'https://portfolio-api-forn.onrender.com',ARTIFACT_RECEIPT:artifactId,
    ARTIFACT_NAME:'socialapis-discovery-state',ARTIFACT_RUN_ID:uploadRun,CLAIM_ARTIFACT_RECEIPT:''};
  try{
    writeFileSync(file,JSON.stringify(s));
    const stdout=execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate','fresh-init'],{cwd:root,env,encoding:'utf8',stdio:'pipe'});
    const saved=JSON.parse(readFileSync(file,'utf8'));
    assert.equal(saved.freshSession.sourceWorkflowRunId,logicalRun);
    assert.equal(saved.freshSession.sourceArtifactUploadRunId,uploadRun);
    assert.deepEqual(saved.requestLedger,s.requestLedger);assert.match(stdout,/"providerRequests":0/u);
  }finally{rmSync(dir,{recursive:true,force:true});}
});
