import {createHash} from 'node:crypto';
import {inflateRawSync} from 'node:zlib';
import {decryptCheckpoint, resolveReviewSyncScope} from './admin-sync.mjs';

// Deliberately fixed diagnostic target; no operator-supplied URL or execution ID.
const TARGET = Object.freeze({artifactId:12725991974,uploader:'40488676716',logical:'40482593764',repo:'CoderPL1005/portfolio'});
const MAX_CIPHER=12*1024*1024+32, MAX_ARCHIVE=MAX_CIPHER+64*1024, MAX_OUTPUT=16*1024;
const names=['socialapis-discovery-state','socialapis-discovery-checkpoint'];
const object=x=>x!==null&&typeof x==='object'&&!Array.isArray(x);
const id=x=>(typeof x==='string'&&/^[1-9]\d{0,19}$/u.test(x))?x:
  Number.isSafeInteger(x)&&x>0?String(x):null;
const finite=x=>Number.isFinite(x)?x:null;
const label=(x,values)=>values.includes(x)?x:'UNKNOWN_OR_UNRECOGNIZED';
const fingerprint=s=>createHash('sha256').update(JSON.stringify(s)).digest('hex');
const fail=errorCode=>({ok:false,errorCode});
const bounded=result=>Buffer.byteLength(JSON.stringify(result))<=MAX_OUTPUT?result:fail('OUTPUT_LIMIT_EXCEEDED');
const phases=['READY','TERMINAL_RECONCILED','CLAIMED','IN_FLIGHT','COMPLETED_UNCONFIRMED','TERMINAL_UNCONFIRMED'];
const statuses=['RUNNING','STOPPED_TERMINAL','STOPPED_RESUMABLE'];
const outcomes=['CONFIRMED','COMPLETED_UNCONFIRMED','FAILED_OR_AMBIGUOUS','RESERVED_AMBIGUOUS_UNTIL_COMPLETED'];
const stopReasons=['TARGET_REACHED','SUFFICIENT_RELEVANT_VACANCIES','NO_VALID_PROVIDER_ITEMS','NO_CURSOR','REPEATED_CURSOR',
  'LOW_MARGINAL_YIELD','CORE_BUDGET_EXHAUSTED','REQUEST_BUDGET_EXHAUSTED','CREDIT_BUDGET_EXHAUSTED','PAGE_FAILED',
  'UNKNOWN_REQUEST_COST','UNKNOWN_CREDIT_COST','INSUFFICIENT_CREDITS','PROVIDER_ERROR'];

function artifactValid(artifact,repository){
  return object(repository)&&repository.full_name===TARGET.repo&&Number.isSafeInteger(repository.id)&&repository.id>0&&
    object(artifact)&&artifact.id===TARGET.artifactId&&names.includes(artifact.name)&&artifact.expired===false&&
    id(artifact.workflow_run?.id)===TARGET.uploader&&artifact.workflow_run?.repository_id===repository.id&&
    Number.isSafeInteger(artifact.size_in_bytes)&&artifact.size_in_bytes>0&&artifact.size_in_bytes<=MAX_ARCHIVE;
}

// Only the checkpoint-proof path is inspected. No operational initialization.
// Mirrors fresh-demo.mjs validation order; does not convert a checkpoint into a result receipt.
function syncProofValid(state,artifact){
  if(TARGET.uploader===state.orchestration.workflowRunId)return true;
  const sync=state.adminSync;
  if(artifact.name!=='socialapis-discovery-state'||sync?.mode!=='recovery'||
    sync.executingWorkflowRunId!==TARGET.uploader||!id(sync.workflowRunId)||
    typeof sync.workflowRunId!=='string'||typeof sync.completedAt!=='string'||
    !Number.isFinite(Date.parse(sync.completedAt))||!Array.isArray(sync.summaries))return false;
  const selected=resolveReviewSyncScope(state,{mode:'recovery',executingWorkflowRunId:TARGET.uploader,targetWorkflowRunId:sync.workflowRunId});
  if(sync.observationRunId!==selected.scope.observationRunId||sync.summaries.length!==selected.bundles.length||
    sync.observationCount!==selected.bundles.reduce((n,b)=>n+b.items.length,0))return false;
  return selected.bundles.every((b,index)=>{
    const receipt=sync.summaries[index];
    return receipt?.runId===b.runId&&receipt.pageNumber===b.pageNumber&&Number.isInteger(receipt.inserted)&&receipt.inserted>=0&&
      Number.isInteger(receipt.unchanged)&&receipt.unchanged>=0&&receipt.inserted+receipt.unchanged===b.items.length;
  });
}

