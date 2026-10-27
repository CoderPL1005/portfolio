import test from 'node:test';
import assert from 'node:assert/strict';
import {encryptCheckpoint,decryptCheckpoint,syncConfiguration,preflightAdminSync,syncReviews} from '../src/admin-sync.mjs';
import {beginExecution,claimPage,performPage,acknowledgePersistence,workflowMetrics,calendarDay} from '../src/pagination-orchestrator.mjs';
import {resolveOneRequestPolicy} from '../src/candidate-technology-profile.mjs';
import {mkdtemp,mkdir,readFile,writeFile,rm} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
import {resolve,relative,isAbsolute} from 'node:path';

const preferences=JSON.parse(await readFile(new URL('../config/backend-target-preferences.json',import.meta.url),'utf8'));
const uid=n=>`00000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
const policy={portfolioSnapshot:{schemaVersion:1,source:'PORTFOLIO_PUBLIC_API_EXPORT',
  technologies:['C#','ASP.NET Core','Python','FastAPI'].map((name,i)=>({id:uid(i+1),name,category:'Backend'})),skills:[],experiences:[],
  projects:[{id:uid(10),status:'COMPLETED',technologies:[1,2,3,4].map(n=>({id:uid(n)}))}]}};

test('V1 producer to import contract: mixed batch, vacancy matching, repost dedup, encrypted persistence and sync-only recovery',async()=>{
  const now=new Date(),resolved=resolveOneRequestPolicy(policy,preferences);
  const state=beginExecution({jobs:[]},resolved,'81001',{now});
  state.oneRequestExecution={workflowRunId:'81001',querySelection:resolved.querySelection};
  const dotnet='Hiring Fresher .NET Backend Developer\nRequirements: Build REST APIs using .NET; 0–1 years experience\nLocation: Hanoi';
  const row=(id,text,url=`https://facebook.com/fixture/posts/${id}`)=>({basic_info:{post_id:id,post_text:text,url},engagement:{},media:{},author:{}});
  const wire={data:{items:[row('8101',dotnet),row('8102','Hiring Intern Python Backend Developer\nRequirements: Python and FastAPI; build REST APIs; no experience required\nLocation: Hanoi'),
    row('8103',dotnet),null,row('8104',dotnet,'https://facebook.com/fixture/posts/pfbidUnverified'),
    row('8105','Hiring Senior Java Backend Developer\nRequirements: Java; 5 years experience\nLocation: Hanoi'),
    row('8106','Hiring Fresher Backend Developer\nRequirements: Build REST APIs\nLocation: Hanoi'),
    row('8107','Hiring Fresher React Frontend Developer\nRequirements: React\nLocation: Hanoi')],page_info:{cursor:'next-must-never-be-fetched'}}};
  let providerCalls=0;const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='synthetic-v1-provider-key';
  try{
    claimPage(state,now);await performPage(state,{now,receipt:'123',claimArtifactName:'socialapis-page-81001-1-claim',captureReviews:true,
      enabled:true,freePlanConfirmed:true,noCashConfirmed:true,secret:process.env.SOCIALAPIS_API_KEY,transport:async(url)=>{
        providerCalls++;assert.equal(new URL(url).searchParams.get('query'),resolved.query);assert.equal(new URL(url).searchParams.has('end_cursor'),false);
        return Response.json(wire,{headers:{'X-SocialAPIs-Credits-Deducted':'1','X-SocialAPIs-Credits-Used':'6'}});
      }});
  }finally{if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  assert.equal(providerCalls,1);assert.equal(state.orchestration.pages.length,1);
  assert.equal(state.orchestration.pages[0].acceptedItems,6);assert.equal(state.orchestration.pages[0].rejectedItems,2);
  assert.equal(state.orchestration.pages[0].newRelevantOpportunities,2);
  assert.equal(state.discoveryReviews[0].items.length,8);assert.equal(state.discoveryReviews[0].items[3].rawContent,null);
  assert.ok(state.jobs.every(j=>j.publishedAt===null));assert.equal(state.jobs[0].vacancies[0].match.classification,'TARGET_MATCH');
  assert.equal(state.jobs[1].vacancies[0].match.classification,'TARGET_MATCH');
  assert.ok(state.discoveryReviews[0].items[4].sourceWarnings.includes('POST_ID_URL_MISMATCH'));
  const checkpoint=encryptCheckpoint(Buffer.from(JSON.stringify(state)),secret);
  const restored=JSON.parse(decryptCheckpoint(checkpoint,secret));const ledger=structuredClone(restored.requestLedger);
  await assert.rejects(()=>syncReviews(restored,config,async()=>{throw Error('Synthetic admin unavailable');}),/RETRY_SYNC_ONLY/u);
  acknowledgePersistence(restored,'456');assert.equal(claimPage(restored),false);
  const seen=new Set();const transport=async(_url,options)=>{
    const bundle=JSON.parse(options.body);assert.deepEqual(bundle,state.discoveryReviews[0]);
    const first=!seen.has(bundle.runId);seen.add(bundle.runId);return Response.json({data:{inserted:first?8:0,unchanged:first?0:8}});
  };
  assert.equal((await syncReviews(restored,config,transport))[0].inserted,8);
  assert.equal((await syncReviews(restored,config,transport))[0].unchanged,8);
  assert.deepEqual(restored.requestLedger[0],{...ledger[0],outcome:'CONFIRMED'});assert.equal(providerCalls,1);
  assert.equal(workflowMetrics(restored).actualRequests,1);assert.equal(workflowMetrics(restored).totalDeductedCredits,1);
  assert.equal(workflowMetrics(restored).reviewSummary.observations,8);
  // A fresh operational view must not relabel already-known content as new yield.
  const next=beginExecution({jobs:[],historicalJobs:state.jobs},resolved,'81003');
  assert.equal(next.orchestration.initialOpportunityIds.length,5);
  if(process.env.DISCOVERY_PIPELINE_TEST_BUNDLE){
    const root=fileURLToPath(new URL('../.debug/',import.meta.url)),output=resolve(process.env.DISCOVERY_PIPELINE_TEST_BUNDLE),local=relative(root,output);
    assert.ok(local&&!local.startsWith('..')&&!isAbsolute(local));
    // Optional cross-language validation uses the actual producer payload, never a hand-written C# substitute.
    await writeFile(output,JSON.stringify(restored.discoveryReviews[0]),{flag:'wx',mode:0o600});
  }
});

