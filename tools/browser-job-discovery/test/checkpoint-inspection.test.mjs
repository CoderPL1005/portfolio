import test, {mock} from 'node:test';
import assert from 'node:assert/strict';
import {existsSync, readFileSync} from 'node:fs';
import fs from 'node:fs';
import fsPromises from 'node:fs/promises';
import {syncBuiltinESMExports} from 'node:module';
import {createHash} from 'node:crypto';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import childProcess from 'node:child_process';
import {parse as parseYaml} from 'yaml';
import {encryptCheckpoint, decryptCheckpoint} from '../src/admin-sync.mjs';

// Proposed test-facing contract, not an implementation or a real checkpoint.
// Missing module produces ordinary behavior assertions below, never an import
// failure. Unexpected errors inside an existing module are NOT swallowed.
let forbiddenCalls=0;
const forbidden=()=>{forbiddenCalls++;throw new Error('OFFLINE_TEST_FORBIDS_EXTERNAL_SIDE_EFFECT');};
mock.method(globalThis,'fetch',forbidden);
mock.method(http,'request',forbidden);mock.method(http,'get',forbidden);
mock.method(https,'request',forbidden);mock.method(https,'get',forbidden);
mock.method(net.Socket.prototype,'connect',forbidden);
for(const method of ['spawn','spawnSync','exec','execSync','execFile','execFileSync','fork'])mock.method(childProcess,method,forbidden);
for(const method of ['writeFile','appendFile','rename','unlink','rm','mkdir','rmdir','copyFile','truncate','symlink','link','chmod','chown']){
  mock.method(fs,method,forbidden);mock.method(fs,`${method}Sync`,forbidden);mock.method(fsPromises,method,forbidden);
}
mock.method(fs,'createWriteStream',forbidden);
syncBuiltinESMExports();
const emitted=[];
for(const method of ['log','error','warn','info','debug'])mock.method(console,method,(...args)=>emitted.push(args.map(x=>typeof x==='string'?x:JSON.stringify(x)).join(' ')));
test.afterEach(()=>{
  const output=emitted.splice(0).join('\n');
  for(const marker of [...sensitive,secret])assert.ok(!output.includes(marker),'stdout/stderr must not leak private fixture values');
  assert.ok(Buffer.byteLength(output)<=16*1024,'console diagnostics must be bounded');
  assert.equal(forbiddenCalls,0,'no network, child process or filesystem mutation attempted');
});
test.after(()=>mock.restoreAll());
const moduleUrl=new URL('../src/checkpoint-inspection.mjs',import.meta.url);
const implementation=existsSync(moduleUrl)?await import(moduleUrl.href):{};
const unavailable=()=>({ok:false,errorCode:'INSPECTION_NOT_IMPLEMENTED'});
const inspectCheckpoint=implementation.inspectCheckpoint??unavailable;
const inspectArtifact=implementation.inspectArtifact??unavailable;
const logical='40482593764',uploader='40488676716',artifactId=12725991974;
const secret='ab'.repeat(32); // Synthetic test key, never an environment secret.
const sensitive=['SYNTHETIC_PRIVATE_JD','SYNTHETIC_SECRET','private-person@example.invalid','PRIVATE_CURSOR_SENTINEL'];
const hash=s=>createHash('sha256').update(JSON.stringify(s)).digest('hex');
const metadata=()=>({id:artifactId,name:'socialapis-discovery-state',expired:false,
  workflow_run:{id:Number(uploader),repository_id:123},size_in_bytes:4096});
