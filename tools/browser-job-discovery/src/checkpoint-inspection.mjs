import {createHash} from 'node:crypto';
import {inflateRawSync} from 'node:zlib';
import {decryptCheckpoint, resolveReviewSyncScope} from './admin-sync.mjs';
import {JobPostExtractor} from './core.mjs';
import {RECOVERY_PRODUCERS,recoveryProducerEvidence} from './recovery-source.mjs';

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
function githubReads(token,transport,signal){
    const headers={Authorization:`Bearer ${token}`,Accept:'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'};
    const base=`https://api.github.com/repos/${TARGET.repo}`;
    const get=path=>transport(base+path,{method:'GET',headers,signal,redirect:'manual',credentials:'omit'});
    const json=async(path,limit=64*1024)=>{const response=await get(path);if(response.status!==200)throw new Error('READ_FAILED');
      return JSON.parse((await readBounded(response,limit)).toString('utf8'));};
    const downloadArtifact=async artifactId=>{
      if(![TARGET.artifactId,...Object.values(COMPARISON_PINS)].includes(artifactId))throw new Error('READ_FAILED');
      const response=await get(`/actions/artifacts/${artifactId}/zip`);
      if(response.status===200)return readBounded(response,MAX_ARCHIVE);
      if(response.status!==302)throw new Error('READ_FAILED');
      const url=new URL(response.headers.get('location'));
      if(url.protocol!=='https:'||url.username||url.password||url.port||url.hash||
        !(url.hostname.endsWith('.blob.core.windows.net')||url.hostname.endsWith('.actions.githubusercontent.com')))throw new Error('READ_FAILED');
      const archive=await transport(url.href,{method:'GET',signal,redirect:'error',credentials:'omit'});
      if(archive.status!==200)throw new Error('READ_FAILED');
      return readBounded(archive,MAX_ARCHIVE);
    };
    return {json,downloadArtifact};
}
export async function inspectFromGitHub({token,key,transport=fetch}={}){
  try{
    if(typeof token!=='string'||!token||typeof key!=='string'||!/^[a-f0-9]{64}$/iu.test(key))return fail('CHECKPOINT_CREDENTIALS_UNAVAILABLE');
    const api=githubReads(token,transport,AbortSignal.timeout(60_000)),repository=await api.json('');
    return await inspectArtifact({github:{getArtifact:()=>api.json(`/actions/artifacts/${TARGET.artifactId}`),
      downloadArtifact:()=>api.downloadArtifact(TARGET.artifactId)},repository,key});
  }catch{return fail('CHECKPOINT_INSPECTION_FAILED');}
}

const COMPARISON_PINS=Object.freeze({claim:12723661427,result:12723771007,originalFinal:12723681280,syncFinal:12725991974});
const COMPARISON_ROLES=Object.freeze(Object.keys(COMPARISON_PINS));
const SNAPSHOT_ROLES=Object.freeze(['result','originalFinal','syncFinal']);
const DIGEST=/^sha256:[a-f0-9]{64}$/u;
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const comparisonFailure=code=>({...fail(code),eligibility:'NOT_YET_DETERMINABLE'});

// Versioned semantic hash: object keys sorted, arrays/text never normalized.
// Independent of archive bytes and the existing insertion-order state fingerprint.
function canonical(value,depth=0){
  if(depth>100)throw new Error('COMPARISON_DEPTH_LIMIT');
  if(typeof value==='number'&&!Number.isFinite(value))throw new Error('COMPARISON_NONFINITE_VALUE');
  if(Array.isArray(value))return value.map(v=>canonical(v,depth+1));
  if(object(value))return Object.fromEntries(Object.keys(value).sort().map(k=>[k,canonical(value[k],depth+1)]));
  return value;
}
const semanticHash=value=>sha(JSON.stringify(canonical(value)));
const equal=(a,b)=>a===undefined||b===undefined?a===b:semanticHash(a)===semanticHash(b);
function comparisonArtifactValid(a,role,repository){
  const run=role==='syncFinal'?TARGET.uploader:TARGET.logical;
  const name=role==='claim'?`socialapis-page-${TARGET.logical}-1-claim`:
    role==='result'?`socialapis-page-${TARGET.logical}-1-result`:'socialapis-discovery-state';
  return repository?.id===1349147313&&repository.full_name===TARGET.repo&&object(a)&&a.id===COMPARISON_PINS[role]&&
    a.name===name&&a.expired===false&&id(a.workflow_run?.id)===run&&a.workflow_run?.repository_id===repository.id&&
    Number.isSafeInteger(a.size_in_bytes)&&a.size_in_bytes>0&&a.size_in_bytes<=MAX_ARCHIVE&&DIGEST.test(a.digest??'');
}
function comparisonShape(s){
  return object(s)&&object(s.orchestration)&&Array.isArray(s.orchestration.pages)&&Array.isArray(s.requestLedger)&&s.requestLedger.every(object)&&
    Array.isArray(s.jobs)&&s.jobs.every(object)&&Array.isArray(s.discoveryReviews)&&s.discoveryReviews.every(object)&&
    (s.historicalJobs===undefined||Array.isArray(s.historicalJobs)&&s.historicalJobs.every(object))&&
    Array.isArray(s.terminalReconciliations)&&s.terminalReconciliations.every(object);
}
const active=s=>s.requestLedger[s.orchestration.attemptIndex];
function closedEntry(s,a,index){
  return a.outcome==='CONFIRMED'||s.terminalReconciliations.some(r=>r.attemptIndex===index&&r.workflowRunId===a.workflowRunId&&
    r.actualRequests===a.actualRequests&&r.creditsDeducted===a.creditsDeducted);
}
function ledgerKnown(s){
  return s.requestLedger.every((a,index)=>a.actualRequests===1&&Number.isFinite(a.creditsDeducted)&&a.creditsDeducted>=0&&a.creditsDeducted<=1&&
    (index===9||closedEntry(s,a,index)));
}
function protectedLedger(s){
  return s.requestLedger.map((row,index)=>{if(index!==9)return row;const {outcome,...rest}=row;return rest;});
}
function protectedState(s){
  const copy=structuredClone(s);
  delete copy.adminSync;delete copy.workflowMetrics;delete copy.orchestration.phase;
  if(copy.requestLedger[9])delete copy.requestLedger[9].outcome;
  const page=copy.orchestration.pages.at(-1);
  if(page){delete page.outcome;delete page.artifactReceipt;}
  return copy;
}
const MAX_CONTENT_FAILURES=16;
const JOB_CONTENT_FIELDS=Object.freeze(['title','employer','location','requirements','body']);
function fieldType(value){
  if(value===undefined)return 'missing';
  if(value===null)return 'null';
  if(Array.isArray(value))return 'array';
  const type=typeof value;
  if(['string','number','boolean','object'].includes(type))return type;
  throw new Error('CONTENT_DIAGNOSTIC_SCHEMA_INVALID');
}
function contentSummary(totalFailures,failures){
  return {valid:totalFailures===0,totalFailures,reportedFailures:failures.length,
    truncated:totalFailures>failures.length,failures};
}

