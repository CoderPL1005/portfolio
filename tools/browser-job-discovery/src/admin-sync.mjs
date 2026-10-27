import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { Stop } from './core.mjs';

const MAX_CHECKPOINT = 12 * 1024 * 1024;
const AAD = Buffer.from('portfolio-discovery-checkpoint-v1');
function key(value) {
  if (typeof value !== 'string' || !/^[a-f0-9]{64}$/iu.test(value)) throw new Stop('PRIVATE_CHECKPOINT_KEY_REQUIRED');
  return Buffer.from(value, 'hex');
}
// Only authenticated ciphertext may be uploaded to artifacts on a public repo.
export function encryptCheckpoint(bytes, secret) {
  if (!Buffer.isBuffer(bytes) || bytes.length > MAX_CHECKPOINT) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
  const nonce = randomBytes(12), cipher = createCipheriv('aes-256-gcm', key(secret), nonce);
  cipher.setAAD(AAD);
  const data = Buffer.concat([cipher.update(bytes), cipher.final()]);
  return Buffer.concat([Buffer.from('DSC1'), nonce, cipher.getAuthTag(), data]);
}
export function decryptCheckpoint(bytes, secret) {
  if (!Buffer.isBuffer(bytes) || bytes.length < 32 || bytes.length > MAX_CHECKPOINT + 32 || bytes.subarray(0, 4).toString() !== 'DSC1')
    throw new Stop('PRIVATE_CHECKPOINT_INVALID');
  try {
    const decipher = createDecipheriv('aes-256-gcm', key(secret), bytes.subarray(4, 16));
    decipher.setAAD(AAD); decipher.setAuthTag(bytes.subarray(16, 32));
    return Buffer.concat([decipher.update(bytes.subarray(32)), decipher.final()]);
  } catch { throw new Stop('PRIVATE_CHECKPOINT_INVALID'); }
}
export function syncConfiguration(env = process.env) {
  let origin;
  try { origin = new URL(env.DISCOVERY_ADMIN_ORIGIN); } catch { throw new Stop('ADMIN_SYNC_CONFIGURATION_REQUIRED'); }
  // Explicit trusted deployment boundary. Do not send credentials to arbitrary input URLs.
  if (origin.href !== 'https://portfolio-api-forn.onrender.com/' || !/^[a-f0-9]{64}$/iu.test(env.DISCOVERY_IMPORT_KEY ?? ''))
    throw new Stop('ADMIN_SYNC_CONFIGURATION_REQUIRED');
  return {origin:origin.origin,secret:env.DISCOVERY_IMPORT_KEY};
}
async function request(config, path, bundle, transport) {
  const signal=AbortSignal.timeout(20_000);
  let response;
  try {
    response=await transport(new URL(path,config.origin),{method:bundle?'POST':'GET',redirect:'error',credentials:'omit',signal,
      headers:{'X-Discovery-Key':config.secret,...(bundle?{'Content-Type':'application/json'}:{})},
      ...(bundle?{body:JSON.stringify(bundle)}:{})});
    if (!response.ok) throw new Error();
    // Never surface raw response/provider diagnostics or any credentials.
    const reader=response.body?.getReader();if(!reader)throw new Error();
    const chunks=[];let size=0;
    try{while(true){const {done,value}=await reader.read();if(done)break;size+=value.byteLength;
      if(size>4096){await reader.cancel();throw new Error();}chunks.push(Buffer.from(value));}}
    finally{reader.releaseLock();}
    const text=Buffer.concat(chunks).toString('utf8');
    const body=JSON.parse(text);
    return body.data;
  } catch { throw new Stop('ADMIN_SYNC_FAILED_RETRY_SYNC_ONLY'); }
}
export async function preflightAdminSync(config, transport=fetch) {
  if((await request(config,'/api/v1/discovery-sync/ready',null,transport))?.ready!==true)throw new Stop('ADMIN_SYNC_NOT_READY');
}
// Scope comes from saved execution metadata and its request ledger, never from
// the new recovery dispatch ID or the presence/order of historical bundles.
export function resolveReviewSyncScope(state,{mode='current',executingWorkflowRunId,targetWorkflowRunId}={}) {
  const workflowId=value=>typeof value==='string' && /^[1-9]\d*$/u.test(value);
  const saved=state.orchestration?.workflowRunId;
  if(!workflowId(saved) || !['current','recovery'].includes(mode) ||
    executingWorkflowRunId!==undefined && !workflowId(executingWorkflowRunId) ||
    mode==='current' && (targetWorkflowRunId!==undefined || executingWorkflowRunId!==undefined && executingWorkflowRunId!==saved) ||
    targetWorkflowRunId!==undefined && !workflowId(targetWorkflowRunId))throw new Stop('ADMIN_SYNC_RUN_ID_INVALID_OR_INCONSISTENT');
  const workflowRunId=targetWorkflowRunId??saved;
  const fresh=state.freshSession;
  if(fresh!==undefined && (!workflowId(fresh?.workflowRunId) || typeof fresh.id!=='string' || !/^fresh-[a-z0-9][a-z0-9-]{7,63}$/u.test(fresh.id)))
    throw new Stop('ADMIN_SYNC_FRESH_ID_INVALID');
  const observationRunId=fresh?.workflowRunId===workflowRunId?fresh.id:workflowRunId;
  if(!Array.isArray(state.discoveryReviews))throw new Stop('NO_CAPTURED_REVIEW_OBSERVATIONS');
  if(observationRunId!==workflowRunId && state.discoveryReviews.some(b=>b?.runId===workflowRunId))throw new Stop('ADMIN_SYNC_RUN_ID_AMBIGUOUS');
  const bundles=state.discoveryReviews.filter(b=>b?.runId===observationRunId);
  if(!bundles.length)throw new Stop('NO_CAPTURED_REVIEW_OBSERVATIONS_FOR_RUN');
  if(!Array.isArray(state.requestLedger) || new Set(bundles.map(b=>JSON.stringify([b.query,b.pageNumber]))).size!==bundles.length)
    throw new Stop('ADMIN_SYNC_RUN_PROVENANCE_INVALID');
  for(const b of bundles){
    const attempts=state.requestLedger.filter(a=>a?.workflowRunId===workflowRunId && a.query===b.query && a.pageNumber===b.pageNumber);
    if(b.schemaVersion!==1 || b.provider!=='SOCIALAPIS' || typeof b.query!=='string' || !b.query.trim() || b.query.length>300 || b.query.includes('\0') ||
      !Number.isInteger(b.pageNumber)||b.pageNumber<1||b.pageNumber>6||!Array.isArray(b.items)||b.items.length>200 ||
      attempts.length!==1 || attempts[0].actualRequests!==1 || !Number.isFinite(attempts[0].creditsDeducted) || attempts[0].creditsDeducted<0 ||
      !['CONFIRMED','COMPLETED_UNCONFIRMED','FAILED_OR_AMBIGUOUS'].includes(attempts[0].outcome))throw new Stop('ADMIN_SYNC_RUN_PROVENANCE_INVALID');
  }
  if(!bundles.some(b=>b.items.length))throw new Stop('NO_CAPTURED_REVIEW_OBSERVATIONS_FOR_RUN');
  return {scope:{mode,workflowRunId,observationRunId,executingWorkflowRunId:executingWorkflowRunId??null},
    bundles:bundles.sort((a,b)=>a.pageNumber-b.pageNumber)};
}
export async function syncReviews(state, config, transport=fetch, options={}) {
  const {scope,bundles}=resolveReviewSyncScope(state,options);
  const summaries=[];
  for(const bundle of bundles){
    const result=await request(config,'/api/v1/discovery-sync/import',bundle,transport);
    if(!Number.isInteger(result?.inserted)||!Number.isInteger(result?.unchanged)||result.inserted<0||result.unchanged<0||
      result.inserted+result.unchanged!==bundle.items.length)throw new Stop('ADMIN_SYNC_RECEIPT_INVALID');
    summaries.push({runId:bundle.runId,pageNumber:bundle.pageNumber,inserted:result.inserted,unchanged:result.unchanged});
  }
  // No mutation of registry/ledger or Human Review. Repeating uses identical payloads.
  state.adminSync={...scope,completedAt:new Date().toISOString(),observationCount:bundles.reduce((sum,b)=>sum+b.items.length,0),summaries};
  return summaries;
}