const repository=()=>({id:123,full_name:'CoderPL1005/portfolio'});
function state(){
  return {jobs:[],requestLedger:[{workflowRunId:'40216232001',outcome:'CONFIRMED',actualRequests:1,creditsDeducted:1},
    {workflowRunId:logical,query:'synthetic query',pageNumber:1,outcome:'CONFIRMED',actualRequests:1,creditsDeducted:1}],
  terminalReconciliations:[],orchestration:{phase:'READY',status:'STOPPED_TERMINAL',stopReason:'TARGET_REACHED',workflowRunId:logical,attemptIndex:1,pages:[]},
  discoveryReviews:[{schemaVersion:1,provider:'SOCIALAPIS',runId:logical,query:'synthetic query',pageNumber:1,
    items:[{rawContent:sensitive[0],providerPostId:'7001'}]}],
  adminSync:{mode:'recovery',workflowRunId:logical,observationRunId:logical,executingWorkflowRunId:uploader,
    completedAt:'2026-10-27T00:00:00Z',observationCount:1,
    summaries:[{runId:logical,pageNumber:1,inserted:0,unchanged:1}]}};
}
const options=s=>({artifact:metadata(),repository:repository(),stateFingerprint:hash(s)});
function inspect(s=state(),o=options(s)){return inspectCheckpoint(s,o);}
function safe(result){
  const text=JSON.stringify(result);
  for(const marker of [...sensitive,secret])assert.ok(!text.includes(marker),'metadata must not contain synthetic private values');
  assert.ok(Buffer.byteLength(text)<=16*1024,'bounded metadata, including errors');
}
function validControl(){const result=inspect();assert.equal(result.ok,true,'valid synthetic checkpoint must be inspectable');return result;}
function failure(result,code){assert.equal(result.ok,false);assert.equal(result.errorCode,code);safe(result);}

// Real stored ZIP structure with CRC32, not a made-up archive descriptor.
function crc32(bytes){let crc=0xffffffff;for(const byte of bytes){crc^=byte;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return (crc^0xffffffff)>>>0;}
function zip(entries){
  const locals=[],centrals=[];let offset=0;
  for(const {name,bytes,mode=0o100600} of entries){
    const n=Buffer.from(name),local=Buffer.alloc(30),central=Buffer.alloc(46),crc=crc32(bytes);
    local.writeUInt32LE(0x04034b50);local.writeUInt16LE(20,4);local.writeUInt32LE(crc,14);
    local.writeUInt32LE(bytes.length,18);local.writeUInt32LE(bytes.length,22);local.writeUInt16LE(n.length,26);
    central.writeUInt32LE(0x02014b50);central.writeUInt16LE(0x0314,4);central.writeUInt16LE(20,6);
    central.writeUInt32LE(crc,16);central.writeUInt32LE(bytes.length,20);central.writeUInt32LE(bytes.length,24);
    central.writeUInt16LE(n.length,28);central.writeUInt32LE((mode*65536)>>>0,38);central.writeUInt32LE(offset,42);
    locals.push(local,n,bytes);centrals.push(central,n);offset+=local.length+n.length+bytes.length;
  }
  const cd=Buffer.concat(centrals),end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50);
  end.writeUInt16LE(entries.length,8);end.writeUInt16LE(entries.length,10);end.writeUInt32LE(cd.length,12);end.writeUInt32LE(offset,16);
  return Buffer.concat([...locals,cd,end]);
}
function encrypted(s=state()){return encryptCheckpoint(Buffer.from(JSON.stringify(s)),secret);}
function artifactFixture({artifact=metadata(),archive=zip([{name:'socialapis-discovery.json.enc',bytes:encrypted()}]),key=secret}={}){
  const calls=[];
  // Only these two read capabilities exist. Real network is blocked process-wide.
  const github={async getArtifact(id){calls.push(['GET_METADATA',id]);return structuredClone(artifact);},
    async downloadArtifact(id){calls.push(['GET_ARCHIVE',id]);return Buffer.from(archive);}};
  return {calls,run:()=>inspectArtifact({github,repository:repository(),key})};
}

test('CONTROL: synthetic archive/crypto fixture is local and has authentic ZIP framing',()=>{
  const bytes=encrypted(),archive=zip([{name:'socialapis-discovery.json.enc',bytes}]);
  assert.equal(archive.readUInt32LE(0),0x04034b50);assert.equal(archive.readUInt32LE(archive.length-22),0x06054b50);
  assert.equal(forbiddenCalls,0);assert.deepEqual(JSON.parse(decryptCheckpoint(bytes,secret).toString()),state());
});