// Read-only explanations of the existing predicates, NOT a compatibility fix.
// Stored values/hashes are never repaired; nullable/enriched historical jobs
// still fail the same validation. Count all failures, even after output is full.
export function diagnoseContentValidation(s,role){
  if(!SNAPSHOT_ROLES.includes(role))return fail('CONTENT_DIAGNOSTIC_ROLE_INVALID');
  try{
    if(!object(s)||!Array.isArray(s.jobs)||!s.jobs.every(object)||
      !Array.isArray(s.discoveryReviews)||!s.discoveryReviews.every(object)||
      (s.historicalJobs!==undefined&&(!Array.isArray(s.historicalJobs)||!s.historicalJobs.every(object))))
      return fail('CONTENT_DIAGNOSTIC_SCHEMA_INVALID');
    const failures=[];let totalFailures=0;
    const add=(collection,recordIndex,rule,extra={})=>{
      totalFailures++;
      if(failures.length<MAX_CONTENT_FAILURES)failures.push({role,collection,recordIndex,rule,...extra});
    };
    // The same identity may deliberately appear in both current and historical
    // collections. Preserve and compare each collection; never merge them here.
    for(const collection of ['jobs','historicalJobs']){
      const identities=new Set();
      for(const [index,j] of (s[collection]??[]).entries()){
        if(j.provider!=='SOCIALAPIS')add(collection,index,'JOB_PROVIDER_INVALID');
        if(typeof j.id!=='string'||!j.id)add(collection,index,'JOB_ID_INVALID');
        // Never coerce arbitrary provider objects into text for identity checks.
        if(typeof j.id!=='string'||j.sourceIdentity!==`SOCIALAPIS:${j.id}`)add(collection,index,'JOB_SOURCE_IDENTITY_INVALID');
        if(identities.has(j.sourceIdentity))add(collection,index,'JOB_DUPLICATE_IDENTITY');
        identities.add(j.sourceIdentity);
        const invalidFields=JOB_CONTENT_FIELDS.filter(k=>typeof j[k]!=='string');
        if(invalidFields.length){
          const fieldFlags=Object.fromEntries(invalidFields.map(k=>[k,{present:Object.hasOwn(j,k),type:fieldType(j[k])}]));
          add(collection,index,'JOB_FIELD_TYPE_INVALID',{fieldFlags});
        }else{
          // Preserve the old persisted-field validation formula. This may NOT be
          // the pre-extraction hash contract of a historical producer generation.
          const material=JOB_CONTENT_FIELDS.map(k=>j[k]).join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim();
          if(j.contentHash!==sha(material))add(collection,index,'JOB_PERSISTED_HASH_MISMATCH',{hashMatches:false});
        }
      }
    }
    const bundles=new Set();
    for(const [index,b] of s.discoveryReviews.entries()){
      const identity=JSON.stringify([b.runId,b.query,b.pageNumber]);
      if(bundles.has(identity))add('discoveryReviews',index,'REVIEW_BUNDLE_DUPLICATE');
      bundles.add(identity);
      if(!Array.isArray(b.items)){add('discoveryReviews',index,'REVIEW_ITEMS_INVALID');continue;}
      for(const [i,item] of b.items.entries()){
        const extra={itemIndex:i};
        if(!object(item)){add('discoveryReviews',index,'REVIEW_ITEM_INVALID',extra);continue;}
        if(item.itemIndex!==i)add('discoveryReviews',index,'REVIEW_ITEM_INDEX_INVALID',extra);
        if(item.rawContent===null){
          if(item.contentHash!==null)add('discoveryReviews',index,'OBSERVATION_NULL_HASH_INVALID',extra);
        }else if(typeof item.rawContent!=='string')add('discoveryReviews',index,'OBSERVATION_RAW_TYPE_INVALID',extra);
        else if(item.contentHash!==sha(item.rawContent))add('discoveryReviews',index,'OBSERVATION_RAW_HASH_MISMATCH',{...extra,hashMatches:false});
      }
    }
    return contentSummary(totalFailures,failures);
  }catch{return fail('CONTENT_DIAGNOSTIC_SCHEMA_INVALID');}
}
function combinedContentValidation(snapshots){
  const reports=SNAPSHOT_ROLES.map(role=>diagnoseContentValidation(snapshots[role],role));
  // Shape was checked at the comparison boundary. A diagnostic error cannot
  // turn a partial scan into valid content or disclose exception details.
  if(reports.some(r=>typeof r.valid!=='boolean'))throw new Error('CONTENT_DIAGNOSTIC_SCHEMA_INVALID');
  return contentSummary(reports.reduce((n,r)=>n+r.totalFailures,0),reports.flatMap(r=>r.failures).slice(0,MAX_CONTENT_FAILURES));
}

// Schemas describe known technical metadata only. Unknown keys are never
// printed, and are not blanket-exempted even below a familiar root object.
const LEAF=true;
const SYNC_SCHEMA={mode:LEAF,workflowRunId:LEAF,observationRunId:LEAF,executingWorkflowRunId:LEAF,completedAt:LEAF,observationCount:LEAF,
  summaries:[{runId:LEAF,pageNumber:LEAF,inserted:LEAF,unchanged:LEAF}]};
