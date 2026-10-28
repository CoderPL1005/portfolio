// Synthetic four-artifact graph; no production payloads, credentials or reads.
// Matches the existing recovery-boundary fixture contract without editing it.
import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {parse} from 'yaml';
import {JobPostExtractor} from '../../src/core.mjs';
import {encryptCheckpoint} from '../../src/admin-sync.mjs';

export const key='ac'.repeat(32),token='SYNTHETIC_READ_TOKEN',current='99999999999';
export const pins={claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974};
export const base='https://api.github.com/repos/CoderPL1005/portfolio';
const logical='40482593764',uploader='40488676716',repo='CoderPL1005/portfolio';
const revision='81e15d15e379d4ea2c27a654a5966d056404efe5',sha=v=>createHash('sha256').update(v).digest('hex');
const source=readFileSync(new URL('../../../../.github/workflows/socialapis-job-discovery.yml',import.meta.url),'utf8');
const definitions=parse(source).jobs.discover.steps;
function crc(bytes){let n=0xffffffff;for(const b of bytes){n^=b;for(let i=0;i<8;i++)n=(n>>>1)^((n&1)?0xedb88320:0);}return(n^0xffffffff)>>>0;}
function zip(bytes){
  const name=Buffer.from('socialapis-discovery.json.enc'),local=Buffer.alloc(30),central=Buffer.alloc(46),end=Buffer.alloc(22),c=crc(bytes);
  local.writeUInt32LE(0x04034b50);local.writeUInt16LE(20,4);local.writeUInt32LE(c,14);local.writeUInt32LE(bytes.length,18);local.writeUInt32LE(bytes.length,22);local.writeUInt16LE(name.length,26);
  central.writeUInt32LE(0x02014b50);central.writeUInt16LE(20,6);central.writeUInt32LE(c,16);central.writeUInt32LE(bytes.length,20);central.writeUInt32LE(bytes.length,24);central.writeUInt16LE(name.length,28);
  end.writeUInt32LE(0x06054b50);end.writeUInt16LE(1,8);end.writeUInt16LE(1,10);end.writeUInt32LE(central.length+name.length,12);end.writeUInt32LE(local.length+name.length+bytes.length,16);
  return Buffer.concat([local,name,bytes,central,name,end]);
}
export function readinessFixture(defect){
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
  const snapshots={claim,result,originalFinal,syncFinal};
  if(defect==='binding')for(const s of [result,originalFinal,syncFinal])s.historicalJobs=[{...s.jobs[0],id:'7002',sourceIdentity:'SOCIALAPIS:7002'}];
  if(defect==='tamper')for(const s of [result,originalFinal,syncFinal])s.jobs[0].title='Fabricated role';
  const routes={},ciphertexts={};routes[base]={id:1349147313,full_name:repo};
  for(const [role,id] of Object.entries(pins)){
    const cipher=encryptCheckpoint(Buffer.from(JSON.stringify(snapshots[role])),key),archive=zip(cipher);ciphertexts[role]=cipher;
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
    if(defect==='later-provider'&&r.id===Number(uploader))steps.find(s=>s.name==='Run ./.github/actions/socialapis-discovery-page').conclusion='success';
    routes[`${base}/actions/runs/${r.id}/jobs?filter=all&per_page=100&page=1`]={total_count:1,jobs:[{name:'discover',run_id:r.id,run_attempt:1,status:r.status,conclusion:r.conclusion,steps}]};
  }
  routes[`${base}/actions/runs/${logical}`]=runs[2];
  for(const file of ['core.mjs','socialapis-adapter.mjs','vacancy-extraction.mjs']){
    const bytes=Buffer.from(readFileSync(new URL(`../../src/${file}`,import.meta.url),'utf8').replace(/\r\n/gu,'\n'));
    routes[`${base}/contents/tools/browser-job-discovery/src/${file}?ref=${revision}`]={encoding:'base64',content:bytes.toString('base64')};
  }
  return {snapshots,routes,ciphertexts};
}
