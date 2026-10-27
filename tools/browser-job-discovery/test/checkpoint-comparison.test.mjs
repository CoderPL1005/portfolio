import test, {mock} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import fsPromises from 'node:fs/promises';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import childProcess from 'node:child_process';
import {syncBuiltinESMExports} from 'node:module';
import {createHash} from 'node:crypto';
import {deflateRawSync} from 'node:zlib';
import {parse as parseYaml} from 'yaml';
import {encryptCheckpoint, decryptCheckpoint} from '../src/admin-sync.mjs';

// Test-facing contract for the approved diagnostic extension. No production
// behavior is implemented here. An absent export is a functional result, not a
// caught module-resolution/syntax error. Existing module import must succeed.
const key='ac'.repeat(32), token='SYNTHETIC_COMPARISON_TOKEN';
const privateValues=['SYNTHETIC_PRIVATE_RECRUITMENT_BODY','person@example.invalid','SYNTHETIC_PRIVATE_CURSOR','SYNTHETIC_SECRET_VALUE'];
const emissions=[];let blockedCalls=0;
const deny=()=>{blockedCalls++;throw Error('OFFLINE_SIDE_EFFECT_FORBIDDEN');};
mock.method(globalThis,'fetch',deny);
for(const api of [http,https]){mock.method(api,'get',deny);mock.method(api,'request',deny);}
mock.method(net.Socket.prototype,'connect',deny);
for(const method of ['spawn','spawnSync','exec','execSync','execFile','execFileSync','fork'])mock.method(childProcess,method,deny);
for(const method of ['writeFile','appendFile','rename','unlink','rm','mkdir','rmdir','copyFile','truncate','symlink','link','chmod','chown']){
  mock.method(fs,method,deny);mock.method(fs,`${method}Sync`,deny);mock.method(fsPromises,method,deny);
}
mock.method(fs,'createWriteStream',deny);syncBuiltinESMExports();
for(const method of ['log','error','warn','info','debug'])mock.method(console,method,(...args)=>emissions.push(args.map(x=>typeof x==='string'?x:JSON.stringify(x)).join(' ')));
const moduleUrl=new URL('../src/checkpoint-inspection.mjs',import.meta.url);
const implementation=await import(moduleUrl.href);
const missing=()=>({ok:false,errorCode:'COMPARISON_NOT_IMPLEMENTED'});
const compare=implementation.compareAcknowledgedCheckpoints??missing;
const inspect=implementation.inspectAcknowledgedCheckpoints??missing;
const inspectNetwork=implementation.inspectAcknowledgedFromGitHub??missing;
const digest=bytes=>createHash('sha256').update(bytes).digest('hex');
const logical='40482593764',uploader='40488676716';
const roles=['claim','result','originalFinal','syncFinal'];
const pins={claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974};
const repository={id:1349147313,full_name:'CoderPL1005/portfolio'};
const ackPaths=['orchestration.phase','orchestration.pages[0].artifactReceipt','orchestration.pages[0].outcome','requestLedger[9].outcome'];
const expectedInspectionHash='f8605e7253c464e057534c80168da58b459ec4755a626766a7a7b7452cef7cb5';

function privacy(value){
  const output=typeof value==='string'?value:JSON.stringify(value);
  for(const sentinel of [...privateValues,key,token])assert.ok(!output.includes(sentinel),'no private fixture values may be output');
  assert.ok(Buffer.byteLength(output)<=16*1024,'whole diagnostic output must be bounded');
}
test.afterEach(()=>{privacy(emissions.splice(0).join('\n'));assert.equal(blockedCalls,0,'zero network/process/filesystem mutation attempts');});
test.after(()=>mock.restoreAll());