test('T01: exact artifact and uploader accepted using read-only GitHub boundary',async()=>{
  const f=artifactFixture(),result=await f.run();assert.equal(result.ok,true);safe(result);
  assert.deepEqual(f.calls,[['GET_METADATA',artifactId],['GET_ARCHIVE',artifactId]]);
  assert.equal(result.artifact.id,String(artifactId));assert.equal(result.artifact.uploaderRunId,uploader);
});
test('T01: absent artifact is rejected without download',async()=>{
  validControl();const f=artifactFixture({artifact:null});failure(await f.run(),'ARTIFACT_PROVENANCE_INVALID');
  assert.deepEqual(f.calls,[['GET_METADATA',artifactId]]);
});
for(const [name,change] of [
  ['wrong ID',m=>m.id++],['wrong uploader',m=>m.workflow_run.id++],['wrong name',m=>m.name='untrusted-artifact'],
  ['wrong repository',m=>m.workflow_run.repository_id=999],['expired',m=>m.expired=true],['missing run',m=>delete m.workflow_run]
])test(`T01: reject ${name} before archive/decryption`,async()=>{
  validControl();const m=metadata();change(m);const f=artifactFixture({artifact:m});
  failure(await f.run(),'ARTIFACT_PROVENANCE_INVALID');assert.deepEqual(f.calls,[['GET_METADATA',artifactId]]);
});

for(const [name,entries] of [
  ['multiple files',()=>[{name:'socialapis-discovery.json.enc',bytes:encrypted()},{name:'extra',bytes:Buffer.from('x')}]],
  ['traversal',()=>[{name:'../socialapis-discovery.json.enc',bytes:encrypted()}]],
  ['symlink',()=>[{name:'socialapis-discovery.json.enc',bytes:encrypted(),mode:0o120777}]],
  ['plaintext',()=>[{name:'socialapis-discovery.json',bytes:Buffer.from('{}')}]],
  ['oversize ciphertext',()=>[{name:'socialapis-discovery.json.enc',bytes:Buffer.alloc(12*1024*1024+33)}]]
])test(`T02: reject archive ${name}`,async()=>{
  validControl();failure(await artifactFixture({archive:zip(entries())}).run(),'CHECKPOINT_ARCHIVE_INVALID');
});
test('T02: reject truncated ZIP without any fallback',async()=>{
  validControl();failure(await artifactFixture({archive:Buffer.from('PK')}).run(),'CHECKPOINT_ARCHIVE_INVALID');
});
test('T03: actual synthetic encryption round-trip through inspector',async()=>{
  const f=artifactFixture();assert.equal((await f.run()).ok,true);assert.equal(forbiddenCalls,0);
});
for(const key of [undefined,'cd'.repeat(32)])test(`T03: missing/wrong synthetic key ${key?'wrong':'missing'} fails safely`,async()=>{
  validControl();const f=artifactFixture();
  const result=await inspectArtifact({github:{getArtifact:async()=>metadata(),downloadArtifact:async()=>zip([{name:'socialapis-discovery.json.enc',bytes:encrypted()}])},repository:repository(),key});
  failure(result,'CHECKPOINT_DECRYPTION_FAILED');assert.deepEqual(f.calls,[]);
});
test('T03: corrupt authenticated ciphertext fails safely',async()=>{
  validControl();const bytes=encrypted();bytes[bytes.length-1]^=1;
  failure(await artifactFixture({archive:zip([{name:'socialapis-discovery.json.enc',bytes}])}).run(),'CHECKPOINT_DECRYPTION_FAILED');
});

for(const phase of ['READY','TERMINAL_RECONCILED'])test(`T04: ${phase} fully closed ledger has no ambiguity`,()=>{
  const s=state();s.orchestration.phase=phase;const before=structuredClone(s),r=inspect(s);
  assert.equal(r.ok,true);assert.equal(r.diagnosis.guard,null);assert.equal(r.ledger.length,2);
  assert.ok(r.ledger.every(row=>row.closed&&!row.ambiguous));assert.deepEqual(s,before);safe(r);
});
for(const phase of ['CLAIMED','IN_FLIGHT','COMPLETED_UNCONFIRMED','TERMINAL_UNCONFIRMED'])test(`T05: ${phase} phase guard precedes bad historical ledger`,()=>{
  const s=state();s.orchestration.phase=phase;s.requestLedger[0].outcome='FAILED_OR_AMBIGUOUS';const r=inspect(s);
  assert.equal(r.ok,true);assert.equal(r.diagnosis.guard,'CHECKPOINT_PHASE');assert.equal(r.diagnosis.ledgerIndex,null);
  assert.equal(r.diagnosis.errorCode,'FRESH_AUDIT_AMBIGUOUS');assert.equal(r.ledger.length,2);
  assert.equal(r.ledger[0].ambiguous,true);assert.equal(r.orchestration.phase,phase);safe(r);
});
test('T06: scans ALL history and reports first bad global index, not latest logical run',()=>{
  const s=state();s.requestLedger[0].outcome='FAILED_OR_AMBIGUOUS';s.requestLedger.push({...s.requestLedger[1],workflowRunId:'40216232002',creditsDeducted:null});
  const r=inspect(s);assert.equal(r.ok,true);assert.equal(r.ledger.length,3);
  assert.equal(r.diagnosis.guard,'GLOBAL_LEDGER');assert.equal(r.diagnosis.ledgerIndex,0);
  assert.equal(r.ledger[0].workflowRunId,'40216232001');assert.equal(r.ledger[2].ambiguous,true);safe(r);
});
function reconciled(){const s=state();s.requestLedger[0].outcome='FAILED_OR_AMBIGUOUS';
  s.terminalReconciliations=[{attemptIndex:0,workflowRunId:'40216232001',actualRequests:1,creditsDeducted:1,artifactId:'9001',claimArtifactId:'9002'}];return s;}