const CLASSIFICATIONS={TARGET_MATCH:LEAF,FALLBACK_MATCH:LEAF,POTENTIAL_MATCH:LEAF,INSUFFICIENT_DATA:LEAF,NOT_MATCHED:LEAF};
const METRICS_SCHEMA=Object.fromEntries(('workflowRunId logicalExecutionId status continuationAvailable profileVersion profileFingerprint '+
  'canonicalOpportunities newlyDiscoveredOpportunities newRelevantOpportunities newRelevantOpportunitiesPerCredit freshnessUnknown freshnessUnverified '+
  'actualRequests knownActualRequests reservedAttempts uncertainAttempts totalDeductedCredits knownDeductedCredits stopReason '+
  'uniqueRelevantVacancies newRelevantVacancies overlapAcrossPages').split(' ').map(k=>[k,LEAF]));
Object.assign(METRICS_SCHEMA,{adminSync:SYNC_SCHEMA,
  querySelection:{query:LEAF,rankingBasis:LEAF,score:LEAF,profileFingerprint:LEAF,preferencesFingerprint:LEAF,version:LEAF,
    evidenceLevel:LEAF,targetPreferencesFingerprint:LEAF,candidateCount:LEAF,coverage:LEAF},
  reviewSummary:{observations:LEAF,withVacancies:LEAF,classifications:CLASSIFICATIONS,freshnessUnknown:LEAF},
  queryDepths:[{normalizedQueryFingerprint:LEAF,pagesSurveyed:LEAF,maximumPageNumber:LEAF,conclusion:LEAF}],
  storage:{inserted:LEAF,updated:LEAF,unchanged:LEAF,totalCandidates:LEAF},
  logicalExecution:{completedPages:LEAF,uniqueRelevantVacancies:LEAF,newRelevantVacancies:LEAF,newRelevantOpportunities:LEAF,actualRequests:LEAF,creditsDeducted:LEAF}});
function metadataKnown(value,schema){
  if(value===null||value===undefined)return true;
  if(schema===LEAF)return ['string','number','boolean'].includes(typeof value);
  if(Array.isArray(schema))return Array.isArray(value)&&value.every(v=>metadataKnown(v,schema[0]));
  return object(value)&&Object.keys(value).every(k=>Object.hasOwn(schema,k)&&metadataKnown(value[k],schema[k]));
}
function syncMetadataKnown(value){
  if(!metadataKnown(value,SYNC_SCHEMA))return false;
  if(value===null||value===undefined)return true;
  const observationId=v=>id(v)||typeof v==='string'&&/^fresh-[a-z0-9][a-z0-9-]{7,63}$/u.test(v);
  for(const k of ['workflowRunId','executingWorkflowRunId'])if(value[k]!==undefined&&value[k]!==null&&!id(value[k]))return false;
  if(value.observationRunId!==undefined&&!observationId(value.observationRunId))return false;
  if(value.mode!==undefined&&!['current','recovery'].includes(value.mode))return false;
  if(value.completedAt!==undefined&&(typeof value.completedAt!=='string'||!Number.isFinite(Date.parse(value.completedAt))))return false;
  if(value.observationCount!==undefined&&(!Number.isSafeInteger(value.observationCount)||value.observationCount<0))return false;
  return (value.summaries??[]).every(row=>
    (row.runId===undefined||observationId(row.runId))&&['pageNumber','inserted','unchanged'].every(k=>
      row[k]===undefined||Number.isSafeInteger(row[k])&&row[k]>=0));
}
function differences(a,b,path='',out=[]){
  if(equal(a,b))return out;
  if(Array.isArray(a)&&Array.isArray(b)&&a.length===b.length){a.forEach((v,i)=>differences(v,b[i],`${path}[${i}]`,out));return out;}
  if(object(a)&&object(b)){
    for(const k of new Set([...Object.keys(a),...Object.keys(b)]))differences(a[k],b[k],path?`${path}.${k}`:k,out);
    return out;
  }
  out.push(path);return out;
}
function comparePair(base,next){
  const last=base.orchestration.pages.length-1;
  const allowed=new Set(['orchestration.phase',`orchestration.pages[${last}].outcome`,
    `orchestration.pages[${last}].artifactReceipt`,'requestLedger[9].outcome']);
  const paths=differences(base,next),ackPaths=[],adminSyncPaths=[],metricsPaths=[];let unclassifiedChanges=0;
  const syncKnown=syncMetadataKnown(base.adminSync)&&syncMetadataKnown(next.adminSync);
  const metricsKnown=metadataKnown(base.workflowMetrics,METRICS_SCHEMA)&&metadataKnown(next.workflowMetrics,METRICS_SCHEMA)&&
    syncMetadataKnown(base.workflowMetrics?.adminSync)&&syncMetadataKnown(next.workflowMetrics?.adminSync);
  for(const path of paths){
    if(allowed.has(path))ackPaths.push(path);
    else if((path==='adminSync'||path.startsWith('adminSync.'))&&syncKnown)adminSyncPaths.push(path);
    else if((path==='workflowMetrics'||path.startsWith('workflowMetrics.'))&&metricsKnown)metricsPaths.push(path);
    else unclassifiedChanges++;
  }
  return {ackPaths,adminSyncPaths,metricsPaths,
    adminSyncChanged:!equal(base.adminSync,next.adminSync),metricsChanged:!equal(base.workflowMetrics,next.workflowMetrics),
    unclassifiedChanges,allDifferencesFingerprint:semanticHash(paths),
    adminSyncFingerprints:[semanticHash(base.adminSync??null),semanticHash(next.adminSync??null)],
    metricsFingerprints:[semanticHash(base.workflowMetrics??null),semanticHash(next.workflowMetrics??null)]};
}
function attemptBinding(s,reference,claimRole=false){
  const run=s.orchestration,a=active(s),r=active(reference);
  if(run.workflowRunId!==TARGET.logical||run.attemptIndex!==9||s.requestLedger.length!==10||!a||
    a.workflowRunId!==TARGET.logical||s.requestLedger.slice(0,10).filter(v=>v.workflowRunId===TARGET.logical).length!==1)return false;
  for(const field of ['workflowRunId','logicalExecutionId','query','allocation','pageNumber','startTime','endTime','inputCursor','inputCursorFingerprint']){
    if(!equal(a[field],r[field]))return false;
  }
  if(a.pageNumber!==1||a.allocation!=='CORE'||run.logicalExecutionId!==a.logicalExecutionId)return false;
  if(!equal(run.config?.startTime,a.startTime)||!equal(run.config?.endTime,a.endTime))return false;
  if(claimRole)return run.phase==='CLAIMED'&&a.actualRequests===null&&a.creditsDeducted===null&&
    a.outcome==='RESERVED_AMBIGUOUS_UNTIL_COMPLETED'&&run.pages.length===0;
  if(run.pages.length!==1)return false;
  const p=run.pages[0];
  return ['workflowRunId','query','allocation','pageNumber','actualRequests','creditsDeducted'].every(k=>equal(p[k],a[k]))&&
    (p.logicalExecutionId===undefined||p.logicalExecutionId===a.logicalExecutionId);
}
function historyComplete(h){
  return h?.source==='AUTHENTICATED_GITHUB_GET'&&h.complete===true&&h.stable===true&&h.sourceProvenanceVerified===true&&Array.isArray(h.runs)&&
    h.runs.some(r=>r.id===TARGET.uploader)&&new Set(h.runs.map(r=>r.id)).size===h.runs.length&&h.runs.every(r=>id(r.id)&&r.status==='completed'&&r.runAttempt===1&&
      ['success','failure'].includes(r.conclusion)&&Array.isArray(r.providerSteps)&&[1,6].includes(r.providerSteps.length)&&
      r.providerSteps.every(s=>s.status==='completed'&&s.conclusion==='skipped'));
}