// Independently authored synthetic snapshots; never call ACK/Fresh/recovery to
// manufacture the expected post-ACK fixture. IDs describe topology only.
function job(id){
  const post={id,provider:'SOCIALAPIS',source:'FACEBOOK',sourceIdentity:`SOCIALAPIS:${id}`,
    title:'Synthetic role',employer:'Synthetic company',location:'Synthetic location',requirements:'Synthetic requirement',
    body:`${privateValues[0]} ${id}`,publishedAt:null,vacancies:[]};
  const normalized=[post.title,post.employer,post.location,post.requirements,post.body].join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim();
  return {...post,contentHash:digest(normalized)};
}
function snapshot(phase,outcome,acknowledged){
  const ledger=Array.from({length:9},(_,index)=>({workflowRunId:String(30000000000+index),logicalExecutionId:`synthetic-old-${index}`,
    pageNumber:1,allocation:'CORE',query:`synthetic-old-${index}`,actualRequests:1,creditsDeducted:1,
    outcome:index===6?'FAILED_OR_AMBIGUOUS':'CONFIRMED'}));
  ledger.push({workflowRunId:logical,logicalExecutionId:'synthetic-current',query:'synthetic query',allocation:'CORE',pageNumber:1,
    inputCursorFingerprint:null,startTime:'2026-01-01',endTime:'2026-01-02',actualRequests:1,creditsDeducted:1,
    outcome,claimArtifactReceipt:String(pins.claim),claimArtifactName:`socialapis-page-${logical}-1-claim`});
  const j=job('7001'),second=job('7002');
  return {evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',requestLedger:ledger,jobs:[j,second],historicalJobs:[job('6999')],
    terminalReconciliations:[{version:1,attemptIndex:6,workflowRunId:ledger[6].workflowRunId,actualRequests:1,creditsDeducted:1,
      artifactId:'6001',claimArtifactId:'6002',stateFingerprint:'1'.repeat(64)}],
    orchestration:{workflowRunId:logical,logicalExecutionId:'synthetic-current',attemptIndex:9,phase,status:'STOPPED_RESUMABLE',stopReason:'REQUEST_BUDGET',
      config:{startTime:'2026-01-01',endTime:'2026-01-02'},pages:[{workflowRunId:logical,logicalExecutionId:'synthetic-current',
        query:'synthetic query',allocation:'CORE',pageNumber:1,actualRequests:1,creditsDeducted:1,outcome,
        returnedPostIds:['7001','7002'],nextCursorPresent:true,nextCursorFingerprint:digest(privateValues[2]),
        ...(acknowledged?{artifactReceipt:String(pins.result)}:{})}]},
    discoveryReviews:[{schemaVersion:1,provider:'SOCIALAPIS',runId:logical,query:'synthetic query',pageNumber:1,
      items:[j,second].map((j,itemIndex)=>({itemIndex,providerPostId:j.id,rawContent:j.body,contentHash:digest(j.body),publishedAt:null,
        targetMatching:{status:'INSUFFICIENT_DATA',reasonCodes:['PROFILE_NOT_PROVIDED']}}))}],
    adminSync:{mode:'current',workflowRunId:'30000000008',completedAt:'2026-01-02T00:00:00Z',summaries:[]},
    workflowMetrics:{workflowRunId:logical,actualRequests:1,totalDeductedCredits:1,status:'STOPPED_RESUMABLE'}};
}
function topology(){
  const result=snapshot('COMPLETED_UNCONFIRMED','COMPLETED_UNCONFIRMED',false);
  const originalFinal=snapshot('READY','CONFIRMED',true);
  originalFinal.adminSync={mode:'current',workflowRunId:logical,observationRunId:logical,executingWorkflowRunId:logical,
    completedAt:'2026-01-02T01:00:00Z',observationCount:2,summaries:[{runId:logical,pageNumber:1,inserted:2,unchanged:0}]};
  originalFinal.workflowMetrics={...originalFinal.workflowMetrics,adminSync:{observationCount:2}};
  const syncFinal=snapshot('COMPLETED_UNCONFIRMED','COMPLETED_UNCONFIRMED',false);
  syncFinal.adminSync={...structuredClone(originalFinal.adminSync),mode:'recovery',executingWorkflowRunId:uploader,
    completedAt:'2026-01-02T02:00:00Z',summaries:[{runId:logical,pageNumber:1,inserted:0,unchanged:2}]};
  syncFinal.workflowMetrics={...syncFinal.workflowMetrics,adminSync:{observationCount:2}};
  const claim=snapshot('CLAIMED','RESERVED_AMBIGUOUS_UNTIL_COMPLETED',false);
  claim.orchestration.status='RUNNING';claim.orchestration.stopReason=null;claim.orchestration.pages=[];
  Object.assign(claim.requestLedger[9],{actualRequests:null,creditsDeducted:null});
  delete claim.requestLedger[9].claimArtifactReceipt;delete claim.requestLedger[9].claimArtifactName;
  claim.jobs=[];claim.discoveryReviews=[];
  // Trusted GET port evidence, not user configuration. The live boundary must
  // acquire/validate history; workflow inputs cannot provide these attestations.
  const history={source:'AUTHENTICATED_GITHUB_GET',complete:true,stable:true,sourceProvenanceVerified:true,
    runs:[{id:uploader,status:'completed',runAttempt:1,conclusion:'success',providerSteps:[{status:'completed',conclusion:'skipped'}]},
      {id:'40545015940',status:'completed',runAttempt:1,conclusion:'failure',providerSteps:[{status:'completed',conclusion:'skipped'}]}]};
  return {snapshots:{claim,result,originalFinal,syncFinal},repository:structuredClone(repository),history};
}

