import test, {mock} from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import fs from 'node:fs';
import fsPromises from 'node:fs/promises';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import childProcess from 'node:child_process';
import {syncBuiltinESMExports} from 'node:module';
import {JobPostExtractor} from '../src/core.mjs';

// Import the existing module successfully. Missing new behavior is an assertion
// failure, never an import error or a test implementation of the diagnostic.
const emissions=[];let sideEffects=0;
const deny=()=>{sideEffects++;throw Error('OFFLINE_SIDE_EFFECT_FORBIDDEN');};
mock.method(globalThis,'fetch',deny);
for(const api of [http,https])for(const k of ['get','request'])mock.method(api,k,deny);
mock.method(net.Socket.prototype,'connect',deny);
for(const k of ['spawn','spawnSync','exec','execSync','execFile','execFileSync','fork'])mock.method(childProcess,k,deny);
for(const k of ['writeFile','appendFile','rename','unlink','rm','mkdir','rmdir','copyFile','truncate','symlink','link','chmod','chown']){
  mock.method(fs,k,deny);mock.method(fs,`${k}Sync`,deny);mock.method(fsPromises,k,deny);
}
mock.method(fs,'createWriteStream',deny);syncBuiltinESMExports();
for(const k of ['log','error','warn','info','debug'])mock.method(console,k,(...args)=>emissions.push(args.join(' ')));
const implementation=await import('../src/checkpoint-inspection.mjs');
const digest=value=>createHash('sha256').update(value,'utf8').digest('hex');
const jobHash=j=>digest([j.title,j.employer,j.location,j.requirements,j.body].join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim());
const sentinels=['SYNTHETIC_PRIVATE_TEXT','person@example.invalid','SYNTHETIC_CURSOR','SYNTHETIC_SECRET', 'PRIVATE_HOSTILE_KEY'];
const rules=['JOB_PROVIDER_INVALID','JOB_ID_INVALID','JOB_SOURCE_IDENTITY_INVALID','JOB_DUPLICATE_IDENTITY','JOB_FIELD_TYPE_INVALID',
  'JOB_PERSISTED_HASH_MISMATCH','REVIEW_ITEMS_INVALID','REVIEW_BUNDLE_DUPLICATE','REVIEW_ITEM_INVALID','REVIEW_ITEM_INDEX_INVALID',
  'OBSERVATION_RAW_TYPE_INVALID','OBSERVATION_RAW_HASH_MISMATCH','OBSERVATION_NULL_HASH_INVALID'];
function legacyJob(id='7001'){
  const j={provider:'SOCIALAPIS',id,sourceIdentity:`SOCIALAPIS:${id}`,title:'Synthetic role',employer:'Synthetic company',
    location:'Synthetic location',requirements:'Synthetic requirement',body:sentinels[0]};
  return {...j,contentHash:jobHash(j)};
}
function state(){return {jobs:[legacyJob()],historicalJobs:[],discoveryReviews:[]};}
function bundle(rawContent='synthetic observation'){
  return {runId:'40482593764',query:'synthetic query',pageNumber:1,items:[{itemIndex:0,rawContent,contentHash:rawContent===null?null:digest(rawContent)}]};
}
function extracted(enriched=false){
  const post={provider:'SOCIALAPIS',id:'7001',source:'FACEBOOK',sourceUrl:'https://www.facebook.com/7001',
    title:'',employer:'',location:'',requirements:'',body:enriched?
      'Company: SYNTHETICEMPLOYER\nFresher .NET Backend Developer\nHà Nội\nRequirements:\n- Experience with ASP.NET Core':
      'Unclassified synthetic announcement'};
  return {input:post,job:new JobPostExtractor().extract(post)};
}
function privacy(r){
  const text=JSON.stringify(r);
  assert.ok(Buffer.byteLength(text)<=16*1024,'bounded metadata output');
  for(const s of sentinels)assert.ok(!text.includes(s),'no private sentinel output');
}
function shape(r){
  assert.equal(typeof r.valid,'boolean');assert.equal(Number.isSafeInteger(r.totalFailures),true);
  assert.equal(r.reportedFailures,r.failures.length);assert.equal(r.truncated,r.totalFailures>r.reportedFailures);
  assert.equal(r.valid,r.totalFailures===0);assert.ok(r.reportedFailures<=16);
  for(const f of r.failures){
    assert.ok(['result','originalFinal','syncFinal'].includes(f.role));
    assert.ok(['jobs','historicalJobs','discoveryReviews'].includes(f.collection));
    assert.ok(rules.includes(f.rule));assert.ok(Number.isSafeInteger(f.recordIndex)&&f.recordIndex>=0);
    assert.ok(Object.keys(f).every(k=>['role','collection','recordIndex','itemIndex','rule','fieldFlags','hashMatches'].includes(k)));
    if('itemIndex' in f)assert.ok(Number.isSafeInteger(f.itemIndex)&&f.itemIndex>=0);
    if('hashMatches' in f)assert.equal(typeof f.hashMatches,'boolean');
    for(const [k,v] of Object.entries(f.fieldFlags??{})){
      assert.ok(['title','employer','location','requirements','body'].includes(k));
      assert.deepEqual(Object.keys(v).sort(),['present','type']);assert.equal(typeof v.present,'boolean');
      assert.ok(['missing','null','string','number','boolean','object','array'].includes(v.type));
    }
  }
  privacy(r);return r;
}
function diagnose(s,role='result'){
  assert.equal(typeof implementation.diagnoseContentValidation,'function','required rule-level diagnostic is not implemented');
  const before=JSON.stringify(s),r=implementation.diagnoseContentValidation(s,role);
  assert.equal(JSON.stringify(s),before,'diagnostic never mutates input');return shape(r);
}
function has(r,rule,collection='jobs',recordIndex=0){
  const f=r.failures.find(f=>f.rule===rule&&f.collection===collection&&f.recordIndex===recordIndex);
  assert.ok(f,`missing precise failure ${rule} in ${collection}[${recordIndex}]`);return f;
}
test.afterEach(()=>{assert.equal(sideEffects,0,'zero network/process/filesystem mutation attempts');privacy(emissions.splice(0));});
test.after(()=>mock.restoreAll());