export function inspectCheckpoint(state,{artifact,repository,stateFingerprint}={}){
  try{
    if(!artifactValid(artifact,repository))return fail('ARTIFACT_PROVENANCE_INVALID');
    if(!object(state)||!object(state.orchestration)||!Array.isArray(state.jobs)||!Array.isArray(state.requestLedger)||
      !state.requestLedger.every(object)||state.terminalReconciliations!==undefined&&
      (!Array.isArray(state.terminalReconciliations)||!state.terminalReconciliations.every(object)))return fail('CHECKPOINT_SCHEMA_INVALID');
    if(state.orchestration.workflowRunId!==TARGET.logical||stateFingerprint!==fingerprint(state))return fail('FRESH_AUDIT_EVIDENCE_INVALID');
    const run=state.orchestration,phaseAllowed=['READY','TERMINAL_RECONCILED'].includes(run.phase);
    // The phase guard is BEFORE sync receipt validation in the actual source.
    if(phaseAllowed){
      try{if(!syncProofValid(state,artifact))return fail('FRESH_AUDIT_EVIDENCE_INVALID');}
      catch{return fail('FRESH_AUDIT_EVIDENCE_INVALID');}
    }
    const reconciliations=state.terminalReconciliations??[];
    const reconciliationMetadata=(r,index)=>({index,attemptIndex:Number.isSafeInteger(r.attemptIndex)?r.attemptIndex:null,
      workflowRunId:id(r.workflowRunId),actualRequests:finite(r.actualRequests),creditsDeducted:finite(r.creditsDeducted),
      artifactId:id(r.artifactId),claimArtifactId:id(r.claimArtifactId)});
    const ledger=state.requestLedger.map((a,index)=>{
      const matchingReconciliations=reconciliations.flatMap((r,i)=>r.attemptIndex===index&&r.workflowRunId===a.workflowRunId&&
        r.actualRequests===a.actualRequests&&r.creditsDeducted===a.creditsDeducted?[reconciliationMetadata(r,i)]:[]);
      const closed=a.outcome==='CONFIRMED'||matchingReconciliations.length>0,reasons=[];
      if(!closed)reasons.push('OUTCOME_NOT_CONFIRMED_AND_NO_MATCHING_TERMINAL_RECONCILIATION');
      if(a.actualRequests!==1)reasons.push('ACTUAL_REQUESTS_NOT_EXACTLY_ONE');
      if(!Number.isFinite(a.creditsDeducted))reasons.push('CREDITS_NOT_FINITE_KNOWN_NUMBER');
      else if(a.creditsDeducted<0||a.creditsDeducted>1)reasons.push('KNOWN_CREDITS_OUTSIDE_CURRENT_PER_REQUEST_CAP');
      return {index,workflowRunId:id(a.workflowRunId),outcome:label(a.outcome,outcomes),
        actualRequests:finite(a.actualRequests),actualRequestsType:typeof a.actualRequests,
        creditsDeducted:finite(a.creditsDeducted),creditsDeductedType:typeof a.creditsDeducted,
        closed,ambiguous:reasons.length>0,reasons,matchingReconciliations};
    });
    const bad=ledger.find(row=>row.ambiguous);
    const guard=!phaseAllowed?'CHECKPOINT_PHASE':bad?'GLOBAL_LEDGER':null;
    return bounded({ok:true,artifact:{id:String(artifact.id),name:artifact.name,uploaderRunId:TARGET.uploader},
      stateFingerprint,orchestration:{phase:label(run.phase,phases),status:label(run.status,statuses),
        stopReason:run.stopReason===null?null:label(run.stopReason,stopReasons),workflowRunId:TARGET.logical},
      diagnosis:{guard,sourceLocation:guard==='CHECKPOINT_PHASE'?'fresh-demo.mjs:14':guard==='GLOBAL_LEDGER'?'fresh-demo.mjs:71':null,
        errorCode:guard?'FRESH_AUDIT_AMBIGUOUS':null,ledgerIndex:guard==='GLOBAL_LEDGER'?bad.index:null,
        scope:'CHECKPOINT_PROOF_PATH_ONLY',syncProofChecked:phaseAllowed},
      ledger,terminalReconciliations:reconciliations.map(reconciliationMetadata)});
  }catch{return fail('CHECKPOINT_SCHEMA_INVALID');}
}

