import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {readFileSync,mkdtempSync,writeFileSync,readFileSync as readBytes,rmSync,mkdirSync} from 'node:fs';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
import {parse} from 'yaml';
import {JobPostExtractor} from '../src/core.mjs';
import {encryptCheckpoint,decryptCheckpoint} from '../src/admin-sync.mjs';
import {restoreAcknowledgedFromGitHub,validateRecoveryContent} from '../src/checkpoint-inspection.mjs';
import {RECOVERY_PRODUCERS} from '../src/recovery-source.mjs';

// All payloads/keys/ports are synthetic. Only local PUBLIC source bytes are read.
const root=fileURLToPath(new URL('../',import.meta.url)),logical='40482593764',uploader='40488676716',current='99999999999';
const key='ac'.repeat(32),token='SYNTHETIC_READ_TOKEN',repo='CoderPL1005/portfolio',base=`https://api.github.com/repos/${repo}`;
const source=readFileSync(new URL('../../../.github/workflows/socialapis-job-discovery.yml',import.meta.url),'utf8');
const definitions=parse(source).jobs.discover.steps;
const pins={claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974};
const revision='81e15d15e379d4ea2c27a654a5966d056404efe5',sha=v=>createHash('sha256').update(v).digest('hex');
function crc(bytes){let n=0xffffffff;for(const b of bytes){n^=b;for(let i=0;i<8;i++)n=(n>>>1)^((n&1)?0xedb88320:0);}return(n^0xffffffff)>>>0;}
function zip(bytes){
  const name=Buffer.from('socialapis-discovery.json.enc'),local=Buffer.alloc(30),central=Buffer.alloc(46),end=Buffer.alloc(22),c=crc(bytes);
  local.writeUInt32LE(0x04034b50);local.writeUInt16LE(20,4);local.writeUInt32LE(c,14);local.writeUInt32LE(bytes.length,18);local.writeUInt32LE(bytes.length,22);local.writeUInt16LE(name.length,26);
  central.writeUInt32LE(0x02014b50);central.writeUInt16LE(20,6);central.writeUInt32LE(c,16);central.writeUInt32LE(bytes.length,20);central.writeUInt32LE(bytes.length,24);central.writeUInt16LE(name.length,28);
  end.writeUInt32LE(0x06054b50);end.writeUInt16LE(1,8);end.writeUInt16LE(1,10);end.writeUInt32LE(central.length+name.length,12);end.writeUInt32LE(local.length+name.length+bytes.length,16);
  return Buffer.concat([local,name,bytes,central,name,end]);
}
function snapshots(){
  const input={id:'7001',provider:'SOCIALAPIS',source:'FACEBOOK',sourceUrl:'https://www.facebook.com/7001',evidenceKind:'SYNTHETIC_FIXTURE',title:'',employer:'',location:'',requirements:'',body:'SYNTHETIC PRIVATE RECRUITMENT'};
  const job=new JobPostExtractor().extract(input),ledger=Array.from({length:10},(_,i)=>({workflowRunId:i===9?logical:String(30000000000+i),actualRequests:1,creditsDeducted:1,
    outcome:i===6?'FAILED_OR_AMBIGUOUS':i===9?'COMPLETED_UNCONFIRMED':'CONFIRMED'}));
  Object.assign(ledger[9],{logicalExecutionId:'synthetic',query:'Synthetic query',allocation:'CORE',pageNumber:1,startTime:'2026-01-01',endTime:'2026-01-02',
    claimArtifactReceipt:String(pins.claim),claimArtifactName:`socialapis-page-${logical}-1-claim`});
  const result={evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[job],historicalJobs:[],requestLedger:ledger,
    terminalReconciliations:[{attemptIndex:6,workflowRunId:ledger[6].workflowRunId,actualRequests:1,creditsDeducted:1,artifactId:'6001',claimArtifactId:'6002',stateFingerprint:'1'.repeat(64)}],
    discoveryReviews:[{runId:logical,query:'Synthetic query',pageNumber:1,items:[{itemIndex:0,providerPostId:'7001',rawContent:input.body,contentHash:sha(input.body)}]}],
    orchestration:{workflowRunId:logical,logicalExecutionId:'synthetic',attemptIndex:9,phase:'COMPLETED_UNCONFIRMED',status:'STOPPED_RESUMABLE',stopReason:'REQUEST_BUDGET',
      config:{startTime:'2026-01-01',endTime:'2026-01-02'},pages:[{workflowRunId:logical,logicalExecutionId:'synthetic',query:'Synthetic query',allocation:'CORE',pageNumber:1,
        actualRequests:1,creditsDeducted:1,outcome:'COMPLETED_UNCONFIRMED',returnedPostIds:['7001']}]}};
  const originalFinal=structuredClone(result),syncFinal=structuredClone(result),claim=structuredClone(result);
  originalFinal.orchestration.phase='READY';originalFinal.requestLedger[9].outcome='CONFIRMED';
  Object.assign(originalFinal.orchestration.pages[0],{outcome:'CONFIRMED',artifactReceipt:String(pins.result)});
  syncFinal.adminSync={mode:'recovery',workflowRunId:logical,executingWorkflowRunId:uploader,observationRunId:logical,completedAt:'2026-01-03T00:00:00Z',observationCount:1,
    summaries:[{runId:logical,pageNumber:1,inserted:0,unchanged:1}]};
  claim.orchestration.phase='CLAIMED';claim.orchestration.pages=[];claim.jobs=[];claim.discoveryReviews=[];
  Object.assign(claim.requestLedger[9],{actualRequests:null,creditsDeducted:null,outcome:'RESERVED_AMBIGUOUS_UNTIL_COMPLETED'});
  delete claim.requestLedger[9].claimArtifactReceipt;delete claim.requestLedger[9].claimArtifactName;
  return {claim,result,originalFinal,syncFinal};
}
function fixture(mutate=()=>{}){
  const s=snapshots();mutate(s);
  const routes={},ciphertexts={};
  routes[base]={id:1349147313,full_name:repo};
  for(const [role,id] of Object.entries(pins)){
    const cipher=encryptCheckpoint(Buffer.from(JSON.stringify(s[role])),key),archive=zip(cipher);ciphertexts[role]=cipher;
    routes[`${base}/actions/artifacts/${id}`]={id,name:role==='claim'?`socialapis-page-${logical}-1-claim`:role==='result'?`socialapis-page-${logical}-1-result`:'socialapis-discovery-state',
      expired:false,size_in_bytes:archive.length,digest:`sha256:${sha(archive)}`,workflow_run:{id:Number(role==='syncFinal'?uploader:logical),repository_id:1349147313}};
    routes[`${base}/actions/artifacts/${id}/zip`]={archive:archive.toString('base64')};
  }
  const runs=[current,uploader,logical].map((id,i)=>({id:Number(id),run_number:3-i,status:i?'completed':'in_progress',run_attempt:1,conclusion:i?'success':null,
    head_sha:i===2?revision:'a'.repeat(40),path:'.github/workflows/socialapis-job-discovery.yml',head_branch:'main',event:'workflow_dispatch',head_repository:{full_name:repo}}));
  routes[`${base}/actions/workflows/socialapis-job-discovery.yml/runs?branch=main&event=workflow_dispatch&per_page=20&page=1`]={total_count:3,workflow_runs:runs};
  const restore=definitions.findIndex(d=>d.name==='Decrypt restored checkpoint (legacy plaintext remains supported)');
  for(const r of runs){
    routes[`${base}/contents/.github/workflows/socialapis-job-discovery.yml?ref=${r.head_sha}`]={encoding:'base64',content:Buffer.from(source).toString('base64')};
    const steps=definitions.map((d,i)=>({name:d.name??`Run ${d.uses??d.run.split('\n')[0]}`,number:i+2,
      status:r.id===Number(current)?i<restore?'completed':i===restore?'in_progress':'pending':'completed',
      conclusion:r.id===Number(current)?i<restore?'success':null:d.uses==='./.github/actions/socialapis-discovery-page'&&r.id===Number(uploader)?'skipped':'success'}));
    routes[`${base}/actions/runs/${r.id}/jobs?filter=all&per_page=100&page=1`]={total_count:1,jobs:[{name:'discover',run_id:r.id,run_attempt:1,status:r.status,conclusion:r.conclusion,steps}]};
  }
  routes[`${base}/actions/runs/${logical}`]=runs[2];
  for(const file of ['core.mjs','socialapis-adapter.mjs','vacancy-extraction.mjs']){
    const bytes=Buffer.from(readFileSync(new URL(`../src/${file}`,import.meta.url),'utf8').replace(/\r\n/gu,'\n'));
    routes[`${base}/contents/tools/browser-job-discovery/src/${file}?ref=${revision}`]={encoding:'base64',content:bytes.toString('base64')};
  }
  const calls=[];
  const transport=async(url,options)=>{
    const address=String(url);assert.ok(address.startsWith(base),'only pinned repository requests');assert.equal(options.method,'GET');
    assert.equal(options.redirect,'manual');calls.push(address);
    assert.ok(Object.hasOwn(routes,address),`unexpected mocked read: ${new URL(address).pathname}`);
    const value=routes[address];return value.archive?new Response(Buffer.from(value.archive,'base64')):Response.json(value);
  };
  return {s,routes,calls,ciphertexts,transport};
}
const restore=f=>restoreAcknowledgedFromGitHub({token,key,restoredState:f.s.syncFinal,artifactId:pins.syncFinal,currentRunId:current,transport:f.transport});