// These are source-byte contracts, not version-marker guesses. The nullable
// overlay replay below is supported only by the reviewed, unchanged extractor.
const recoveryBlocked=reason=>({status:'BLOCKED',reason});
const recoveryViolation=()=>({status:'INTEGRITY_VIOLATION',reason:'CONTENT_INTEGRITY_VIOLATION'});

// Presentation only: never serialize the internal restore result/ciphertext.
// Unknown strings, including exception messages, cannot become public reasons.
const READINESS_REASONS=new Set([
  'CHECKPOINT_CREDENTIALS_UNAVAILABLE','CHECKPOINT_CAPACITY_REACHED','CHECKPOINT_ARCHIVE_INVALID',
  'CHECKPOINT_DECRYPTION_FAILED','CHECKPOINT_SCHEMA_INVALID','CHECKPOINT_INSPECTION_FAILED',
  'ARTIFACT_PROVENANCE_INVALID','ARTIFACT_DIGEST_MISMATCH','RESTORED_SOURCE_UNVERIFIED','RESTORED_SOURCE_MISMATCH',
  'PRODUCER_SOURCE_UNVERIFIED','CONTENT_CONTRACT_UNPROVABLE','CONTENT_BINDING_UNVERIFIED','HASH_PREIMAGE_UNPROVABLE',
  'CONTENT_INTEGRITY_VIOLATION','PROTECTED_STATE_OR_ACK_INVARIANT_FAILED','CLAIM_BINDING_MISSING',
  'LATER_HISTORY_NOT_VERIFIED','UNCLASSIFIED_SNAPSHOT_DIFFERENCES','RECOVERY_EVIDENCE_INCOMPLETE',
  'OUTPUT_LIMIT_EXCEEDED','RECOVERY_SOURCE_UNVERIFIED','RECOVERY_SOURCE_FINGERPRINT_MISMATCH',
  'VERIFY_ONLY_UNSEAL_REQUIRED','PRIVATE_REPLAY_OUTPUT_REQUIRED'
]);
export function readinessReport(selection){
  const ready=selection?.status==='SELECTED'&&selection.reason==='ACKNOWLEDGED_FINAL_VERIFIED'&&selection.artifactId===COMPARISON_PINS.originalFinal;
  return {status:ready?'READY':'BLOCKED',reason:ready?'ACKNOWLEDGED_FINAL_VERIFIED':
    READINESS_REASONS.has(selection?.reason)?selection.reason:'RECOVERY_SOURCE_UNVERIFIED',
    selectedArtifactId:ready?COMPARISON_PINS.originalFinal:null,providerRequests:0};
}

// Pure internal evidence port. Its caller must obtain records/source bytes from
// the authenticated read boundary, never from workflow inputs or a boolean.
export function validateRecoveryContent({snapshot:s,producerEvidence:e}={}){
  try{
    const producer=object(e)&&Object.hasOwn(RECOVERY_PRODUCERS,e.headSha)?RECOVERY_PRODUCERS[e.headSha]:null;
    if(!producer||e.source!=='AUTHENTICATED_GITHUB_GET'||e.repository?.id!==1349147313||e.repository.full_name!==TARGET.repo||
      id(e.runId)!==TARGET.logical||!equal(e.sourceDigests,producer.digests))return recoveryBlocked('PRODUCER_SOURCE_UNVERIFIED');
    if(!object(s)||!Array.isArray(s.jobs)||!Array.isArray(s.discoveryReviews)||
      s.historicalJobs!==undefined&&!Array.isArray(s.historicalJobs)||!Array.isArray(e.records))return recoveryBlocked('CONTENT_CONTRACT_UNPROVABLE');
    const bindings=new Map();
    for(const r of e.records){
      if(!object(r)||typeof r.sourceIdentity!=='string'||bindings.has(r.sourceIdentity)||
        !Object.values(COMPARISON_PINS).some(v=>id(v)===id(r.originArtifactId)))return recoveryBlocked('CONTENT_BINDING_UNVERIFIED');
      bindings.set(r.sourceIdentity,r);
    }
    for(const collection of [s.jobs,s.historicalJobs??[]]){
      const identities=new Set();
      for(const j of collection){
        if(!object(j)||j.provider!=='SOCIALAPIS'||j.source!=='FACEBOOK'||typeof j.id!=='string'||!j.id||j.sourceIdentity!==`SOCIALAPIS:${j.id}`||
          identities.has(j.sourceIdentity)||typeof j.body!=='string'||j.body.length>24_000||
          JOB_CONTENT_FIELDS.slice(0,4).some(k=>j[k]!==null&&typeof j[k]!=='string'))return recoveryViolation();
        identities.add(j.sourceIdentity);
        const r=bindings.get(j.sourceIdentity);
        if(!r||!object(r.input)||!object(r.projection))return recoveryBlocked('CONTENT_BINDING_UNVERIFIED');
        const p=r.input;
        if(p.provider!=='SOCIALAPIS'||p.source!=='FACEBOOK'||p.id!==j.id||p.body!==j.body||typeof p.body!=='string')return recoveryViolation();
        // Both reviewed SocialAPIs adapters supply exactly four blank fields.
        // No formula search or coercion of missing pre-extraction fields.
        if(JOB_CONTENT_FIELDS.slice(0,4).some(k=>p[k]!==''))return recoveryBlocked('HASH_PREIMAGE_UNPROVABLE');
        const hash=sha(JOB_CONTENT_FIELDS.map(k=>p[k]).join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim());
        if(r.inputHash!==hash||j.contentHash!==hash||!equal(r.projection,j))return recoveryViolation();
        const replay=producer.kind==='VACANCY'?new JobPostExtractor().extract(p):p;
        for(const k of JOB_CONTENT_FIELDS)if(!equal(j[k],replay[k]))return recoveryViolation();
        if(producer.kind==='VACANCY'&&j.extractionVersion!==replay.extractionVersion)return recoveryViolation();
      }
    }
    // Observation hashes retain their exact raw UTF-8 contract, independently
    // of the historical job hash formula and nullable derived fields.
    const observations=diagnoseContentValidation({jobs:[],historicalJobs:[],discoveryReviews:s.discoveryReviews},'result');
    if(observations.valid!==true)return recoveryViolation();
    return {status:'VERIFIED',reason:'CONTENT_CONTRACT_VERIFIED'};
  }catch{return recoveryBlocked('CONTENT_CONTRACT_UNPROVABLE');}
}