function crc32(bytes){
  let crc=0xffffffff;
  for(const byte of bytes){crc^=byte;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}
  return (crc^0xffffffff)>>>0;
}
function invalidArchive(){throw new Error('CHECKPOINT_ARCHIVE_INVALID');}

// In-memory, single-entry ZIP reader: stored/deflate, optional data descriptor.
// Central and local headers must agree. No extraction, paths or filesystem writes.
function ciphertextFromZip(zip){
  if(!Buffer.isBuffer(zip)||zip.length<22||zip.length>MAX_ARCHIVE)invalidArchive();
  let end=-1;
  for(let i=zip.length-22;i>=Math.max(0,zip.length-65557);i--){
    if(zip.readUInt32LE(i)===0x06054b50&&i+22+zip.readUInt16LE(i+20)===zip.length){end=i;break;}
  }
  if(end<0||zip.readUInt16LE(end+4)!==0||zip.readUInt16LE(end+6)!==0||
    zip.readUInt16LE(end+8)!==1||zip.readUInt16LE(end+10)!==1)invalidArchive();
  const central=zip.readUInt32LE(end+16),centralSize=zip.readUInt32LE(end+12);
  if(central+centralSize!==end||centralSize<46||zip.readUInt32LE(central)!==0x02014b50)invalidArchive();
  const flags=zip.readUInt16LE(central+8),method=zip.readUInt16LE(central+10),crc=zip.readUInt32LE(central+16);
  const compressed=zip.readUInt32LE(central+20),uncompressed=zip.readUInt32LE(central+24);
  const nameLength=zip.readUInt16LE(central+28),extraLength=zip.readUInt16LE(central+30),commentLength=zip.readUInt16LE(central+32);
  const mode=zip.readUInt32LE(central+38)>>>16;
  if(flags&~0x0808||![0,8].includes(method)||compressed>MAX_ARCHIVE||uncompressed<32||uncompressed>MAX_CIPHER||
    zip.readUInt16LE(central+34)!==0||zip.readUInt32LE(central+42)!==0||
    mode&&(mode&0o170000)!==0o100000||46+nameLength+extraLength+commentLength!==centralSize)invalidArchive();
  const expected=Buffer.from('socialapis-discovery.json.enc');
  if(!zip.subarray(central+46,central+46+nameLength).equals(expected)||zip.readUInt32LE(0)!==0x04034b50||
    zip.readUInt16LE(6)!==flags||zip.readUInt16LE(8)!==method||zip.readUInt16LE(26)!==nameLength)invalidArchive();
  const start=30+nameLength+zip.readUInt16LE(28),finish=start+compressed;
  if(!zip.subarray(30,30+nameLength).equals(expected)||start>central||finish>central)invalidArchive();
  if(flags&8){
    let descriptor=finish;
    if(zip.readUInt32LE(descriptor)===0x08074b50)descriptor+=4;
    if(descriptor+12!==central||zip.readUInt32LE(descriptor)!==crc||
      zip.readUInt32LE(descriptor+4)!==compressed||zip.readUInt32LE(descriptor+8)!==uncompressed)invalidArchive();
  }else if(finish!==central||zip.readUInt32LE(14)!==crc||zip.readUInt32LE(18)!==compressed||zip.readUInt32LE(22)!==uncompressed)invalidArchive();
  const bytes=method===0?Buffer.from(zip.subarray(start,finish)):
    inflateRawSync(zip.subarray(start,finish),{maxOutputLength:MAX_CIPHER});
  if(bytes.length!==uncompressed||crc32(bytes)!==crc)invalidArchive();
  return bytes;
}