// Valid stored/deflated ZIP test bytes, not an archive reader/crypto implementation.
function crc(bytes){let v=0xffffffff;for(const b of bytes){v^=b;for(let i=0;i<8;i++)v=(v>>>1)^((v&1)?0xedb88320:0);}return (v^0xffffffff)>>>0;}
function zip(bytes,{method=0,descriptor=false,name='socialapis-discovery.json.enc',mode=0o100600}={}){
  const n=Buffer.from(name),compressed=method===8?deflateRawSync(bytes):bytes,flags=descriptor?8:0,checksum=crc(bytes);
  const h=Buffer.alloc(30),c=Buffer.alloc(46),d=descriptor?Buffer.alloc(16):Buffer.alloc(0),end=Buffer.alloc(22);
  h.writeUInt32LE(0x04034b50);h.writeUInt16LE(20,4);h.writeUInt16LE(flags,6);h.writeUInt16LE(method,8);h.writeUInt16LE(n.length,26);
  if(!descriptor){h.writeUInt32LE(checksum,14);h.writeUInt32LE(compressed.length,18);h.writeUInt32LE(bytes.length,22);}
  if(descriptor){d.writeUInt32LE(0x08074b50);d.writeUInt32LE(checksum,4);d.writeUInt32LE(compressed.length,8);d.writeUInt32LE(bytes.length,12);}
  c.writeUInt32LE(0x02014b50);c.writeUInt16LE(0x0314,4);c.writeUInt16LE(20,6);c.writeUInt16LE(flags,8);c.writeUInt16LE(method,10);
  c.writeUInt32LE(checksum,16);c.writeUInt32LE(compressed.length,20);c.writeUInt32LE(bytes.length,24);c.writeUInt16LE(n.length,28);c.writeUInt32LE((mode*65536)>>>0,38);
  const centralOffset=h.length+n.length+compressed.length+d.length;end.writeUInt32LE(0x06054b50);end.writeUInt16LE(1,8);end.writeUInt16LE(1,10);
  end.writeUInt32LE(c.length+n.length,12);end.writeUInt32LE(centralOffset,16);
  return Buffer.concat([h,n,compressed,d,c,n,end]);
}
function fixture(mutate=()=>{},zipOptions={}){
  const input=topology();mutate(input);
  const archives=new Map(),artifacts=[];input.archiveDigests={};
  for(const role of roles){
    const bytes=zip(encryptCheckpoint(Buffer.from(JSON.stringify(input.snapshots[role])),key),zipOptions);
    const id=pins[role];archives.set(id,bytes);input.archiveDigests[id]=`sha256:${digest(bytes)}`;
    artifacts.push({id,name:role==='claim'?`socialapis-page-${logical}-1-claim`:role==='result'?`socialapis-page-${logical}-1-result`:'socialapis-discovery-state',
      expired:false,size_in_bytes:bytes.length,digest:input.archiveDigests[id],
      created_at:role==='originalFinal'?'2026-01-02T01:01:00Z':role==='syncFinal'?'2026-01-02T02:01:00Z':'2026-01-02T00:01:00Z',
      workflow_run:{id:Number(role==='syncFinal'?uploader:logical),repository_id:repository.id}});
  }
  input.artifacts=artifacts;
  const calls=[];
  const github={getRepository:async()=>{calls.push(['GET_REPOSITORY']);return structuredClone(input.repository);},
    getArtifact:async id=>{calls.push(['GET_METADATA',id]);return structuredClone(input.artifacts.find(a=>a.id===id));},
    downloadArtifact:async id=>{calls.push(['GET_ARCHIVE',id]);assert.ok(roles.some(r=>pins[r]===id),'fixed download targets only');return Buffer.from(archives.get(id));},
    getHistoryEvidence:async()=>{calls.push(['GET_HISTORY']);return structuredClone(input.history);}};
  return {input,archives,calls,github};
}
function positive(){const r=compare(fixture().input);assert.equal(r.ok,true,'complete valid synthetic topology must be supported');
  assert.equal(r.eligibility,'YES');privacy(r);return r;}