export function compareAcknowledgedCheckpoints(input){return compareCheckpointEvidence(input);}
function compareCheckpointEvidence(input,recovery=false){
  try{
    const {snapshots,artifacts,repository,archiveDigests,plaintextDigests,history}=input??{};
    if(!Array.isArray(artifacts)||artifacts.length!==4||new Set(artifacts.map(a=>a?.id)).size!==4||
      !COMPARISON_ROLES.every(role=>comparisonArtifactValid(artifacts.find(a=>a.id===COMPARISON_PINS[role]),role,repository)))
      return comparisonFailure('ARTIFACT_PROVENANCE_INVALID');
    if(!COMPARISON_ROLES.every(role=>archiveDigests?.[COMPARISON_PINS[role]]===artifacts.find(a=>a.id===COMPARISON_PINS[role]).digest))
      return comparisonFailure('ARTIFACT_DIGEST_MISMATCH');
    if(!object(snapshots)||Object.keys(snapshots).length!==4||!COMPARISON_ROLES.every(role=>comparisonShape(snapshots[role])))return comparisonFailure('CHECKPOINT_SCHEMA_INVALID');
    const {claim,result,originalFinal,syncFinal}=snapshots,completed=SNAPSHOT_ROLES.map(role=>snapshots[role]);
    const contentValidation=recovery?
      contentSummary(SNAPSHOT_ROLES.filter(role=>validateRecoveryContent({snapshot:snapshots[role],producerEvidence:input.producerEvidence}).status!=='VERIFIED').length,[]):
      combinedContentValidation(snapshots);
    const checks={sameAttempt:COMPARISON_ROLES.every(role=>attemptBinding(snapshots[role],result,role==='claim')),
      historicalLedgerEqual:completed.every(s=>equal(protectedLedger(s),protectedLedger(result)))&&
        equal(claim.requestLedger.slice(0,9),result.requestLedger.slice(0,9)),
      terminalReconciliationsEqual:COMPARISON_ROLES.every(role=>equal(snapshots[role].terminalReconciliations,result.terminalReconciliations)),
      historicalTerminalValid:COMPARISON_ROLES.every(role=>{
        const s=snapshots[role],row=s.requestLedger[6];
        return row?.outcome==='FAILED_OR_AMBIGUOUS'&&s.terminalReconciliations.some(r=>r.attemptIndex===6&&
          r.workflowRunId===row.workflowRunId&&r.actualRequests===row.actualRequests&&r.creditsDeducted===row.creditsDeducted&&
          id(r.artifactId)&&id(r.claimArtifactId)&&r.artifactId!==r.claimArtifactId&&/^[a-f0-9]{64}$/u.test(r.stateFingerprint??''));
      }),
      accountingEqual:completed.every(s=>ledgerKnown(s))&&completed.every(s=>equal(s.requestLedger.map(a=>[a.actualRequests,a.creditsDeducted]),
        result.requestLedger.map(a=>[a.actualRequests,a.creditsDeducted]))),
      jobsEqual:completed.every(s=>equal(s.jobs,result.jobs)),historicalJobsEqual:completed.every(s=>equal(s.historicalJobs,result.historicalJobs)),
      observationsEqual:completed.every(s=>equal(s.discoveryReviews,result.discoveryReviews)),contentHashesValid:contentValidation.valid,
      laterHistoryVerified:historyComplete(history)};
    let missingClaim=false,receiptsValid=true;
    for(const s of completed){
      const a=active(s);if(!a){receiptsValid=false;continue;}
      if(a.claimArtifactReceipt===undefined||a.claimArtifactName===undefined)missingClaim=true;
      if(a.claimArtifactReceipt!==undefined&&a.claimArtifactReceipt!==String(COMPARISON_PINS.claim)||
        a.claimArtifactName!==undefined&&a.claimArtifactName!==`socialapis-page-${TARGET.logical}-1-claim`)receiptsValid=false;
    }
    checks.receiptsValid=receiptsValid;
    const finalPage=originalFinal.orchestration.pages.at(-1),resultPage=result.orchestration.pages.at(-1),syncPage=syncFinal.orchestration.pages.at(-1);
    checks.originalAcknowledged=originalFinal.orchestration.phase==='READY'&&active(originalFinal)?.outcome==='CONFIRMED'&&
      finalPage?.outcome==='CONFIRMED'&&finalPage?.artifactReceipt===String(COMPARISON_PINS.result);
    if(finalPage?.artifactReceipt!==undefined&&finalPage.artifactReceipt!==String(COMPARISON_PINS.result))checks.receiptsValid=false;
    checks.completedSource=result.orchestration.phase==='COMPLETED_UNCONFIRMED'&&active(result)?.outcome==='COMPLETED_UNCONFIRMED'&&
      resultPage?.outcome==='COMPLETED_UNCONFIRMED'&&resultPage.artifactReceipt===undefined;
    checks.syncPreservesSource=syncFinal.orchestration.phase===result.orchestration.phase&&
      active(syncFinal)?.outcome===active(result)?.outcome&&syncPage?.outcome===resultPage?.outcome&&syncPage?.artifactReceipt===resultPage?.artifactReceipt;
    const comparisons={resultToOriginalFinal:comparePair(result,originalFinal),resultToSyncFinal:comparePair(result,syncFinal)};
    const contradiction=Object.entries(checks).some(([k,v])=>k!=='laterHistoryVerified'&&!v);
    const unclassified=Object.values(comparisons).some(c=>c.unclassifiedChanges>0);
    const eligibility=contradiction?'NO':missingClaim||!checks.laterHistoryVerified||unclassified?'NOT_YET_DETERMINABLE':'YES';
    const rows=SNAPSHOT_ROLES.map(role=>{
      const s=snapshots[role],a=active(s),known=s.requestLedger.every(r=>Number.isFinite(r.actualRequests)&&Number.isFinite(r.creditsDeducted));
      return {role,artifactId:String(COMPARISON_PINS[role]),phase:label(s.orchestration.phase,phases),
        status:label(s.orchestration.status,statuses),
        stopReason:s.orchestration.stopReason===null?null:label(s.orchestration.stopReason,[...stopReasons,'REQUEST_BUDGET','CREDIT_BUDGET','DAILY_REQUEST_BUDGET']),
        activeOutcome:label(a?.outcome,outcomes),workflowRunId:id(s.orchestration.workflowRunId),
        attemptIndex:Number.isSafeInteger(s.orchestration.attemptIndex)&&s.orchestration.attemptIndex>=0?s.orchestration.attemptIndex:null,
        actualRequests:known?s.requestLedger.reduce((n,v)=>n+v.actualRequests,0):null,
        creditsDeducted:known?s.requestLedger.reduce((n,v)=>n+v.creditsDeducted,0):null,
        claimReceipt:id(a?.claimArtifactReceipt),lastPageReceipt:id(s.orchestration.pages.at(-1)?.artifactReceipt),
        lastPageOutcome:label(s.orchestration.pages.at(-1)?.outcome,outcomes),
        workflowSlot:s.requestLedger.slice(0,10).filter(row=>row.workflowRunId===TARGET.logical).length,
        pageNumber:Number.isSafeInteger(s.orchestration.pages.at(-1)?.pageNumber)?s.orchestration.pages.at(-1).pageNumber:null,
        lastPageFingerprint:semanticHash(s.orchestration.pages.at(-1)??null),
        archiveDigest:archiveDigests[COMPARISON_PINS[role]],
        plaintextFingerprint:/^[a-f0-9]{64}$/u.test(plaintextDigests?.[role]??'')?plaintextDigests[role]:null,
        stateFingerprint:fingerprint(s),canonicalFingerprint:semanticHash(s),ledgerFingerprint:semanticHash(s.requestLedger),
        jobsFingerprint:semanticHash(s.jobs),historicalJobsFingerprint:semanticHash(s.historicalJobs??null),
        observationsFingerprint:semanticHash(s.discoveryReviews),terminalReconciliationsFingerprint:semanticHash(s.terminalReconciliations),
        registryCount:s.jobs.length,observationCount:s.discoveryReviews.reduce((n,b)=>n+(Array.isArray(b.items)?b.items.length:0),0)};
    });
    const resultReport={ok:true,eligibility,fingerprintVersion:'CANONICAL_JSON_V1',protectedFingerprint:semanticHash(protectedState(result)),
      ledgerRowsCompared:Math.max(...completed.map(s=>s.requestLedger.length)),snapshots:rows,checks,comparisons,contentValidation,
      artifactProvenance:COMPARISON_ROLES.map(role=>{
        const a=artifacts.find(v=>v.id===COMPARISON_PINS[role]);
        const timestamp=v=>typeof v==='string'&&Number.isFinite(Date.parse(v))?new Date(v).toISOString():null;
        return {role,id:String(a.id),name:a.name,uploaderRunId:id(a.workflow_run.id),repositoryId:repository.id,
          digest:a.digest,expired:false,createdAt:timestamp(a.created_at),expiresAt:timestamp(a.expires_at)};
      }),
      laterHistory:{verified:checks.laterHistoryVerified,runCount:Array.isArray(history?.runs)?history.runs.length:null,
        fingerprint:object(history)?semanticHash(history):null},
      reasons:[...(contradiction?['PROTECTED_STATE_OR_ACK_INVARIANT_FAILED']:[]),...(missingClaim?['CLAIM_BINDING_MISSING']:[]),
        ...(!checks.laterHistoryVerified?['LATER_HISTORY_NOT_VERIFIED']:[]),...(unclassified?['UNCLASSIFIED_SNAPSHOT_DIFFERENCES']:[])],
      recoveryExecuted:false};
    // Preserve counts/decision while making any output-size truncation explicit.
    // Never dump a large failure list or treat an omitted failure as a pass.
    while(Buffer.byteLength(JSON.stringify(resultReport))>MAX_OUTPUT&&contentValidation.failures.length){
      contentValidation.failures.pop();
      contentValidation.reportedFailures=contentValidation.failures.length;
      contentValidation.truncated=true;
    }
    return Buffer.byteLength(JSON.stringify(resultReport))<=MAX_OUTPUT?resultReport:comparisonFailure('OUTPUT_LIMIT_EXCEEDED');
  }catch{return comparisonFailure('CHECKPOINT_SCHEMA_INVALID');}
}