test('T07: exact matching terminal reconciliation closes row without modifying it',()=>{
  const s=reconciled(),before=structuredClone(s),r=inspect(s);assert.equal(r.ok,true);assert.equal(r.diagnosis.guard,null);
  assert.equal(r.ledger[0].closed,true);assert.equal(r.ledger[0].matchingReconciliations.length,1);
  assert.equal(r.ledger[0].matchingReconciliations[0].artifactId,'9001');assert.deepEqual(s,before);
});
for(const [field,value] of [['attemptIndex',1],['workflowRunId','9999'],['actualRequests',0],['creditsDeducted',0]])test(`T07: mismatch ${field} cannot close historical row`,()=>{
  validControl();const s=reconciled();s.terminalReconciliations[0][field]=value;
  const r=inspect(s);assert.equal(r.ok,true);assert.equal(r.ledger[0].closed,false);assert.equal(r.diagnosis.ledgerIndex,0);
});
for(const [field,value] of [['actualRequests',null],['actualRequests','1'],['actualRequests',0],['actualRequests',2],
  ['creditsDeducted',null],['creditsDeducted','1'],['creditsDeducted',Infinity],['creditsDeducted',-1],['creditsDeducted',2]])test(`T08: ${field}=${String(value)} is ambiguous even with CONFIRMED outcome`,()=>{
  const s=state();s.requestLedger[0][field]=value;const r=inspect(s);assert.equal(r.ok,true);
  assert.equal(r.ledger[0].closed,true);assert.equal(r.ledger[0].ambiguous,true);assert.equal(r.diagnosis.ledgerIndex,0);
  assert.equal(r.ledger[0][field],Number.isFinite(value)?value:null);assert.equal(r.ledger[0][`${field}Type`],typeof value);
  if(field==='creditsDeducted')assert.equal(r.ledger[0].reasons.includes('KNOWN_CREDITS_OUTSIDE_CURRENT_PER_REQUEST_CAP'),Number.isFinite(value)&&(value<0||value>1));
  safe(r);
});
test('T09: sync-only uploader and logical Discovery identity remain distinct',()=>{
  const r=inspect();assert.equal(r.ok,true);assert.equal(r.orchestration.workflowRunId,logical);
  assert.equal(r.artifact.uploaderRunId,uploader);assert.equal(r.diagnosis.guard,null);
});
for(const [name,change] of [
  ['tampered hash',(_s,o)=>o.stateFingerprint='0'.repeat(64)],
  ['wrong logical run',s=>s.orchestration.workflowRunId='9999'],
  ['wrong sync uploader',s=>s.adminSync.executingWorkflowRunId='9999'],
  ['wrong receipt count',s=>s.adminSync.summaries[0].unchanged=0],
  ['missing sync receipt',s=>delete s.adminSync]
])test(`T09: ${name} is upstream evidence failure, not exact ledger diagnosis`,()=>{
  validControl();const s=state();const o=options(s);change(s,o);if(name!=='tampered hash')o.stateFingerprint=hash(s);
  const r=inspect(s,o);failure(r,'FRESH_AUDIT_EVIDENCE_INVALID');assert.notEqual(r.diagnosis?.guard,'GLOBAL_LEDGER');
});