test('actual manual V1 CLI needs no balance JSON or rolling quota, preserving eight historic attempts today',async()=>{
  const directory=fileURLToPath(new URL('../',import.meta.url));await mkdir(join(directory,'.debug'),{recursive:true});
  const local=await mkdtemp(join(directory,'.debug','v1-init-')),output=join(local,'state.json');
  const guard='data:text/javascript,'+encodeURIComponent(`import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};globalThis.fetch=()=>{throw Error('NETWORK_FORBIDDEN')};`);
  const env={...process.env,DISCOVERY_STATE_FILE:output,INITIALIZE_STATE:'true',GITHUB_RUN_ID:'81002',DISCOVERY_AUTO_ADMIN_SYNC:'true',DISCOVERY_ONE_REQUEST_V1:'true',
    DISCOVERY_CHECKPOINT_KEY:secret,DISCOVERY_IMPORT_KEY:secret,DISCOVERY_ADMIN_ORIGIN:config.origin,DISCOVERY_QUERY:'',RESUME_CONTINUATION:'false',
    DISCOVERY_START_TIME:'2026-10-27',DISCOVERY_END_TIME:'2026-10-28',DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1',DISCOVERY_POLICY:JSON.stringify(policy)};
  for(const name of ['SOCIALAPIS_MAX_31_DAY_REQUESTS','SOCIALAPIS_MAX_31_DAY_CREDITS','SOCIALAPIS_FREE_BALANCE_EVIDENCE'])delete env[name];
  const invoke=(changes={})=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate','init'],{cwd:directory,env:{...env,...changes},stdio:'pipe'});
  try{
    for(const change of [{DISCOVERY_MAX_REQUESTS:'2'},{DISCOVERY_MAX_CREDITS:'2'},
      {DISCOVERY_QUERY:'arbitrary query'},{RESUME_CONTINUATION:'true'},{DISCOVERY_AUTO_ADMIN_SYNC:'false'},
      {DISCOVERY_POLICY:JSON.stringify({profile:{technologies:['.NET'],locations:['Hanoi'],seniorities:['Junior']}})}])assert.throws(()=>invoke(change));
    invoke();const saved=JSON.parse(await readFile(output,'utf8'));
    assert.equal(saved.requestLedger.length,0);assert.equal(saved.orchestration.config.startTime,'2026-10-27');
    assert.equal(saved.orchestration.config.endTime,'2026-10-28');assert.equal(saved.orchestration.config.rotatingQuery,null);assert.deepEqual(saved.orchestration.config.adaptiveQueries,[]);
    assert.equal(saved.oneRequestExecution.querySelection.query,saved.orchestration.config.query);
    assert.ok(!/Junior|Frontend/iu.test(saved.orchestration.config.query));
    assert.equal(saved.oneRequestExecution.querySelection.coverage,'ONE_QUERY_ONLY_NOT_ENTIRE_POOL');
    assert.equal(Object.hasOwn(saved.oneRequestExecution,'globalBudget'),false);
    saved.requestLedger=Array.from({length:8},(_,index)=>({workflowRunId:String(70000+index),logicalExecutionId:`historical-${index}`,
      day:calendarDay(),actualRequests:1,creditsDeducted:1,outcome:'CONFIRMED'}));
    // Opaque legacy metadata remains stored, but can no longer block execution.
    saved.oneRequestExecution.globalBudget={verifiedAt:'expired',ledgerFingerprint:'legacy',maxRequests31Days:8,maxCredits31Days:8};
    saved.orchestration.stopReason='REQUEST_BUDGET';
    await writeFile(output,JSON.stringify(saved));
    invoke({INITIALIZE_STATE:'false',GITHUB_RUN_ID:'81009',SOCIALAPIS_FREE_BALANCE_EVIDENCE:'not-json',SOCIALAPIS_MAX_31_DAY_REQUESTS:'8',SOCIALAPIS_MAX_31_DAY_CREDITS:'8'});
    const restarted=JSON.parse(await readFile(output,'utf8'));assert.deepEqual(restarted.requestLedger,saved.requestLedger);
    assert.equal(claimPage(restarted),true);assert.equal(restarted.requestLedger.length,9);
    assert.throws(()=>claimPage(restarted),/AMBIGUOUS_PAGE/u);
  }finally{await rm(local,{recursive:true,force:true});}
});

test('optional saved real capture through current Backend pipeline; fixture Portfolio evidence, zero real HTTP',
  {skip:!process.env.DISCOVERY_FRESH_CAPTURE_REPLAY},async()=>{
    const path=new URL('../.debug/socialapis-response.json',import.meta.url),bytes=await readFile(path);
    const wire=JSON.parse(bytes),state=beginExecution({jobs:[]},resolveOneRequestPolicy(policy,preferences),'81004');
    let replayCalls=0;const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='synthetic-capture-replay-key';
    try{
      claimPage(state);await performPage(state,{receipt:'123',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,captureReviews:true,
        secret:process.env.SOCIALAPIS_API_KEY,transport:async()=>{replayCalls++;return Response.json(wire,{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});}});
    }finally{if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
    assert.equal(replayCalls,1);assert.equal(wire.data.items.length,8);assert.equal(state.jobs.length,8);assert.equal(state.discoveryReviews[0].items.length,8);
    assert.deepEqual(await readFile(path),bytes);assert.ok(state.jobs.every(j=>j.publishedAt===null));
    console.log(JSON.stringify({captureReplayOnly:true,realHttpRequests:0,portfolioEvidence:'SYNTHETIC_REAL_SHAPED_FIXTURE',
      accepted:state.orchestration.pages[0].acceptedItems,rejected:state.orchestration.pages[0].rejectedItems,
      uniquePrimaryOpportunities:state.orchestration.pages[0].newRelevantOpportunities,reviewSummary:workflowMetrics(state).reviewSummary}));
  });

const secret='a'.repeat(64),config=syncConfiguration({DISCOVERY_ADMIN_ORIGIN:'https://portfolio-api-forn.onrender.com',DISCOVERY_IMPORT_KEY:secret});
test('private checkpoint is authenticated ciphertext, never JD plaintext; wrong key/tampering fail closed',()=>{
  const bytes=Buffer.from(JSON.stringify({contact:'private@example.invalid',rawContent:'PRIVATE_JD'}));
  const encrypted=encryptCheckpoint(bytes,secret);
  assert.equal(encrypted.includes(Buffer.from('PRIVATE_JD')),false);
  assert.deepEqual(decryptCheckpoint(encrypted,secret),bytes);
  assert.throws(()=>decryptCheckpoint(encrypted,'b'.repeat(64)),/PRIVATE_CHECKPOINT_INVALID/u);
  encrypted[encrypted.length-1]^=1;assert.throws(()=>decryptCheckpoint(encrypted,secret));
  assert.throws(()=>encryptCheckpoint(bytes,''),/KEY_REQUIRED/u);
});
test('sync configuration prevents sending scoped secret to arbitrary origin or unconfigured endpoint',()=>{
  for(const origin of ['http://portfolio-api-forn.onrender.com','https://evil.invalid','https://portfolio-api-forn.onrender.com/evil'])
    assert.throws(()=>syncConfiguration({DISCOVERY_ADMIN_ORIGIN:origin,DISCOVERY_IMPORT_KEY:secret}));
  assert.throws(()=>syncConfiguration({}));
});
test('all invalid/mismatch and duplicate observations survive strict rejection; sync retry never calls provider',async()=>{
  const s=beginExecution({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[]},{query:'Fresher .NET Hà Nội',
    profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']},maxRequests:1,maxCredits:1},'synthetic-sync');
  const originalKey=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='synthetic-sync-provider-key';
  const row={basic_info:{post_id:'123',post_text:'Hiring Senior Java Developer in Hanoi',url:'https://facebook.com/fixture/posts/pfbidUnverified'}};
  let providerCalls=0;
  try{
    claimPage(s);await performPage(s,{receipt:'123',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,
      captureReviews:true,secret:process.env.SOCIALAPIS_API_KEY,transport:async()=>{
        providerCalls++;return new Response(JSON.stringify({data:{items:[row,null,row],page_info:{cursor:null}}}),{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});
      }});
  }finally{if(originalKey===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=originalKey;}
  assert.equal(providerCalls,1);assert.equal(s.jobs.length,0);assert.equal(s.discoveryReviews[0].items.length,3);
  assert.ok(s.discoveryReviews[0].items[0].sourceWarnings.includes('POST_ID_URL_MISMATCH'));
  assert.equal(s.discoveryReviews[0].items[1].contentValidity.status,'NEEDS_REVIEW');
  assert.ok(s.discoveryReviews[0].items[2].sourceWarnings.includes('DUPLICATE_CONTENT'));
  const ledger=structuredClone(s.requestLedger),bundles=structuredClone(s.discoveryReviews);
  let syncCalls=0;await assert.rejects(()=>syncReviews(s,config,async()=>{syncCalls++;throw Error('SENSITIVE_PROVIDER_ERROR');}),/RETRY_SYNC_ONLY/u);
  assert.equal(syncCalls,1);assert.equal(s.adminSync,undefined);
  const seen=new Set();const transport=async(url,options)=>{
    assert.equal(options.redirect,'error');assert.equal(options.headers['X-Discovery-Key'],secret);
    const body=JSON.parse(options.body);assert.deepEqual(body,bundles[0]);
    const first=!seen.has(body.runId);seen.add(body.runId);
    return Response.json({data:{inserted:first?3:0,unchanged:first?0:3}});
  };
  assert.equal((await syncReviews(s,config,transport))[0].inserted,3);
  assert.equal((await syncReviews(s,config,transport))[0].unchanged,3);
  assert.equal(providerCalls,1);assert.deepEqual(s.requestLedger,ledger);assert.deepEqual(s.discoveryReviews,bundles);
});
test('readiness is checked without provider or writes; rejected/invalid receipt never becomes sync success',async()=>{
  let calls=0;await preflightAdminSync(config,async(url,options)=>{calls++;assert.equal(options.method,'GET');assert.ok(url.pathname.endsWith('/ready'));return Response.json({data:{ready:true}});});
  assert.equal(calls,1);
  await assert.rejects(()=>preflightAdminSync(config,async()=>new Response('',{status:401})),/SYNC_FAILED/u);
  const s={discoveryReviews:[{items:[{}]}]};
  await assert.rejects(()=>syncReviews(s,config,async()=>Response.json({data:{inserted:0,unchanged:0}})),/RECEIPT_INVALID/u);
  assert.equal(s.adminSync,undefined);
  await assert.rejects(()=>preflightAdminSync(config,async()=>new Response('x'.repeat(5000))),/SYNC_FAILED/u);
});
test('one-request automatic-sync demo rejects changed request/date/cursor settings before any transport',async()=>{
  const {execFileSync}=await import('node:child_process');
  const directory=fileURLToPath(new URL('../',import.meta.url));await mkdir(join(directory,'.debug'),{recursive:true});
  const local=await mkdtemp(join(directory,'.debug','sync-budget-'));
  const guard='data:text/javascript,'+encodeURIComponent(`import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};globalThis.fetch=()=>{throw Error('PROVIDER_FORBIDDEN')};`);
  const env={...process.env,DISCOVERY_STATE_FILE:join(local,'state.json'),INITIALIZE_STATE:'true',DISCOVERY_AUTO_ADMIN_SYNC:'true',DISCOVERY_CHECKPOINT_KEY:secret,
    DISCOVERY_IMPORT_KEY:secret,DISCOVERY_ADMIN_ORIGIN:config.origin,DISCOVERY_QUERY:'Fresher .NET Hà Nội',DISCOVERY_START_TIME:'2026-10-26',DISCOVERY_END_TIME:'2026-10-26',
    DISCOVERY_MAX_REQUESTS:'1',DISCOVERY_MAX_CREDITS:'1',DISCOVERY_POLICY:JSON.stringify({profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']}})};
  try{
    for(const change of [{DISCOVERY_MAX_REQUESTS:'2'},{DISCOVERY_MAX_CREDITS:'2'},{DISCOVERY_QUERY:'different'},{DISCOVERY_END_TIME:'2026-10-27'},{RESUME_CONTINUATION:'true'}])
      assert.throws(()=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate','init'],{cwd:directory,env:{...env,...change},stdio:'pipe'}),e=>e.stderr.toString().includes('ONE_REQUEST_DEMO_CONFIGURATION_REQUIRED'));
    execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate','init'],{cwd:directory,env,stdio:'pipe'});
    const saved=JSON.parse(await readFile(env.DISCOVERY_STATE_FILE,'utf8'));
    assert.equal(saved.requestLedger.length,0);assert.equal(saved.orchestration.config.maxRequests,1);assert.equal(saved.orchestration.plan.cursor,null);
  }finally{await rm(local,{recursive:true,force:true});}
});
test('existing CLI seals/restores exact historical state offline and sync replay cannot trigger any provider HTTP',async()=>{
  const directory=fileURLToPath(new URL('../',import.meta.url));await mkdir(join(directory,'.debug'),{recursive:true});
  const local=await mkdtemp(join(directory,'.debug','sync-test-')),output=join(local,'socialapis-discovery.json');
  const state=beginExecution({evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[]},
    {query:'fixture',profile:{technologies:['ASP.NET Core'],locations:['Hanoi'],seniorities:['Fresher']},maxRequests:1,maxCredits:1},'synthetic-sync-cli');
  state.discoveryReviews=[{schemaVersion:1,provider:'SOCIALAPIS',runId:'synthetic-sync-cli',query:'fixture',pageNumber:1,items:[]}];
  const env={...process.env,DISCOVERY_STATE_FILE:output,DISCOVERY_CHECKPOINT_KEY:secret,DISCOVERY_ADMIN_ORIGIN:config.origin,DISCOVERY_IMPORT_KEY:secret};
  const guard='data:text/javascript,'+encodeURIComponent(`import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('NETWORK_FORBIDDEN')};
    globalThis.fetch=async (url,options)=>{if(url.hostname!=='portfolio-api-forn.onrender.com'||url.pathname!=='/api/v1/discovery-sync/import')throw Error('UNEXPECTED_ENDPOINT');
      if(options.method!=='POST')throw Error('UNEXPECTED_METHOD');return Response.json({data:{inserted:0,unchanged:0}});};`);
  const invoke=action=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate',action],{cwd:directory,encoding:'utf8',stdio:'pipe',env});
  try{
    const bytes=Buffer.from(JSON.stringify(state));await writeFile(output,bytes);invoke('seal');
    assert.deepEqual(decryptCheckpoint(await readFile(output+'.enc'),secret),bytes);
    await rm(output);invoke('unseal');assert.deepEqual(await readFile(output),bytes);
    const stdout=invoke('sync');assert.match(stdout,/"providerRequests":0/u);assert.ok(!stdout.includes(secret));
    const after=JSON.parse(await readFile(output,'utf8'));assert.deepEqual(after.requestLedger,state.requestLedger);assert.deepEqual(after.jobs,state.jobs);
  }finally{await rm(local,{recursive:true,force:true});}
});