test('CONTROL: actual producer nullable and enriched fixtures retain pre-extraction hash',()=>{
  for(const enriched of [false,true]){
    const {input,job}=extracted(enriched);assert.equal(job.contentHash,jobHash(input));assert.equal(job.body,input.body);
    assert.equal(job.sourceIdentity,'SOCIALAPIS:7001');assert.equal(job.extractionVersion,'vacancy-aware-v1.1');
    if(enriched){for(const k of ['title','employer','location','requirements'])assert.equal(typeof job[k],'string');assert.notEqual(jobHash(job),job.contentHash);}
    else assert.equal(job.title,null);
  }
});
test('T01: legacy string-only hash contract is accepted with no failures',()=>{
  assert.deepEqual(diagnose(state()),{valid:true,totalFailures:0,reportedFailures:0,truncated:false,failures:[]});
});
test('T01: actual nullable producer output remains rejected, with type flags not invented hash material',()=>{
  const s=state();s.jobs=[extracted().job];const r=diagnose(s);assert.equal(r.valid,false);
  const f=has(r,'JOB_FIELD_TYPE_INVALID');assert.deepEqual(f.fieldFlags.title,{present:true,type:'null'});
  assert.ok(!r.failures.some(f=>f.rule==='JOB_PERSISTED_HASH_MISMATCH'),'cannot hash invalid material by coercion');
});
test('T02: actual enriched producer hash remains rejected against persisted fields',()=>{
  const s=state();s.jobs=[extracted(true).job];const r=diagnose(s);
  assert.equal(has(r,'JOB_PERSISTED_HASH_MISMATCH').hashMatches,false);assert.equal(r.valid,false);
  assert.ok(!r.failures.some(f=>f.rule==='JOB_FIELD_TYPE_INVALID'));
});
for(const value of ['0'.repeat(64),null,123])test(`T03: genuine saved hash defect ${typeof value}`,()=>{
  const s=state();s.jobs[0].contentHash=value;assert.equal(has(diagnose(s),'JOB_PERSISTED_HASH_MISMATCH').hashMatches,false);
});
test('T03: missing saved hash is a mismatch',()=>{const s=state();delete s.jobs[0].contentHash;has(diagnose(s),'JOB_PERSISTED_HASH_MISMATCH');});
test('T03: producer CRLF/NFC/trim normalization remains accepted',()=>{
  const s=state();s.jobs[0].body='  synthetic e\u0301\r\ntext\r  ';s.jobs[0].contentHash=jobHash(s.jobs[0]);assert.equal(diagnose(s).valid,true);
});
for(const [field,value,rule] of [['provider','OTHER','JOB_PROVIDER_INVALID'],['id','','JOB_ID_INVALID'],['id',12,'JOB_ID_INVALID'],
  ['sourceIdentity','OTHER:7001','JOB_SOURCE_IDENTITY_INVALID']])test(`T04: ${field} ${typeof value} has fixed failure rule`,()=>{
  const s=state();s.jobs[0][field]=value;has(diagnose(s),rule);
});
for(const [value,type] of [[null,'null'],[42,'number'],[false,'boolean'],[{},'object'],[[],'array']])test(`T04: ${type} field classified safely`,()=>{
  const s=state();s.jobs[0].requirements=value;
  assert.deepEqual(has(diagnose(s),'JOB_FIELD_TYPE_INVALID').fieldFlags.requirements,{present:true,type});
});
test('T04: absent field distinguished from null',()=>{
  const s=state();delete s.jobs[0].requirements;
  assert.deepEqual(has(diagnose(s),'JOB_FIELD_TYPE_INVALID').fieldFlags.requirements,{present:false,type:'missing'});
});
for(const collection of ['jobs','historicalJobs'])test(`T05: duplicates within ${collection} rejected`,()=>{
  const s=state();s[collection]=[legacyJob(),legacyJob()];has(diagnose(s),'JOB_DUPLICATE_IDENTITY',collection,1);
});
test('T05: identity may occur once in each collection',()=>{const s=state();s.historicalJobs=[legacyJob()];assert.equal(diagnose(s).valid,true);});
test('T06: observation exact raw hash does not trim or normalize',()=>{
  const s=state();s.discoveryReviews=[bundle('  synthetic e\u0301\r\n')];assert.equal(diagnose(s).valid,true);
  s.discoveryReviews[0].items[0].contentHash=digest('synthetic é');
  assert.equal(has(diagnose(s),'OBSERVATION_RAW_HASH_MISMATCH','discoveryReviews').hashMatches,false);
});
test('T06: observation null/raw hash pairing remains strict',()=>{
  const s=state();s.discoveryReviews=[bundle(null)];assert.equal(diagnose(s).valid,true);
  s.discoveryReviews[0].items[0].contentHash='0'.repeat(64);has(diagnose(s),'OBSERVATION_NULL_HASH_INVALID','discoveryReviews');
});
for(const value of [undefined,17,{},[]])test(`T06: observation invalid raw type ${typeof value}`,()=>{
  const s=state();s.discoveryReviews=[bundle()];s.discoveryReviews[0].items[0].rawContent=value;
  has(diagnose(s),'OBSERVATION_RAW_TYPE_INVALID','discoveryReviews');
});
test('T06: invalid items container',()=>{const s=state();s.discoveryReviews=[{...bundle(),items:null}];has(diagnose(s),'REVIEW_ITEMS_INVALID','discoveryReviews');});
test('T06: duplicate bundle and invalid item are distinct',()=>{
  const s=state(),b=bundle();s.discoveryReviews=[b,structuredClone(b)];s.discoveryReviews[1].items=[null];
  const r=diagnose(s);has(r,'REVIEW_BUNDLE_DUPLICATE','discoveryReviews',1);has(r,'REVIEW_ITEM_INVALID','discoveryReviews',1);
});
test('T06: item index output is array position, never untrusted saved value',()=>{
  const s=state();s.discoveryReviews=[bundle()];s.discoveryReviews[0].items[0].itemIndex=sentinels[3];
  assert.equal(has(diagnose(s),'REVIEW_ITEM_INDEX_INVALID','discoveryReviews').itemIndex,0);
});
test('T07: multiple independent defects in one record all reported',()=>{
  const s=state();Object.assign(s.jobs[0],{provider:'OTHER',id:'',sourceIdentity:'OTHER',contentHash:'0'.repeat(64)});
  const r=diagnose(s);assert.equal(r.totalFailures,4);
  for(const rule of ['JOB_PROVIDER_INVALID','JOB_ID_INVALID','JOB_SOURCE_IDENTITY_INVALID','JOB_PERSISTED_HASH_MISMATCH'])has(r,rule);
});
test('T07: continue after first invalid collection and bundle',()=>{
  const s=state();s.jobs[0].provider='OTHER';s.historicalJobs=[{...legacyJob(),contentHash:'0'.repeat(64)}];
  s.discoveryReviews=[{...bundle(),items:null},bundle()];s.discoveryReviews[1].items[0].contentHash='0'.repeat(64);
  const r=diagnose(s);assert.equal(r.totalFailures,5);has(r,'JOB_PROVIDER_INVALID');has(r,'JOB_PERSISTED_HASH_MISMATCH','historicalJobs');
  has(r,'REVIEW_ITEMS_INVALID','discoveryReviews');has(r,'REVIEW_BUNDLE_DUPLICATE','discoveryReviews',1);has(r,'OBSERVATION_RAW_HASH_MISMATCH','discoveryReviews',1);
});
for(const role of ['result','originalFinal','syncFinal'])test(`T07: fixed role ${role} reported`,()=>{
  const s=state();s.jobs[0].contentHash='0'.repeat(64);assert.equal(has(diagnose(s,role),'JOB_PERSISTED_HASH_MISMATCH').role,role);
});
test('T08: private values and hostile property names never echoed',()=>{
  const s=state();Object.assign(s.jobs[0],{provider:sentinels[3],id:sentinels[2],sourceIdentity:sentinels[1],query:sentinels[1],cursor:sentinels[2],
    [sentinels[4]]:sentinels[0],contentHash:'f'.repeat(64)});
  const r=diagnose(s);assert.ok(!JSON.stringify(r).includes('f'.repeat(64)));assert.ok(!JSON.stringify(r).includes('7001'));
});
test('T08: scan all 1000 failures while reporting at most 16',()=>{
  const s=state();s.jobs=Array.from({length:1000},(_,i)=>({...legacyJob(String(7001+i)),contentHash:'0'.repeat(64)}));
  const r=diagnose(s);assert.equal(r.totalFailures,1000);assert.equal(r.reportedFailures,16);assert.equal(r.truncated,true);assert.equal(r.valid,false);
});
test('T08: deeply frozen input is not mutated',()=>{
  const freeze=v=>{if(v&&typeof v==='object'){Object.values(v).forEach(freeze);Object.freeze(v);}return v;};
  assert.equal(diagnose(freeze(state())).valid,true);
});
test('T08: deterministic repeat inspection',()=>{
  const s=state();s.jobs[0].provider='OTHER';assert.deepEqual(diagnose(s),diagnose(s));
});