// Selection returns metadata, not repaired state. Every non-content invariant
// is re-evaluated by the same comparator; a supplied eligibility flag is unused.
export function selectAcknowledgedRecoverySource(input){
  const prior=compareAcknowledgedCheckpoints(input);
  if(!prior.ok)return recoveryBlocked(prior.errorCode);
  for(const role of SNAPSHOT_ROLES){
    const content=validateRecoveryContent({snapshot:input.snapshots[role],producerEvidence:input.producerEvidence});
    if(content.status!=='VERIFIED')return recoveryBlocked(content.reason);
  }
  const comparison=compareCheckpointEvidence(input,true);
  if(!comparison.ok)return recoveryBlocked(comparison.errorCode);
  if(comparison.eligibility!=='YES')return recoveryBlocked(comparison.reasons[0]??'RECOVERY_EVIDENCE_INCOMPLETE');
  return {status:'SELECTED',artifactId:COMPARISON_PINS.originalFinal,workflowRunId:TARGET.logical,
    stateFingerprint:fingerprint(input.snapshots.originalFinal),reason:'ACKNOWLEDGED_FINAL_VERIFIED'};
}

export async function inspectAcknowledgedCheckpoints({github,key}={}){
  return readAcknowledgedEvidence({github,key},input=>compareAcknowledgedCheckpoints(input));
}
async function readAcknowledgedEvidence({github,key},consume){
  let finalCiphertext;
  try{
    if(typeof key!=='string'||!/^[a-f0-9]{64}$/iu.test(key))return comparisonFailure('CHECKPOINT_CREDENTIALS_UNAVAILABLE');
    const repository=await github.getRepository(),artifacts=[];
    for(const role of COMPARISON_ROLES){
      const a=await github.getArtifact(COMPARISON_PINS[role]);
      if(!comparisonArtifactValid(a,role,repository))return comparisonFailure('ARTIFACT_PROVENANCE_INVALID');
      artifacts.push(a);
    }
    const snapshots={},archiveDigests={},plaintextDigests={};
    for(const [index,role] of COMPARISON_ROLES.entries()){
      let ciphertext,plaintext;
      try{
        const archive=await github.downloadArtifact(COMPARISON_PINS[role]);
        if(!Buffer.isBuffer(archive)||archive.length>MAX_ARCHIVE)return comparisonFailure('CHECKPOINT_ARCHIVE_INVALID');
        const archiveDigest=`sha256:${sha(archive)}`;archiveDigests[COMPARISON_PINS[role]]=archiveDigest;
        if(archiveDigest!==artifacts[index].digest)return comparisonFailure('ARTIFACT_DIGEST_MISMATCH');
        try{ciphertext=ciphertextFromZip(archive);}catch{return comparisonFailure('CHECKPOINT_ARCHIVE_INVALID');}
        try{plaintext=decryptCheckpoint(ciphertext,key);}catch{return comparisonFailure('CHECKPOINT_DECRYPTION_FAILED');}
        if(role==='originalFinal')finalCiphertext=Buffer.from(ciphertext);
        plaintextDigests[role]=sha(plaintext);
        try{snapshots[role]=JSON.parse(plaintext.toString('utf8'));}catch{return comparisonFailure('CHECKPOINT_SCHEMA_INVALID');}
      }finally{plaintext?.fill(0);ciphertext?.fill(0);}
    }
    let history;
    try{history=await github.getHistoryEvidence();}catch{history=undefined;}
    return await consume({repository,artifacts,snapshots,archiveDigests,plaintextDigests,history},finalCiphertext);
  }catch{return comparisonFailure('CHECKPOINT_INSPECTION_FAILED');}
  finally{finalCiphertext?.fill(0);}
}