test('authenticated restore selects exact original ciphertext and preserves all source snapshots',async()=>{
  const f=fixture(),before=JSON.stringify(f.s),r=await restore(f);
  assert.equal(r.status,'SELECTED');assert.equal(r.artifactId,pins.originalFinal);assert.deepEqual(r.encryptedCheckpoint,f.ciphertexts.originalFinal);
  assert.deepEqual(JSON.parse(decryptCheckpoint(r.encryptedCheckpoint,key)),f.s.originalFinal);assert.equal(JSON.stringify(f.s),before);
  assert.ok(f.calls.every(v=>v.startsWith(base)));assert.equal(f.calls.filter(v=>v.endsWith('/zip')).length,4);
});
for(const defect of ['unknown-source','missing-observation','historical-binding','derived-tamper','later-started','current-provider-started','incomplete-history','missing-historical-steps','wrong-restored','expired'])
test(`authenticated boundary blocks ${defect} with no provider`,async()=>{
  const f=fixture(s=>{
    if(defect==='missing-observation')for(const role of ['result','originalFinal','syncFinal'])s[role].discoveryReviews=[];
    if(defect==='historical-binding')for(const role of ['result','originalFinal','syncFinal'])s[role].historicalJobs=[{...s[role].jobs[0],id:'7002',sourceIdentity:'SOCIALAPIS:7002'}];
    if(defect==='derived-tamper')for(const role of ['result','originalFinal','syncFinal'])s[role].jobs[0].title='Fabricated role';
  });
  if(defect==='unknown-source')f.routes[`${base}/contents/tools/browser-job-discovery/src/core.mjs?ref=${revision}`].content=Buffer.from('unknown code').toString('base64');
  if(defect==='expired')f.routes[`${base}/actions/artifacts/${pins.result}`].expired=true;
  if(defect==='wrong-restored')f.s.syncFinal.jobs[0].body='unbound local state';
  if(defect==='incomplete-history')delete f.routes[`${base}/actions/workflows/socialapis-job-discovery.yml/runs?branch=main&event=workflow_dispatch&per_page=20&page=1`];
  if(defect==='missing-historical-steps')f.routes[`${base}/actions/runs/${uploader}/jobs?filter=all&per_page=100&page=1`].jobs[0].steps.pop();
  if(defect==='later-started'||defect==='current-provider-started'){
    const id=defect==='later-started'?uploader:current;
    const step=f.routes[`${base}/actions/runs/${id}/jobs?filter=all&per_page=100&page=1`].jobs[0].steps.find(s=>s.name==='Run ./.github/actions/socialapis-discovery-page');
    step.status=defect==='later-started'?'completed':'in_progress';step.conclusion=defect==='later-started'?'success':null;
  }
  const r=await restore(f);assert.equal(r.status,'BLOCKED');assert.match(r.reason,/^[A-Z][A-Z0-9_]+$/u);assert.equal(r.encryptedCheckpoint,undefined);
});
test('only the active verified restore prefix may omit unstarted future steps',async()=>{
  const f=fixture(),steps=f.routes[`${base}/actions/runs/${current}/jobs?filter=all&per_page=100&page=1`].jobs[0].steps;
  steps.splice(steps.findIndex(s=>s.status==='in_progress')+1);
  const r=await restore(f);assert.equal(r.status,'SELECTED');assert.equal(r.artifactId,pins.originalFinal);
});
test('manifest is immutable and inherited property names cannot authorize a producer',()=>{
  assert.equal(Object.isFrozen(RECOVERY_PRODUCERS[revision].digests),true);
  for(const headSha of ['constructor','toString','__proto__'])assert.equal(validateRecoveryContent({snapshot:{jobs:[],discoveryReviews:[]},
    producerEvidence:{headSha,source:'AUTHENTICATED_GITHUB_GET',repository:{id:1349147313,full_name:repo},runId:logical,records:[]}}).status,'BLOCKED');
});
test('existing workflow/CLI restore hands selected identity to Fresh before any provider action',()=>{
  const steps=parse(source).jobs.discover.steps,unseal=steps.find(s=>s.id==='restored_checkpoint'),fresh=steps.find(s=>s.run==='node src/cli.mjs --orchestrate fresh-init');
  assert.equal(unseal.run,'node src/cli.mjs --orchestrate unseal');assert.match(unseal.env.DISCOVERY_RECOVER_ACKNOWLEDGED,/fresh_session_id/u);
  for(const field of ['ARTIFACT_RECEIPT','ARTIFACT_NAME','ARTIFACT_RUN_ID'])assert.match(fresh.env[field],/steps.restored_checkpoint.outputs/u);
  assert.ok(steps.indexOf(unseal)<steps.indexOf(fresh));assert.ok(steps.indexOf(fresh)<steps.findIndex(s=>s.uses==='./.github/actions/socialapis-discovery-page'));
});
test('actual unseal CLI restores synthetic final bytes and emits only pinned outputs, with no sockets',()=>{
  const f=fixture();mkdirSync(join(root,'.debug'),{recursive:true});const local=mkdtempSync(join(root,'.debug','recovery-boundary-'));
  try{
    const output=join(local,'state.json'),outputs=join(local,'outputs.txt'),routes=join(local,'routes.json'),bootstrap=join(local,'guard.mjs');
    writeFileSync(output+'.enc',f.ciphertexts.syncFinal);writeFileSync(routes,JSON.stringify(f.routes));
    writeFileSync(bootstrap,`import net from 'node:net';import{readFileSync}from'node:fs';
net.Socket.prototype.connect=()=>{throw Error('OFFLINE_SOCKET_FORBIDDEN')};
const routes=JSON.parse(readFileSync(${JSON.stringify(routes)},'utf8'));
globalThis.fetch=async(url,options)=>{if(options.method!=='GET'||!String(url).startsWith(${JSON.stringify(base)}))throw Error('OFFLINE_ENDPOINT_FORBIDDEN');
const value=routes[String(url)];if(!value)throw Error('OFFLINE_ROUTE_MISSING');return value.archive?new Response(Buffer.from(value.archive,'base64')):Response.json(value);};`);
    const stdout=execFileSync(process.execPath,['--import',pathToFileURL(bootstrap).href,'src/cli.mjs','--orchestrate','unseal'],{cwd:root,encoding:'utf8',stdio:'pipe',
      env:{...process.env,DISCOVERY_STATE_FILE:output,DISCOVERY_CHECKPOINT_KEY:key,DISCOVERY_RECOVER_ACKNOWLEDGED:'true',GH_READ_TOKEN:token,GITHUB_RUN_ID:current,
        ARTIFACT_RECEIPT:String(pins.syncFinal),GITHUB_OUTPUT:outputs}});
    assert.deepEqual(JSON.parse(readBytes(output,'utf8')),f.s.originalFinal);assert.deepEqual(readBytes(output+'.enc'),f.ciphertexts.originalFinal);
    assert.match(readBytes(outputs,'utf8'),/artifact_id=12723681280\nrun_id=40482593764\nartifact_name=socialapis-discovery-state/u);
    assert.match(stdout,/"providerRequests":0/u);assert.doesNotMatch(stdout,/SYNTHETIC PRIVATE RECRUITMENT|SYNTHETIC_READ_TOKEN/u);assert.ok(!stdout.includes(key));
  }finally{rmSync(local,{recursive:true,force:true});}
});
test('pinned encrypted recovery cannot fall back to a pre-existing plaintext file if ciphertext is missing',()=>{
  mkdirSync(join(root,'.debug'),{recursive:true});const local=mkdtempSync(join(root,'.debug','recovery-missing-'));
  try{
    const output=join(local,'state.json'),bytes=JSON.stringify(snapshots().originalFinal);writeFileSync(output,bytes);
    const guard='data:text/javascript,'+encodeURIComponent("import net from 'node:net';net.Socket.prototype.connect=()=>{throw Error('OFFLINE_SOCKET_FORBIDDEN')};globalThis.fetch=()=>{throw Error('OFFLINE_NETWORK_FORBIDDEN')};");
    assert.throws(()=>execFileSync(process.execPath,['--import',guard,'src/cli.mjs','--orchestrate','unseal'],{cwd:root,encoding:'utf8',stdio:'pipe',
      env:{...process.env,DISCOVERY_STATE_FILE:output,DISCOVERY_CHECKPOINT_KEY:key,DISCOVERY_RECOVER_ACKNOWLEDGED:'true',ARTIFACT_RECEIPT:String(pins.syncFinal),GH_READ_TOKEN:token,GITHUB_RUN_ID:current}}),
      e=>e.status===1&&e.stderr.includes('ORCHESTRATION_FAILED'));
    assert.equal(readBytes(output,'utf8'),bytes);
  }finally{rmSync(local,{recursive:true,force:true});}
});