// Full synthetic comparator boundary. No ACK/Fresh/crypto/transport execution.
function comparisonInput(){
  const logical='40482593764',uploader='40488676716',pins={claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974};
  const ledger=Array.from({length:10},(_,i)=>({workflowRunId:i===9?logical:String(30000000000+i),actualRequests:1,creditsDeducted:1,
    outcome:i===6?'FAILED_OR_AMBIGUOUS':i===9?'COMPLETED_UNCONFIRMED':'CONFIRMED'}));
  Object.assign(ledger[9],{logicalExecutionId:'synthetic',query:'synthetic query',allocation:'CORE',pageNumber:1,startTime:'2026-01-01',endTime:'2026-01-02',
    claimArtifactReceipt:String(pins.claim),claimArtifactName:`socialapis-page-${logical}-1-claim`});
  const result={...state(),requestLedger:ledger,terminalReconciliations:[{attemptIndex:6,workflowRunId:ledger[6].workflowRunId,actualRequests:1,
    creditsDeducted:1,artifactId:'6001',claimArtifactId:'6002',stateFingerprint:'1'.repeat(64)}],
    orchestration:{workflowRunId:logical,logicalExecutionId:'synthetic',attemptIndex:9,phase:'COMPLETED_UNCONFIRMED',status:'STOPPED_RESUMABLE',stopReason:'REQUEST_BUDGET',
      config:{startTime:'2026-01-01',endTime:'2026-01-02'},pages:[{workflowRunId:logical,logicalExecutionId:'synthetic',query:'synthetic query',allocation:'CORE',
        pageNumber:1,actualRequests:1,creditsDeducted:1,outcome:'COMPLETED_UNCONFIRMED'}]}};
  const originalFinal=structuredClone(result),syncFinal=structuredClone(result),claim=structuredClone(result);
  originalFinal.orchestration.phase='READY';originalFinal.requestLedger[9].outcome='CONFIRMED';
  Object.assign(originalFinal.orchestration.pages[0],{outcome:'CONFIRMED',artifactReceipt:String(pins.result)});
  claim.orchestration.phase='CLAIMED';claim.orchestration.pages=[];claim.jobs=[];
  Object.assign(claim.requestLedger[9],{actualRequests:null,creditsDeducted:null,outcome:'RESERVED_AMBIGUOUS_UNTIL_COMPLETED'});
  delete claim.requestLedger[9].claimArtifactReceipt;delete claim.requestLedger[9].claimArtifactName;
  const archiveDigests={},artifacts=Object.entries(pins).map(([role,id])=>{
    archiveDigests[id]=`sha256:${digest(role)}`;
    return {id,name:role==='claim'?`socialapis-page-${logical}-1-claim`:role==='result'?`socialapis-page-${logical}-1-result`:'socialapis-discovery-state',
      expired:false,size_in_bytes:100,digest:archiveDigests[id],workflow_run:{id:Number(role==='syncFinal'?uploader:logical),repository_id:1349147313}};
  });
  return {snapshots:{claim,result,originalFinal,syncFinal},repository:{id:1349147313,full_name:'CoderPL1005/portfolio'},archiveDigests,artifacts,
    history:{source:'AUTHENTICATED_GITHUB_GET',complete:true,stable:true,sourceProvenanceVerified:true,
      runs:[{id:uploader,status:'completed',runAttempt:1,conclusion:'success',providerSteps:[{status:'completed',conclusion:'skipped'}]}]}};
}
function compare(input){return implementation.compareAcknowledgedCheckpoints(input);}
test('CONTROL: existing comparator accepts independent synthetic full topology',()=>{
  const r=compare(comparisonInput());assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');assert.equal(r.checks.contentHashesValid,true);
});
test('T09: comparator valid summary preserves YES and all existing checks',()=>{
  const r=compare(comparisonInput());assert.equal(r.eligibility,'YES');assert.deepEqual(r.reasons,[]);
  assert.ok(r.contentValidation,'new diagnostic summary required');shape(r.contentValidation);assert.equal(r.contentValidation.valid,true);
});
for(const enriched of [false,true])test(`T09: historical ${enriched?'enriched':'nullable'} jobs remain NO with precise diagnostics`,()=>{
  const input=comparisonInput();for(const role of ['result','originalFinal','syncFinal'])input.snapshots[role].jobs=[extracted(enriched).job];
  const r=compare(input);assert.equal(r.eligibility,'NO');assert.equal(r.checks.contentHashesValid,false);
  assert.deepEqual(r.reasons,['PROTECTED_STATE_OR_ACK_INVARIANT_FAILED']);assert.ok(r.contentValidation,'diagnostic required');
  const d=shape(r.contentValidation);assert.equal(d.totalFailures,3);
  assert.deepEqual(d.failures.map(f=>f.role),['result','originalFinal','syncFinal']);
  for(const f of d.failures)assert.equal(f.rule,enriched?'JOB_PERSISTED_HASH_MISMATCH':'JOB_FIELD_TYPE_INVALID');
});
test('T09: genuine hash damage remains NO, not reclassified historical acceptance',()=>{
  const input=comparisonInput();for(const role of ['result','originalFinal','syncFinal'])input.snapshots[role].jobs[0].contentHash='0'.repeat(64);
  const r=compare(input);assert.equal(r.eligibility,'NO');assert.ok(r.contentValidation,'diagnostic required');
  assert.equal(shape(r.contentValidation).totalFailures,3);
});
test('T09: combined diagnostics use one global limit and count all snapshot failures',()=>{
  const input=comparisonInput();for(const role of ['result','originalFinal','syncFinal'])input.snapshots[role].jobs=
    Array.from({length:100},(_,i)=>({...legacyJob(String(7001+i)),contentHash:'0'.repeat(64)}));
  const r=compare(input);assert.equal(r.eligibility,'NO');assert.ok(r.contentValidation,'diagnostic required');
  const d=shape(r.contentValidation);assert.equal(d.totalFailures,300);assert.equal(d.reportedFailures,16);assert.equal(d.truncated,true);privacy(r);
});
test('T09: bad provenance rejects before diagnostic and never widens artifact boundary',()=>{
  const input=comparisonInput();input.artifacts[0].id=123;const r=compare(input);
  assert.equal(r.ok,false);assert.equal(r.errorCode,'ARTIFACT_PROVENANCE_INVALID');assert.equal(r.contentValidation,undefined);
});
test('T10: prior approved test files remain byte-identical',()=>{
  for(const [name,hash] of [['checkpoint-comparison.test.mjs','488cd188772325fae8a471ac814bd6745607a8aadf4b092a351e40ce1e2c13dc'],
    ['checkpoint-inspection.test.mjs','f8605e7253c464e057534c80168da58b459ec4755a626766a7a7b7452cef7cb5']])
    assert.equal(digest(fs.readFileSync(new URL(name,import.meta.url))),hash);
});