export async function inspectArtifact({github,repository,key}={}){
  let ciphertext,plaintext;
  try{
    const artifact=await github.getArtifact(TARGET.artifactId);
    if(!artifactValid(artifact,repository))return fail('ARTIFACT_PROVENANCE_INVALID');
    const archive=await github.downloadArtifact(TARGET.artifactId);
    try{ciphertext=ciphertextFromZip(archive);}catch{return fail('CHECKPOINT_ARCHIVE_INVALID');}
    try{plaintext=decryptCheckpoint(ciphertext,key);}catch{return fail('CHECKPOINT_DECRYPTION_FAILED');}
    let state;
    try{state=JSON.parse(plaintext.toString('utf8'));}catch{return fail('CHECKPOINT_SCHEMA_INVALID');}
    return inspectCheckpoint(state,{artifact,repository,stateFingerprint:fingerprint(state)});
  }catch{return fail('CHECKPOINT_INSPECTION_FAILED');}
  finally{plaintext?.fill(0);ciphertext?.fill(0);}
}

async function readBounded(response,limit){
  const reader=response.body?.getReader();
  if(!reader)throw new Error('READ_FAILED');
  const chunks=[];let length=0;
  try{
    while(true){const {value,done}=await reader.read();if(done)break;length+=value.byteLength;
      if(length>limit){await reader.cancel();throw new Error('READ_FAILED');}chunks.push(Buffer.from(value));}
    return Buffer.concat(chunks);
  }finally{reader.releaseLock();}
}

// The only network capability: fixed GitHub GETs and one signed artifact GET.
// Authorization is never forwarded to the signed storage URL; no automatic redirects/retries.
export async function inspectFromGitHub({token,key,transport=fetch}={}){
  try{
    if(typeof token!=='string'||!token||typeof key!=='string'||!/^[a-f0-9]{64}$/iu.test(key))return fail('CHECKPOINT_CREDENTIALS_UNAVAILABLE');
    const signal=AbortSignal.timeout(60_000);
    const headers={Authorization:`Bearer ${token}`,Accept:'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'};
    const base=`https://api.github.com/repos/${TARGET.repo}`;
    const get=path=>transport(base+path,{method:'GET',headers,signal,redirect:'manual',credentials:'omit'});
    const json=async path=>{const response=await get(path);if(response.status!==200)throw new Error('READ_FAILED');
      return JSON.parse((await readBounded(response,64*1024)).toString('utf8'));};
    const repository=await json('');
    const github={getArtifact:()=>json(`/actions/artifacts/${TARGET.artifactId}`),downloadArtifact:async()=>{
      const response=await get(`/actions/artifacts/${TARGET.artifactId}/zip`);
      if(response.status===200)return readBounded(response,MAX_ARCHIVE);
      if(response.status!==302)throw new Error('READ_FAILED');
      const url=new URL(response.headers.get('location'));
      if(url.protocol!=='https:'||url.username||url.password||url.port||url.hash||
        !(url.hostname.endsWith('.blob.core.windows.net')||url.hostname.endsWith('.actions.githubusercontent.com')))throw new Error('READ_FAILED');
      const archive=await transport(url.href,{method:'GET',signal,redirect:'error',credentials:'omit'});
      if(archive.status!==200)throw new Error('READ_FAILED');
      return readBounded(archive,MAX_ARCHIVE);
    }};
    return await inspectArtifact({github,repository,key});
  }catch{return fail('CHECKPOINT_INSPECTION_FAILED');}
}