// Builds evidence only from bounded authenticated REST reads. The pure comparator
// consumes this internal port, never a workflow-provided assertion of authority.
async function laterHistory(api,currentRunId){
  const {REVIEWED_WORKFLOW_HASHES,workflowHash,HISTORY_LIMITS}=await import('./checkpoint-history.mjs');
  const {parse}=await import('yaml');
  const query='/actions/workflows/socialapis-job-discovery.yml/runs?branch=main&event=workflow_dispatch&per_page=20&page=';
  const list=async()=>{
    const runs=[];let total,last=Infinity;
    for(let page=1;page<=HISTORY_LIMITS.pages;page++){
      const data=await api.json(query+page,512*1024);
      if(!Array.isArray(data.workflow_runs)||data.workflow_runs.length>20||!Number.isSafeInteger(data.total_count)||data.total_count<1||
        total!==undefined&&total!==data.total_count)throw new Error('HISTORY_UNVERIFIED');
      total=data.total_count;
      for(const run of data.workflow_runs){
        if(!id(run.id)||!Number.isSafeInteger(run.run_number)||run.run_number>=last||runs.length>=HISTORY_LIMITS.runs)throw new Error('HISTORY_UNVERIFIED');
        last=run.run_number;runs.push(run);
        if(String(run.id)===TARGET.logical)return {total,runs};
      }
      if(data.workflow_runs.length!==20)throw new Error('HISTORY_UNVERIFIED');
    }
    throw new Error('HISTORY_UNVERIFIED');
  };
  const before=await list(),records=[];
  if(currentRunId&&!before.runs.some(r=>id(r.id)===currentRunId))throw new Error('HISTORY_UNVERIFIED');
  for(const run of before.runs){
    const restoring=currentRunId&&id(run.id)===currentRunId;
    if(run.path!=='.github/workflows/socialapis-job-discovery.yml'||run.head_branch!=='main'||run.event!=='workflow_dispatch'||
      run.head_repository?.full_name!==TARGET.repo||run.run_attempt!==1||
      (restoring?run.status!=='in_progress'||run.conclusion!==null:run.status!=='completed'||!['success','failure'].includes(run.conclusion))||
      !/^[a-f0-9]{40}$/u.test(run.head_sha??''))throw new Error('HISTORY_UNVERIFIED');
    const file=await api.json(`/contents/.github/workflows/socialapis-job-discovery.yml?ref=${run.head_sha}`,128*1024);
    if(file.encoding!=='base64'||typeof file.content!=='string'||file.content.length>90_000)throw new Error('HISTORY_UNVERIFIED');
    const source=Buffer.from(file.content,'base64').toString('utf8');
    if(!REVIEWED_WORKFLOW_HASHES.has(workflowHash(source)))throw new Error('HISTORY_UNVERIFIED');
    const defs=parse(source)?.jobs?.discover?.steps;
    if(!Array.isArray(defs))throw new Error('HISTORY_UNVERIFIED');
    const jobs=[];let count;
    for(let page=1;page<=HISTORY_LIMITS.metadataPages;page++){
      const data=await api.json(`/actions/runs/${run.id}/jobs?filter=all&per_page=100&page=${page}`,512*1024);
      if(!Array.isArray(data.jobs)||!Number.isSafeInteger(data.total_count)||count!==undefined&&count!==data.total_count)throw new Error('HISTORY_UNVERIFIED');
      count=data.total_count;jobs.push(...data.jobs);if(jobs.length===count)break;
      if(data.jobs.length!==100||page===HISTORY_LIMITS.metadataPages)throw new Error('HISTORY_UNVERIFIED');
    }
    if(jobs.length!==1)throw new Error('HISTORY_UNVERIFIED');
    const job=jobs[0];
    if(job.name!=='discover'||job.run_id!==run.id||job.run_attempt!==1||job.status!==run.status||job.conclusion!==run.conclusion||!Array.isArray(job.steps))throw new Error('HISTORY_UNVERIFIED');
    const steps=job.steps.filter(s=>s.name!=='Set up job'&&s.name!=='Complete job'&&!s.name.startsWith('Post '));
    if((restoring?steps.length>defs.length:steps.length!==defs.length)||steps.some((s,i)=>(!restoring&&s.status!=='completed')||!Number.isSafeInteger(s.number)||i>0&&s.number<=steps[i-1].number||
      s.name!==(defs[i].name??`Run ${defs[i].uses??defs[i].run.split('\n')[0]}`)))throw new Error('HISTORY_UNVERIFIED');
    if(restoring){await activeRestoreRun(run,defs,steps);continue;}
    const providerSteps=steps.filter((_s,i)=>defs[i].uses==='./.github/actions/socialapis-discovery-page').map(s=>({status:s.status,conclusion:s.conclusion}));
    if(![1,6].includes(providerSteps.length))throw new Error('HISTORY_UNVERIFIED');
    if(String(run.id)!==TARGET.logical)records.push({id:String(run.id),status:run.status,runAttempt:run.run_attempt,conclusion:run.conclusion,providerSteps});
  }
  const after=await list();
  // Full run metadata equality also detects transitions during inspection.
  return {source:'AUTHENTICATED_GITHUB_GET',complete:true,sourceProvenanceVerified:true,
    stable:equal(before,after),runs:records};
}

