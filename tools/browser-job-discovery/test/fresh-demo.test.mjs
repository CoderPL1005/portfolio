import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {readFileSync,mkdtempSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {join,resolve,relative,isAbsolute} from 'node:path';
import {execFileSync} from 'node:child_process';
import {parse} from 'yaml';
import {createFreshSession,freshClaimName} from '../src/fresh-demo.mjs';
import {beginExecution,claimPage,performPage,acknowledgePersistence,calendarDay} from '../src/pagination-orchestrator.mjs';
import {encryptCheckpoint,decryptCheckpoint,syncReviews,syncConfiguration} from '../src/admin-sync.mjs';

const config={query:'Fresher .NET Hanoi',profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher','Junior','Intern']},maxRequests:1,maxCredits:1};
const secret='synthetic-fresh-provider-key';
function previous(){
  const s=beginExecution({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[]},config,'100');claimPage(s);
  s.jobs=Array.from({length:24},(_,i)=>({provider:'SOCIALAPIS',id:String(8000+i),sourceIdentity:`SOCIALAPIS:${8000+i}`,vacancies:[]}));
  s.contentRegistry={historical:'untouched'};s.discoveryYieldHistory=[{query:'historical'}];
  s.discoveryReviews=[{schemaVersion:1,provider:'SOCIALAPIS',runId:'synthetic-historical-review',query:'historical',pageNumber:1,items:[]}];
  const reasons=Array.from({length:5},(_,index)=>({index,reason:'POST_ID_URL_MISMATCH'}));
  Object.assign(s.requestLedger[0],{actualRequests:1,creditsDeducted:1,outcome:'FAILED_OR_AMBIGUOUS',
    itemStats:{totalItems:5,acceptedItems:0,rejectedItems:5,rejectionReasons:reasons,outcome:'NO_VALID_ITEMS'},
    rejectionDiagnostics:reasons.map(r=>({...r,pageNumber:1}))});
  Object.assign(s.orchestration,{phase:'CLAIMED',status:'STOPPED_TERMINAL',stopReason:'NO_VALID_PROVIDER_ITEMS'});
  return s;
}
const options=s=>({sessionId:'fresh-offline-demo-01',workflowRunId:'200',activationSha:'a'.repeat(40),freeConfirmed:true,noCashConfirmed:true,
  proof:{artifactId:'456',claimArtifactId:'123',artifactKind:'RESULT',provenance:'ACTIONS_RESULT',workflowRunId:'100',
    artifactName:'socialapis-page-100-1-result',attemptIndex:0,stateFingerprint:createHash('sha256').update(JSON.stringify(s)).digest('hex')}});
const fresh=s=>createFreshSession(s,config,options(s));

test('manual fresh run permits eight confirmed attempts today without budget evidence and preserves history',()=>{
  const old=previous();old.orchestration.phase='READY';old.orchestration.stopReason='REQUEST_BUDGET';
  const original=old.requestLedger[0];
  old.requestLedger=Array.from({length:8},(_,index)=>({...structuredClone(original),workflowRunId:String(90+index),
    day:calendarDay(),actualRequests:1,creditsDeducted:1,outcome:'CONFIRMED'}));
  const o=options(old);
  const s=createFreshSession(old,config,o);assert.deepEqual(s.requestLedger,old.requestLedger);
  assert.equal(s.freshSession.historicalRequests,8);assert.equal(s.freshSession.historicalCredits,8);
  assert.equal(claimPage(s),true);assert.equal(s.requestLedger.length,9);
});

test('a failed provider attempt cannot reuse its durable claim for a second HTTP call',async()=>{
  const s=fresh(previous());let calls=0;
  await execute(s,null,async()=>{calls++;throw Error('synthetic connection loss');});
  assert.equal(s.orchestration.phase,'CLAIMED');
  await assert.rejects(()=>performPage(s,{receipt:'789',secret,captureReviews:true,enabled:true,freePlanConfirmed:true,noCashConfirmed:true,
    transport:async()=>{calls++;return Response.json({});}}),/AMBIGUOUS_PAGE|CLAIM_ALREADY_USED/u);
  assert.equal(calls,1);
});


test('fresh session isolates operational jobs/cursor, preserving historical dedup/yield/accounting and original evidence',()=>{
  const old=previous(),bytes=JSON.stringify(old),s=fresh(old);
  assert.equal(JSON.stringify(old),bytes);assert.equal(old.jobs.length,24);
  assert.deepEqual(s.jobs,[]);assert.equal(s.orchestration.plan.pageNumber,1);assert.equal(s.orchestration.plan.cursor,null);
  assert.deepEqual(s.historicalJobs,old.jobs); // Private historical dedup evidence survives isolation.
  assert.equal(s.contentRegistry,undefined);assert.deepEqual(s.discoveryYieldHistory,old.discoveryYieldHistory);assert.deepEqual(s.discoveryReviews,old.discoveryReviews);
  assert.deepEqual(s.requestLedger,old.requestLedger);assert.equal(s.terminalReconciliations.length,1);
  assert.equal(s.freshSession.historicalRequests,1);assert.equal(s.freshSession.historicalCredits,1);
  assert.equal(s.requestLedger[0].rejectionDiagnostics.length,5);assert.equal(s.orchestration.phase,'READY');
});
test('fresh namespace never bypasses ambiguous request, unknown cost, mismatched artifact slot or missing global accounting',()=>{
  for(const mutate of [s=>s.requestLedger[0].creditsDeducted=null,s=>s.requestLedger[0].actualRequests=null,
    s=>s.orchestration.stopReason='DISCOVERY_TIMEOUT',s=>s.orchestration.phase='IN_FLIGHT',s=>delete s.requestLedger]){
    const s=previous();mutate(s);const bytes=JSON.stringify(s);assert.throws(()=>fresh(s));assert.equal(JSON.stringify(s),bytes);
  }
  for(const patch of [{artifactName:'socialapis-page-100-2-result'},{claimArtifactId:''},{workflowRunId:'99'},{stateFingerprint:'0'.repeat(64)}]){
    const s=previous(),o=options(s);assert.throws(()=>createFreshSession(s,config,{...o,proof:{...o.proof,...patch}}));
  }
});
test('optional actual Run 40216232001 terminal evidence authorizes only audit-copy reconciliation for a NEW session',
  {skip:!process.env.DISCOVERY_RECOVERY_TEST_EVIDENCE},()=>{
    const directory=resolve(process.env.DISCOVERY_RECOVERY_TEST_EVIDENCE),path=join(directory,'state','socialapis-discovery.json');
    const bytes=readFileSync(path),observed=JSON.parse(readFileSync(join(directory,'run-40216232001-observed.json'),'utf8'));
    assert.equal(createHash('sha256').update(bytes).digest('hex'),observed.stateJsonSHA256);
    assert.equal(observed.metadataKind,'OBSERVED_FROM_PERSISTED_RESULT_NOT_DISPATCH_INPUTS');assert.equal(observed.resultUploaded,true);
    const old=JSON.parse(bytes),o=options(old);
    o.proof={...o.proof,workflowRunId:'40216232001',artifactId:observed.resultArtifactId,claimArtifactId:observed.claimArtifactId,
      artifactName:'socialapis-page-40216232001-1-result',attemptIndex:old.orchestration.attemptIndex};
    const next=createFreshSession(old,config,o);
    assert.deepEqual(next.jobs,[]);assert.equal(old.jobs.length,24);assert.deepEqual(next.requestLedger,old.requestLedger);
    assert.equal(next.freshSession.historicalRequests,old.requestLedger.reduce((n,a)=>n+a.actualRequests,0));
    assert.equal(next.freshSession.historicalCredits,old.requestLedger.reduce((n,a)=>n+a.creditsDeducted,0));
    assert.equal(next.requestLedger.at(-1).actualRequests,1);assert.equal(next.requestLedger.at(-1).creditsDeducted,1);
    assert.equal(next.requestLedger.at(-1).rejectionDiagnostics.length,5);assert.deepEqual(readFileSync(path),bytes);
  });
test('fresh mode requires new valid activation, explicit Free/no-cash and exactly one request/credit',()=>{
  const s=previous(),o=options(s);
  for(const patch of [{sessionId:'discovery-demo-2026-10-26'},{activationSha:''},{workflowRunId:''},{freeConfirmed:false},{noCashConfirmed:false}])
    assert.throws(()=>createFreshSession(s,config,{...o,...patch}));
  for(const patch of [{maxRequests:2},{maxCredits:2},{mode:'production'}])assert.throws(()=>createFreshSession(s,{...config,...patch},o));
  const next=fresh(s);assert.throws(()=>createFreshSession(next,config,options(next)),/ALREADY_INITIALIZED/u);
  assert.equal(freshClaimName(o.sessionId),'discovery-fresh-offline-demo-01-claimed');
});
async function execute(s,payload,transport){
  assert.equal(claimPage(s),true);const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY=secret;
  let calls=0;
  try{await performPage(s,{secret,receipt:'789',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,captureReviews:true,
    transport:async(...args)=>{calls++;if(transport)return transport(...args);
      return Response.json(payload,{headers:{'X-SocialAPIs-Credits-Deducted':'1','X-SocialAPIs-Credits-Used':'6'}});}});
  }finally{if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  assert.equal(calls,1);return s;
}
const row=(id='123',url=`https://www.facebook.com/fixture/posts/${id}`)=>({basic_info:{post_id:id,
  post_text:'Hiring Fresher .NET Backend Developer\nLocation: Hanoi\nRequirements: ASP.NET Core',url},owner_info:{},feedback_details:{},video_details:{}});
test('one mock HTTP page retains raw/normalized JSON privately, strict dedup and all review observations with unknown dates',async()=>{
  const s=fresh(previous()),payload={data:{items:[row(),row(),row('124','https://facebook.com/fixture/posts/pfbidUnverified'),null],page_info:{cursor:'not-for-next-request'}}};
  await execute(s,payload);
  assert.equal(s.jobs.length,1);assert.equal(s.jobs[0].publishedAt,null);
  assert.equal(s.discoveryReviews.length,2);assert.equal(s.discoveryReviews.at(-1).runId,s.freshSession.id);
  assert.equal(s.discoveryReviews.at(-1).items.length,4);
  assert.ok(s.discoveryReviews.at(-1).items[2].sourceWarnings.includes('POST_ID_URL_MISMATCH'));
  assert.equal(s.discoveryReviews.at(-1).items[3].contentValidity.status,'NEEDS_REVIEW');
  assert.deepEqual(s.privateResponseAudit.payload,payload);
  assert.throws(()=>claimPage(s),/PERSISTENCE_CONFIRMATION_REQUIRED/u);
  acknowledgePersistence(s,'999');assert.equal(claimPage(s),false);
  await assert.rejects(()=>performPage(s,{transport:()=>{assert.fail('second transport forbidden');}}));
  const bytes=Buffer.from(JSON.stringify(s)),encrypted=encryptCheckpoint(bytes,'a'.repeat(64));
  assert.equal(encrypted.includes(Buffer.from(payload.data.items[0].basic_info.post_text)),false);
  assert.deepEqual(decryptCheckpoint(encrypted,'a'.repeat(64)),bytes);
  let syncCalls=0;const seen=new Set();const sync=async(_url,opts)=>{syncCalls++;const bundle=JSON.parse(opts.body);
    assert.deepEqual(bundle,s.discoveryReviews.find(b=>b.runId===bundle.runId));const first=!seen.has(bundle.runId);seen.add(bundle.runId);
    return Response.json({data:{inserted:first?bundle.items.length:0,unchanged:first?0:bundle.items.length}});};
  const c=syncConfiguration({DISCOVERY_ADMIN_ORIGIN:'https://portfolio-api-forn.onrender.com',DISCOVERY_IMPORT_KEY:'b'.repeat(64)});
  const ledger=structuredClone(s.requestLedger);await syncReviews(s,c,sync);await syncReviews(s,c,sync);
  assert.equal(syncCalls,2);assert.deepEqual(s.requestLedger,ledger);
  assert.deepEqual([...seen],[s.freshSession.id]);assert.equal(s.discoveryReviews.length,2);
  assert.equal(s.adminSync.workflowRunId,s.freshSession.workflowRunId);assert.equal(s.adminSync.observationRunId,s.freshSession.id);
});
test('timeout is ambiguous, no retry/new session; unknown charge retains private response but cannot resume',async()=>{
  const s=fresh(previous());await execute(s,null,async()=>{throw Error('SENSITIVE_NETWORK_ERROR');});
  assert.equal(s.orchestration.phase,'CLAIMED');assert.equal(s.requestLedger.at(-1).creditsDeducted,null);
  assert.throws(()=>claimPage(s));assert.throws(()=>createFreshSession(s,config,{...options(s),sessionId:'fresh-another-demo-02',workflowRunId:'201'}));
  assert.doesNotMatch(JSON.stringify(s),/SENSITIVE_NETWORK_ERROR/u);
  const cost=fresh(previous());await execute(cost,null,async()=>Response.json({data:{items:[row()]} }));
  assert.equal(cost.requestLedger.at(-1).creditsDeducted,null);assert.equal(cost.privateResponseAudit.payload.data.items.length,1);
  assert.equal(cost.orchestration.phase,'CLAIMED');
});
test('fresh metadata never labels a later ordinary run as the old session, and fresh mode requires review capture',async()=>{
  const s=fresh(previous());claimPage(s);
  await assert.rejects(()=>performPage(s,{transport:()=>assert.fail('network forbidden')}),/FRESH_REVIEW_CAPTURE_REQUIRED/u);
  // Complete the reserved original page through the same fake transport, not a second claim.
  const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY=secret;
  try{await performPage(s,{receipt:'789',secret,enabled:true,freePlanConfirmed:true,noCashConfirmed:true,captureReviews:true,
    transport:async()=>Response.json({data:{items:[row()],page_info:{cursor:null}}},{headers:{'X-SocialAPIs-Credits-Deducted':'1'}})});
  }finally{if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  acknowledgePersistence(s,'999');const audit=structuredClone(s.privateResponseAudit);
  beginExecution(s,config,'201');await execute(s,{data:{items:[row('125')],page_info:{cursor:null}}});
  assert.equal(s.discoveryReviews.at(-1).runId,'201');assert.equal(s.discoveryReviews[1].runId,s.freshSession.id);
  assert.deepEqual(s.privateResponseAudit,audit);
});
test('fresh CLI initializes offline through existing entrypoint, no missing-state initialization or network',()=>{
  const root=fileURLToPath(new URL('../',import.meta.url));mkdirSync(join(root,'.debug'),{recursive:true});
  const dir=mkdtempSync(join(root,'.debug','fresh-test-')),file=join(dir,'state.json'),s=previous(),o=options(s);
  const guard='data:text/javascript,'+encodeURIComponent(`import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};globalThis.fetch=()=>{throw Error('PROVIDER_FORBIDDEN')};`);
  const env={...process.env,DISCOVERY_STATE_FILE:file,DISCOVERY_AUTO_ADMIN_SYNC:'true',DISCOVERY_FRESH_SESSION_ID:o.sessionId,
    DISCOVERY_FRESH_ACTIVATION_SHA:o.activationSha,GITHUB_RUN_ID:o.workflowRunId,DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1',
    SOCIALAPIS_FREE_PLAN_CONFIRMED:'true',SOCIALAPIS_NO_CASH_CONFIRMED:'true',DISCOVERY_POLICY:JSON.stringify({profile:config.profile}),
    DISCOVERY_QUERY:config.query,DISCOVERY_START_TIME:'',DISCOVERY_END_TIME:'',INITIALIZE_STATE:'false',RESUME_CONTINUATION:'false',
    DISCOVERY_IMPORT_KEY:'b'.repeat(64),DISCOVERY_CHECKPOINT_KEY:'a'.repeat(64),DISCOVERY_ADMIN_ORIGIN:'https://portfolio-api-forn.onrender.com',
    ARTIFACT_RECEIPT:'456',CLAIM_ARTIFACT_RECEIPT:'123',ARTIFACT_NAME:'socialapis-page-100-1-result',ARTIFACT_RUN_ID:'100'};
  const invoke=(action='fresh-init',extra={})=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate',action],{cwd:root,env:{...env,...extra},stdio:'pipe',encoding:'utf8'});
  try{
    assert.throws(invoke);writeFileSync(file,JSON.stringify(s));const stdout=invoke(),saved=JSON.parse(readFileSync(file,'utf8'));
    assert.equal(saved.freshSession.id,o.sessionId);assert.deepEqual(saved.jobs,[]);assert.equal(saved.requestLedger.length,1);
    assert.match(stdout,/"providerRequests":0/u);assert.ok(!stdout.includes(env.DISCOVERY_IMPORT_KEY));
    assert.throws(()=>invoke('summary',{DISCOVERY_AUTO_ADMIN_SYNC:'false'}),e=>e.stderr.toString().includes('PRIVATE_FRESH_CHECKPOINT_REQUIRES_ENCRYPTED_MODE'));
  }finally{rmSync(dir,{recursive:true,force:true});}
});
test('optional private real capture replays through fresh normalization/review with no HTTP and unchanged capture',
  {skip:!process.env.DISCOVERY_FRESH_CAPTURE_REPLAY},async()=>{
    const root=fileURLToPath(new URL('../',import.meta.url)),path=join(root,'.debug','socialapis-response.json');
    const bytes=readFileSync(path),payload=JSON.parse(bytes),s=fresh(previous());
    assert.equal(payload.data.items.length,8);await execute(s,payload);
    assert.equal(s.discoveryReviews.at(-1).items.length,8);assert.deepEqual(readFileSync(path),bytes);
    assert.equal(s.privateResponseAudit.payload.data.items.length,8);
    assert.equal(s.requestLedger.at(-1).actualRequests,1);assert.equal(s.jobs.length,8);
    assert.ok(s.jobs.every(j=>j.publishedAt===null));
    if(process.env.DISCOVERY_FRESH_CONTRACT_OUTPUT){
      const output=resolve(process.env.DISCOVERY_FRESH_CONTRACT_OUTPUT),local=relative(join(root,'.debug'),output);
      assert.ok(local&&!local.startsWith('..')&&!isAbsolute(local));
      assert.notEqual(output,resolve(path));
      writeFileSync(output,JSON.stringify(s.discoveryReviews.at(-1)),{flag:'wx',mode:0o600});
    }
  });
test('fresh manual workflow atomically burns NEW claim once; unchanged old marker and no dispatch/retry/delete operations',async()=>{
  const workflow=parse(readFileSync(new URL('../../../.github/workflows/socialapis-job-discovery.yml',import.meta.url),'utf8'));
  const job=workflow.jobs.discover,steps=job.steps,activation=steps.find(s=>s.id==='fresh_activation');
  assert.equal(workflow.permissions.contents,'read');assert.equal(job.permissions.contents,'write');
  assert.equal(job.permissions.actions,'read');assert.equal(steps.find(s=>s.uses?.startsWith('actions/checkout')).with['persist-credentials'],false);
  assert.deepEqual(Object.keys(workflow.on),['workflow_dispatch']);assert.equal(workflow.on.workflow_dispatch.inputs.fresh_session_id.default,'');
  assert.equal(workflow.concurrency.group,'socialapis-job-discovery-state');assert.equal(workflow.concurrency['cancel-in-progress'],false);
  assert.ok(steps.findIndex(s=>s.run==='node src/cli.mjs --orchestrate sync-ready')<steps.indexOf(activation));
  assert.equal(activation.with.retries,0);assert.doesNotMatch(activation.with.script,/deleteRef|updateRef|createWorkflowDispatch|discovery-demo-2026-10-26-claimed/u);
  const old={...process.env};let calls=0;const refs=new Set();
  Object.assign(process.env,{GITHUB_WORKSPACE:fileURLToPath(new URL('../../../',import.meta.url)),FRESH_ID:'fresh-offline-demo-01',FRESH_REQUESTS:'1',FRESH_CREDITS:'1',FRESH_AUTO_SYNC:'true',FRESH_FREE:'true',FRESH_LIVE:'true',FRESH_RESUME:'false',FRESH_INITIALIZE:'false'});
  const invoke=new (Object.getPrototypeOf(async function(){}).constructor)('github','context','core',activation.with.script);
  const github={rest:{git:{createRef:async({ref})=>{calls++;if(refs.has(ref))throw Error('REFERENCE_ALREADY_EXISTS');refs.add(ref);}}}};
  try{await invoke(github,{repo:{owner:'synthetic',repo:'fixture'},sha:'a'.repeat(40)},{setOutput(){}});
    await assert.rejects(()=>invoke(github,{repo:{},sha:'a'.repeat(40)},{setOutput(){}}),/REFERENCE_ALREADY_EXISTS/u);
    assert.equal(calls,2);assert.deepEqual([...refs],['refs/tags/discovery-fresh-offline-demo-01-claimed']);
    process.env.FRESH_REQUESTS='2';await assert.rejects(()=>invoke(github,{},{}),/CONFIGURATION_INVALID/u);assert.equal(calls,2);
  }finally{for(const key of Object.keys(process.env))if(!(key in old))delete process.env[key];Object.assign(process.env,old);}
});