async function transportPositive(){const f=fixture(),r=await inspect({github:f.github,key});assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');privacy(r);}
function notEligible(r){assert.notEqual(r.eligibility,'YES');privacy(r);}

test('CONTROL: synthetic crypto and archive framing work without production data or mutation helpers',()=>{
  const s=topology(),bytes=Buffer.from(JSON.stringify(s.snapshots.result)),encrypted=encryptCheckpoint(bytes,key);
  assert.deepEqual(decryptCheckpoint(encrypted,key),bytes);assert.equal(zip(encrypted).readUInt32LE(0),0x04034b50);
  assert.equal(s.snapshots.result.orchestration.phase,'COMPLETED_UNCONFIRMED');assert.equal(s.snapshots.originalFinal.orchestration.phase,'READY');
  assert.equal(s.snapshots.claim.requestLedger[9].claimArtifactReceipt,undefined);assert.equal(blockedCalls,0);
});

test('T01: exactly four pinned artifacts accepted through GET-only boundary',async()=>{
  const f=fixture(),r=await inspect({github:f.github,key});assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');
  assert.deepEqual(f.calls.filter(c=>c[0]==='GET_METADATA').map(c=>c[1]).sort(),Object.values(pins).sort());
  assert.deepEqual(f.calls.filter(c=>c[0]==='GET_ARCHIVE').map(c=>c[1]).sort(),Object.values(pins).sort());privacy(r);
});
for(const role of roles)for(const [name,change] of [
  ['wrong id',a=>a.id++],['wrong name',a=>a.name='wrong'],['wrong uploader',a=>a.workflow_run.id++],
  ['wrong repository',a=>a.workflow_run.repository_id++],['expired',a=>a.expired=true],['missing digest',a=>delete a.digest]
])test(`T01: ${role} ${name} cannot become eligible`,()=>{
  positive();const f=fixture();change(f.input.artifacts.find(a=>a.id===pins[role]));notEligible(compare(f.input));
});
for(const kind of ['missing','duplicate','fifth'])test(`T01: ${kind} artifact set rejected`,()=>{
  positive();const f=fixture();if(kind==='missing')f.input.artifacts.pop();
  if(kind==='duplicate')f.input.artifacts.push(structuredClone(f.input.artifacts[0]));
  if(kind==='fifth')f.input.artifacts.push({...f.input.artifacts[0],id:123456});
  notEligible(compare(f.input));
});
test('T01: actual archive digest mismatch rejects despite valid-looking metadata',async()=>{
  await transportPositive();const f=fixture();f.archives.get(pins.result)[10]^=1;
  const r=await inspect({github:f.github,key});assert.equal(r.ok,false);assert.equal(r.errorCode,'ARTIFACT_DIGEST_MISMATCH');privacy(r);
});

for(const options of [{method:0},{method:8},{method:8,descriptor:true}])test(`T02: valid archive ${JSON.stringify(options)} is inspected`,async()=>{
  const f=fixture(()=>{},options),r=await inspect({github:f.github,key});assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');privacy(r);
});
for(const mode of ['missing key','wrong key','corrupt authenticated payload','truncated ZIP','oversize','traversal','symlink'])test(`T02: ${mode} fails closed`,async()=>{
  await transportPositive();const f=fixture();let actualKey=key;
  if(mode==='missing key')actualKey=undefined;if(mode==='wrong key')actualKey='ad'.repeat(32);
  if(mode==='corrupt authenticated payload'){const b=encryptCheckpoint(Buffer.from(JSON.stringify(f.input.snapshots.result)),key);b[b.length-1]^=1;f.archives.set(pins.result,zip(b));}
  if(mode==='truncated ZIP')f.archives.set(pins.result,Buffer.from('PK'));
  if(mode==='oversize')f.archives.set(pins.result,zip(Buffer.alloc(12*1024*1024+33)));
  if(mode==='traversal')f.archives.set(pins.result,zip(encryptCheckpoint(Buffer.from('{}'),key),{name:'../socialapis-discovery.json.enc'}));
  if(mode==='symlink')f.archives.set(pins.result,zip(encryptCheckpoint(Buffer.from('{}'),key),{mode:0o120777}));
  // Keep transport digest truthful so archive/GCM guards are actually reached.
  if(!mode.includes('key')){const a=f.input.artifacts.find(a=>a.id===pins.result);a.digest=`sha256:${digest(f.archives.get(pins.result))}`;a.size_in_bytes=f.archives.get(pins.result).length;}
  const r=await inspect({github:f.github,key:actualKey});assert.equal(r.ok,false);privacy(r);
});

test('T03: report observed three-snapshot phase/outcome, not expected defaults',()=>{
  const r=positive();assert.deepEqual(r.snapshots.map(s=>[s.role,s.phase,s.activeOutcome]),[
    ['result','COMPLETED_UNCONFIRMED','COMPLETED_UNCONFIRMED'],['originalFinal','READY','CONFIRMED'],['syncFinal','COMPLETED_UNCONFIRMED','COMPLETED_UNCONFIRMED']]);
  assert.ok(r.snapshots.every(s=>s.workflowRunId===logical&&s.attemptIndex===9));
});
test('T03: original final that remained unacknowledged is reported, never assumed READY',()=>{
  positive();const f=fixture(x=>x.snapshots.originalFinal=structuredClone(x.snapshots.result));const r=compare(f.input);
  assert.equal(r.snapshots.find(s=>s.role==='originalFinal').phase,'COMPLETED_UNCONFIRMED');notEligible(r);
});
for(const [name,change] of [
  ['active index',s=>s.orchestration.attemptIndex=8],['logical run',s=>s.orchestration.workflowRunId='99999999999'],
  ['page number',s=>s.orchestration.pages[0].pageNumber=2],['last page run',s=>s.orchestration.pages[0].workflowRunId='99999999999'],
  ['claim receipt',s=>s.requestLedger[9].claimArtifactReceipt='999999'],['result receipt',s=>s.orchestration.pages[0].artifactReceipt='999999'],
  ['ledger query',s=>s.requestLedger[9].query='different synthetic query'],['extra current reservation',s=>s.requestLedger.push({...s.requestLedger[9]})]
])test(`T04: conflicting ${name} rejects attempt binding`,()=>{
  positive();const r=compare(fixture(x=>change(x.snapshots.originalFinal)).input);assert.equal(r.eligibility,'NO');privacy(r);
});
test('T04: absent legacy claim binding is UNKNOWN, not invented from co-occurrence',()=>{
  positive();const f=fixture(x=>{for(const role of ['result','originalFinal','syncFinal']){
    delete x.snapshots[role].requestLedger[9].claimArtifactReceipt;delete x.snapshots[role].requestLedger[9].claimArtifactName;}});
  const r=compare(f.input);assert.equal(r.eligibility,'NOT_YET_DETERMINABLE');privacy(r);
});
test('T04: CLAIM must describe the same reserved attempt, not another query',()=>{
  positive();const r=compare(fixture(x=>x.snapshots.claim.requestLedger[9].query='another synthetic query').input);
  assert.equal(r.eligibility,'NO');privacy(r);
});

test('T05: entire protected historical ledger, terminal record and accounting are equal',()=>{
  const r=positive();assert.equal(r.checks.historicalLedgerEqual,true);assert.equal(r.checks.terminalReconciliationsEqual,true);
  assert.equal(r.checks.accountingEqual,true);assert.ok(r.snapshots.every(s=>s.actualRequests===10&&s.creditsDeducted===10));
});
for(const [name,change] of [
  ['old request count',s=>s.requestLedger[0].actualRequests=2],['unknown old credits',s=>s.requestLedger[2].creditsDeducted=null],
  ['numeric string credit',s=>s.requestLedger[3].creditsDeducted='1'],['historical outcome',s=>s.requestLedger[0].outcome='FAILED_OR_AMBIGUOUS'],
  ['terminal record',s=>s.terminalReconciliations[0].artifactId='999999'],['missing terminal record',s=>s.terminalReconciliations=[]],
  ['hidden ledger field',s=>s.requestLedger[1].hidden={value:'changed'}],['reordered old rows',s=>[s.requestLedger[0],s.requestLedger[1]]=[s.requestLedger[1],s.requestLedger[0]]],
  ['missing old row',s=>s.requestLedger.splice(0,1)],['extra old row',s=>s.requestLedger.push({...s.requestLedger[0]})]
])test(`T05: ${name} cannot hide behind current row 9`,()=>{
  positive();const r=compare(fixture(x=>change(x.snapshots.originalFinal)).input);assert.equal(r.eligibility,'NO');privacy(r);
});
for(const field of ['actualRequests','creditsDeducted'])test(`T05: ${field} unknown in RESULT is not ACK-eligible`,()=>{
  positive();const r=compare(fixture(x=>x.snapshots.result.requestLedger[9][field]=null).input);notEligible(r);
});

test('T06: jobs, historicalJobs and all review observations agree by full-content fingerprints',()=>{
  const r=positive();assert.equal(r.checks.jobsEqual,true);assert.equal(r.checks.historicalJobsEqual,true);assert.equal(r.checks.observationsEqual,true);
});
for(const [name,change] of [
  ['job identity',s=>s.jobs[0].sourceIdentity='SOCIALAPIS:9999'],['job hash',s=>s.jobs[0].contentHash='0'.repeat(64)],
  ['raw job body',s=>s.jobs[0].body+=' altered'],['historical job',s=>s.historicalJobs[0].body+=' altered'],
  ['raw observation',s=>s.discoveryReviews[0].items[0].rawContent+=' altered'],
  ['observation hash',s=>s.discoveryReviews[0].items[0].contentHash='0'.repeat(64)],
  ['missing job',s=>s.jobs.pop()],['duplicate job',s=>s.jobs.push({...s.jobs[0]})],['job reordering',s=>s.jobs.reverse()],
  ['hidden nested decision',s=>s.discoveryReviews[0].items[0].targetMatching.reasonCodes=['changed']],
  ['different run observation',s=>s.discoveryReviews.push({...s.discoveryReviews[0],runId:'99999999999'})]
])test(`T06: ${name} detected in full protected content`,()=>{
  positive();const r=compare(fixture(x=>change(x.snapshots.originalFinal)).input);assert.equal(r.eligibility,'NO');privacy(r);
});
test('T06: consistently tampered contentHash must be rejected, not merely equal across snapshots',()=>{
  positive();const f=fixture(x=>{for(const role of ['result','originalFinal','syncFinal'])x.snapshots[role].discoveryReviews[0].items[0].contentHash='0'.repeat(64);});
  assert.equal(compare(f.input).eligibility,'NO');
});
test('T06: consistently invalid job hashes cannot be legitimized by cross-snapshot equality',()=>{
  positive();const f=fixture(x=>{for(const role of ['result','originalFinal','syncFinal'])x.snapshots[role].jobs[0].contentHash='0'.repeat(64);});
  assert.equal(compare(f.input).eligibility,'NO');
});
test('T06: object insertion order changes state fingerprint but not versioned canonical equality',()=>{
  positive();const f=fixture(x=>{const j=x.snapshots.originalFinal.jobs[0];x.snapshots.originalFinal.jobs[0]=Object.fromEntries(Object.entries(j).reverse());});
  const r=compare(f.input);assert.equal(r.eligibility,'YES');assert.equal(r.checks.jobsEqual,true);
  assert.equal(r.fingerprintVersion,'CANONICAL_JSON_V1');privacy(r);
});

test('T07: only four exact ACK paths, Admin Sync and derived metrics are classified separately',()=>{
  const r=positive();assert.deepEqual([...r.comparisons.resultToOriginalFinal.ackPaths].sort(),[...ackPaths].sort());
  assert.equal(r.comparisons.resultToOriginalFinal.adminSyncChanged,true);assert.equal(r.comparisons.resultToOriginalFinal.metricsChanged,true);
  assert.equal(r.comparisons.resultToOriginalFinal.unclassifiedChanges,0);
  assert.equal(r.comparisons.resultToSyncFinal.ackPaths.length,0);
});
for(const [name,change] of [
  ['nested orchestration',s=>s.orchestration.config.hidden='changed'],['unknown top-level',s=>s.unrecognizedData={nested:'changed'}],
  ['unknown nested sync field',s=>s.adminSync.unrecognized={ledger:{actualRequests:0}}],
  ['unknown nested metrics field',s=>s.workflowMetrics.unrecognized={rawBody:privateValues[0]}],
  ['hostile property name',s=>s[privateValues[1]]={value:privateValues[3]}]
])test(`T07: ${name} is not broadly exempted as metadata or ACK`,()=>{
  positive();const r=compare(fixture(x=>change(x.snapshots.originalFinal)).input);
  assert.equal(r.eligibility,'NOT_YET_DETERMINABLE');assert.ok(r.comparisons.resultToOriginalFinal.unclassifiedChanges>0);privacy(r);
});
test('T07: per-role JSON and ZIP fingerprints are not confused with semantic equality',()=>{
  const one=fixture(),two=fixture();assert.notEqual(one.input.archiveDigests[pins.result],two.input.archiveDigests[pins.result]);
  const a=compare(one.input),b=compare(two.input);assert.equal(a.ok,true);assert.equal(b.ok,true);
  assert.equal(a.protectedFingerprint,b.protectedFingerprint);assert.equal(a.eligibility,'YES');privacy(a);privacy(b);
});

for(const [name,change] of [
  ['missing history',x=>delete x.history],['incomplete pagination',x=>x.history.complete=false],['concurrent history change',x=>x.history.stable=false],
  ['source not verified',x=>x.history.sourceProvenanceVerified=false],['untrusted evidence source',x=>x.history.source='OPERATOR_BOOLEAN'],
  ['pending run',x=>x.history.runs[0].status='in_progress'],['missing provider steps',x=>delete x.history.runs[0].providerSteps],
  ['unverified rerun',x=>x.history.runs[0].runAttempt=2],
  ['provider started',x=>x.history.runs[0].providerSteps[0].conclusion='success'],['ambiguous provider',x=>x.history.runs[0].providerSteps[0].conclusion=null]
])test(`T08: ${name} cannot authorize an older final checkpoint`,()=>{
  positive();const r=compare(fixture(change).input);assert.equal(r.eligibility,'NOT_YET_DETERMINABLE');privacy(r);
});
test('T08: overall run success alone is not zero-provider evidence',()=>{
  positive();const f=fixture(x=>{x.history.runs[0].conclusion='success';x.history.runs[0].providerSteps=[];});
  assert.equal(compare(f.input).eligibility,'NOT_YET_DETERMINABLE');
});
test('T08: later sync snapshot with a new provider attempt cannot be silently rolled back',()=>{
  positive();const f=fixture(x=>x.snapshots.syncFinal.requestLedger.push({workflowRunId:'40555000000',actualRequests:1,creditsDeducted:1,outcome:'CONFIRMED'}));
  notEligible(compare(f.input));
});

test('T09: YES is evidence-only, leaves all snapshots/receipts and counters immutable',()=>{
  const f=fixture(),before=structuredClone(f.input),r=compare(f.input);assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');
  assert.deepEqual(f.input,before);assert.equal(blockedCalls,0);privacy(r);
});
test('T09: malformed snapshot fails closed instead of creating an empty state',()=>{
  positive();const f=fixture();f.input.snapshots.result=null;const r=compare(f.input);assert.equal(r.ok,false);notEligible(r);
});
test('T09: oversized history cannot leak, silently truncate comparison, or authorize incomplete evidence',()=>{
  positive();const f=fixture(x=>{for(const role of ['result','originalFinal','syncFinal'])
    x.snapshots[role].requestLedger.push(...Array.from({length:1000},(_,i)=>({workflowRunId:String(31000000000+i),actualRequests:1,creditsDeducted:1,outcome:'CONFIRMED'})));});
  // The deliberately enlarged history is not a fully bound active-index-9
  // topology. Rejecting it before projection is valid; if comparisons are
  // returned, they must not pretend that only the first ten rows were checked.
  const r=compare(f.input);notEligible(r);
  if(r.ok)assert.equal(r.ledgerRowsCompared,1010,'no silent ledger truncation');
  else assert.match(r.errorCode,/^[A-Z][A-Z_]+$/u,'fixed metadata-only failure code');
  privacy(r);
});

test('T10: private raw values, query, cursor and hostile exceptions never become output',async()=>{
  positive();await transportPositive();const f=fixture();f.github.getArtifact=async()=>{throw Error(privateValues.join(' '));};
  const r=await inspect({github:f.github,key});assert.equal(r.ok,false);privacy(r);
});
test('T10: arbitrary artifact and URL overrides cannot redirect the pinned diagnostic',async()=>{
  await transportPositive();const f=fixture();
  const r=await inspect({github:f.github,key,artifactIds:[123],baseUrl:'https://untrusted.invalid'});
  assert.equal(r.ok,true);assert.equal(r.eligibility,'YES');
  assert.ok(f.calls.filter(c=>c[0]==='GET_ARCHIVE').every(c=>Object.values(pins).includes(c[1])));privacy(r);
});
for(const location of ['https://untrusted.invalid/payload','https://synthetic.blob.core.windows.net/payload'])test(`T10: injected HTTP redirect boundary ${location.includes('untrusted')?'rejects untrusted':'strips storage authorization'}`,async()=>{
  await transportPositive();const f=fixture(),calls=[];
  const transport=async(url,options)=>{
    calls.push({url:String(url),options});assert.equal(options.method,'GET');assert.equal(options.credentials,'omit');
    if(String(url).startsWith('https://api.github.com/repos/CoderPL1005/portfolio')){
      assert.equal(options.redirect,'manual');assert.equal(options.headers.Authorization,`Bearer ${token}`);
      if(String(url).endsWith('/portfolio'))return Response.json(repository);
      const m=String(url).match(/\/artifacts\/(\d+)(\/zip)?$/u);
      if(m){const id=Number(m[1]);assert.ok(Object.values(pins).includes(id));
        return m[2]?new Response(null,{status:302,headers:{location}}):Response.json(f.input.artifacts.find(a=>a.id===id));}
      // Trusted history-reader endpoints are GET-only; a test fixture port can
      // also supply already validated history to avoid inventing REST schemas.
      throw Error('UNEXPECTED_READ_PATH');
    }
    assert.equal(String(url),location);assert.equal(options.headers,undefined);assert.equal(options.redirect,'error');
    return new Response('not a ZIP');
  };
  const r=await inspectNetwork({token,key,transport,historyReader:async()=>structuredClone(f.input.history)});
  assert.equal(r.ok,false);privacy(r);
  assert.ok(calls.length>0,'missing implementation cannot pass a rejection test');
  const external=calls.filter(c=>!c.url.startsWith('https://api.github.com/'));
  assert.equal(external.length,location.includes('untrusted')?0:1);
  assert.equal(calls.filter(c=>c.url.endsWith('/zip')).length,1,'no retry or next artifact after failure');
});
test('T10: no key means zero HTTP, no empty-state fallback',async()=>{
  await transportPositive();let calls=0;const r=await inspectNetwork({token,key:undefined,transport:async()=>{calls++;throw Error();}});
  assert.equal(r.ok,false);assert.equal(calls,0);privacy(r);
});

test('T11: new workflow is manual-only, read-only, no arbitrary target inputs or provider secrets',()=>{
  const path=new URL('../../../.github/workflows/discovery-ack-checkpoint-verification.yml',import.meta.url);
  const workflow=fs.existsSync(path)?parseYaml(fs.readFileSync(path,'utf8')):{};
  assert.deepEqual(Object.keys(workflow.on??{}),['workflow_dispatch'],'comparison workflow behavior not implemented');
  assert.equal(workflow.on.workflow_dispatch?.inputs,undefined);assert.deepEqual(workflow.permissions,{contents:'read',actions:'read'});
  const jobs=Object.values(workflow.jobs??{});assert.equal(jobs.length,1);const j=jobs[0];
  assert.equal(j['runs-on'],'ubuntu-latest');assert.ok(j['timeout-minutes']<=5);assert.match(j.if,/refs\/heads\/main/u);
  assert.equal(j.env?.DISCOVERY_CHECKPOINT_KEY,undefined);assert.equal(workflow.env?.DISCOVERY_CHECKPOINT_KEY,undefined);
  assert.equal(j.steps.filter(s=>s.env?.DISCOVERY_CHECKPOINT_KEY).length,1);
  assert.equal(j.steps.find(s=>s.uses?.startsWith('actions/checkout@'))?.with?.['persist-credentials'],false);
  const source=JSON.stringify(workflow);
  assert.doesNotMatch(source,/upload-artifact|actions\/cache|SOCIALAPIS_API_KEY|DISCOVERY_IMPORT_KEY|createRef|--orchestrate|continue-on-error|schedule/iu);
});
test('T11: old fixed-target diagnostic and approved test bytes remain intact',()=>{
  const bytes=fs.readFileSync(new URL('./checkpoint-inspection.test.mjs',import.meta.url));assert.equal(digest(bytes),expectedInspectionHash);
  const old=fs.readFileSync(new URL('../../../.github/workflows/discovery-checkpoint-inspection.yml',import.meta.url),'utf8');
  assert.equal(parseYaml(old).on.workflow_dispatch?.inputs,undefined);
  assert.equal(typeof implementation.inspectCheckpoint,'function');assert.equal(typeof implementation.inspectArtifact,'function');
});
test('T12: implementation reuses audited archive/crypto primitives, cannot call operational mutation helpers',()=>{
  const source=fs.readFileSync(moduleUrl,'utf8');assert.ok(/export (?:async )?function compareAcknowledgedCheckpoints/u.test(source),'comparison behavior not implemented');
  assert.match(source,/decryptCheckpoint/u);assert.match(source,/ciphertextFromZip/u);
  assert.doesNotMatch(source,/createFreshSession|acknowledgePersistence|reconcileTerminalFailure|syncReviews|claimPage|beginExecution|child_process|writeFile|appendFile|createWriteStream/);
  assert.doesNotMatch(source,/process\.(?:stdout|stderr)/u,'return metadata to the audited workflow, never dump raw streams');
});
test('CONTROL: zero live calls and no original diagnostic-test edits',()=>{
  assert.equal(blockedCalls,0);assert.equal(digest(fs.readFileSync(new URL('./checkpoint-inspection.test.mjs',import.meta.url))),expectedInspectionHash);
});