export async function inspectAcknowledgedFromGitHub({token,key,transport=fetch,historyReader}={}){
  try{
    if(typeof token!=='string'||!token||typeof key!=='string'||!/^[a-f0-9]{64}$/iu.test(key))return comparisonFailure('CHECKPOINT_CREDENTIALS_UNAVAILABLE');
    const api=githubReads(token,transport,AbortSignal.timeout(240_000));
    return await inspectAcknowledgedCheckpoints({key,github:{getRepository:()=>api.json(''),
      getArtifact:artifactId=>api.json(`/actions/artifacts/${artifactId}`),downloadArtifact:api.downloadArtifact,
      getHistoryEvidence:()=>historyReader?historyReader():laterHistory(api)}});
  }catch{return comparisonFailure('CHECKPOINT_INSPECTION_FAILED');}
}

// The active Discovery run is excluded ONLY while its reviewed decrypt step is
// actually running before every provider action in the reviewed serial source.
// GitHub may omit future steps; the complete current prefix proves execution
// has not reached them. This exemption never applies to historical runs.
// Completed/unknown/provider-started runs never acquire this exemption.
async function activeRestoreRun(run,defs,steps){
  const current=steps.findIndex(s=>s.status==='in_progress');
  const restore=defs.findIndex(s=>s.name==='Decrypt restored checkpoint (legacy plaintext remains supported)');
  const providerIndexes=defs.flatMap((s,i)=>s.uses==='./.github/actions/socialapis-discovery-page'?[i]:[]);
  if(run.status!=='in_progress'||current!==restore||restore<0||!providerIndexes.length||
    steps.filter(s=>s.status==='in_progress').length!==1||
    steps.slice(0,restore).some(s=>s.status!=='completed'||!['success','skipped'].includes(s.conclusion))||
    providerIndexes.some(i=>i<=restore||i<steps.length&&(!['queued','pending'].includes(steps[i].status)||steps[i].conclusion!==null||steps[i].started_at)))
    throw new Error('HISTORY_UNVERIFIED');
}

// Internal restore port: callers must never log this result's encrypted bytes.
// It returns the ORIGINAL ciphertext only after proof-based source selection.
export async function restoreAcknowledgedCheckpoints({github,key,restoredState,artifactId}={}){
  if(!Object.values(COMPARISON_PINS).some(v=>id(v)===id(artifactId)))return recoveryBlocked('RESTORED_SOURCE_UNVERIFIED');
  const outcome=await readAcknowledgedEvidence({github,key},async(input,ciphertext)=>{
    const role=COMPARISON_ROLES.find(r=>id(COMPARISON_PINS[r])===id(artifactId));
    if(!equal(restoredState,input.snapshots[role]))return recoveryBlocked('RESTORED_SOURCE_MISMATCH');
    input.producerEvidence=await github.getProducerEvidence(input);
    const selection=selectAcknowledgedRecoverySource(input);
    if(selection.status!=='SELECTED')return selection;
    return {...selection,encryptedCheckpoint:Buffer.from(ciphertext)};
  });
  return outcome.ok===false?recoveryBlocked(outcome.errorCode):outcome;
}
export async function restoreAcknowledgedFromGitHub({token,key,restoredState,artifactId,currentRunId,transport=fetch}={}){
  try{
    if(typeof token!=='string'||!token||typeof key!=='string'||!/^[a-f0-9]{64}$/iu.test(key)||!id(currentRunId))
      return recoveryBlocked('CHECKPOINT_CREDENTIALS_UNAVAILABLE');
    const api=githubReads(token,transport,AbortSignal.timeout(240_000));
    return await restoreAcknowledgedCheckpoints({key,restoredState,artifactId,github:{
      getRepository:()=>api.json(''),getArtifact:v=>api.json(`/actions/artifacts/${v}`),downloadArtifact:api.downloadArtifact,
      getHistoryEvidence:()=>laterHistory(api,id(currentRunId)),getProducerEvidence:input=>recoveryProducerEvidence(api,input)}});
  }catch{return recoveryBlocked('RECOVERY_SOURCE_UNVERIFIED');}
}