test('T10: hostile metadata values and raw content are never reflected',()=>{
  const s=state();s.orchestration.status=sensitive[0];s.orchestration.stopReason=sensitive[1];
  s.orchestration.cursor=sensitive[3];s.requestLedger[0].outcome=sensitive[2];s.requestLedger[0].workflowRunId=sensitive[1];
  s.terminalReconciliations=[{attemptIndex:0,workflowRunId:sensitive[1],actualRequests:1,creditsDeducted:1,artifactId:sensitive[0]}];
  const r=inspect(s);assert.equal(r.ok,true);safe(r);assert.equal(r.orchestration.status,'UNKNOWN_OR_UNRECOGNIZED');
  assert.equal(r.orchestration.stopReason,'UNKNOWN_OR_UNRECOGNIZED');assert.equal(r.ledger[0].workflowRunId,null);
});
test('T10: raw transport exception is replaced by fixed safe error',async()=>{
  validControl();const r=await inspectArtifact({repository:repository(),key:secret,github:{getArtifact:async()=>{throw new Error(sensitive.join(' '));}}});
  failure(r,'CHECKPOINT_INSPECTION_FAILED');
});
test('T10: output overflow fails closed instead of returning truncated ledger',()=>{
  validControl();const s=state();s.requestLedger.push(...Array.from({length:1000},()=>({...s.requestLedger[0]})));
  failure(inspect(s),'OUTPUT_LIMIT_EXCEEDED');
});

test('T11: workflow is manual-only and has exclusively read permissions',()=>{
  const path=new URL('../../../.github/workflows/discovery-checkpoint-inspection.yml',import.meta.url);
  const workflow=existsSync(path)?parseYaml(readFileSync(path,'utf8')):{};
  assert.deepEqual(Object.keys(workflow.on??{}),['workflow_dispatch'],'manual diagnostic behavior is not implemented yet');
  assert.deepEqual(workflow.permissions,{contents:'read',actions:'read'});
  const jobs=Object.values(workflow.jobs??{});assert.equal(jobs.length,1);
  const job=jobs[0];assert.equal(job['runs-on'],'ubuntu-latest');assert.ok(job['timeout-minutes']<=5);
  assert.equal(job.env?.DISCOVERY_CHECKPOINT_KEY,undefined);assert.equal(workflow.env?.DISCOVERY_CHECKPOINT_KEY,undefined);
  const steps=job.steps,checkout=steps.find(s=>s.uses?.startsWith('actions/checkout@'));
  assert.equal(checkout?.with?.['persist-credentials'],false);
  assert.equal(steps.filter(s=>s.env?.DISCOVERY_CHECKPOINT_KEY).length,1);
  const source=JSON.stringify(workflow);
  assert.doesNotMatch(source,/upload-artifact|actions\/cache|workflow_dispatches|createRef|pagination-cli|socialapis-job-discovery|discovery-demo-once|GITHUB_STEP_SUMMARY/iu);
  assert.doesNotMatch(source,/socialapis\.io|generativelanguage|discovery-sync|continue-on-error|schedule/iu);
  assert.equal(workflow.on.workflow_dispatch?.inputs,undefined,'no arbitrary source/endpoint inputs');
});
test('T11: diagnostic source cannot invoke Fresh/ACK/reconcile/sync/provider or child processes',()=>{
  const source=existsSync(moduleUrl)?readFileSync(moduleUrl,'utf8'):'';
  assert.ok(source.length>0,'read-only diagnostic implementation does not exist');
  assert.doesNotMatch(source,/createFreshSession|acknowledgePersistence|reconcileTerminalFailure|syncReviews|claimPage|beginExecution|child_process|socialapis-adapter|pagination-cli|pagination-orchestrator|writeFile|appendFile|createWriteStream/);
});
for(const [name,change] of [['null ledger',s=>s.requestLedger=null],['null row',s=>s.requestLedger[0]=null],
  ['missing orchestration',s=>delete s.orchestration],['invalid reconciliation',s=>s.terminalReconciliations={}]
])test(`T12: ${name} fails closed without mutation or empty-state fallback`,()=>{
  validControl();const s=state();change(s);const before=structuredClone(s);
  failure(inspect(s),'CHECKPOINT_SCHEMA_INVALID');assert.deepEqual(s,before);assert.equal(forbiddenCalls,0);
});
test('CONTROL: tests made zero real network or child-process calls',()=>assert.equal(forbiddenCalls,0));
