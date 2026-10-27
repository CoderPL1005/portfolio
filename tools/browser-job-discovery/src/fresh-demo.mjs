import {createHash} from 'node:crypto';
import {Stop} from './core.mjs';
import {beginExecution,reconcileTerminalFailure,acknowledgePersistence} from './pagination-orchestrator.mjs';
import {resolveReviewSyncScope} from './admin-sync.mjs';

const hash=value=>createHash('sha256').update(JSON.stringify(value)).digest('hex');
const positive=value=>/^[1-9]\d*$/u.test(String(value??''));
function validateCheckpointProof(state,proof){
  const invalid=()=>{throw new Stop('FRESH_AUDIT_EVIDENCE_INVALID');};
  if(proof.artifactKind!=='CHECKPOINT'||!positive(proof.artifactUploadRunId)||
    !['socialapis-discovery-state','socialapis-discovery-checkpoint'].includes(proof.artifactName))invalid();
  // A checkpoint receipt is not a provider result receipt. Never acknowledge or
  // reconcile an unresolved attempt using a later uploader's artifact.
  if(!['READY','TERMINAL_RECONCILED'].includes(state.orchestration.phase))throw new Stop('FRESH_AUDIT_AMBIGUOUS');
  if(proof.artifactUploadRunId===state.orchestration.workflowRunId)return;
  const sync=state.adminSync;
  if(proof.artifactName!=='socialapis-discovery-state'||sync?.mode!=='recovery'||
    sync.executingWorkflowRunId!==proof.artifactUploadRunId||!positive(sync.workflowRunId)||
    typeof sync.completedAt!=='string'||!Number.isFinite(Date.parse(sync.completedAt))||!Array.isArray(sync.summaries))invalid();
  let selected;
  try{selected=resolveReviewSyncScope(state,{mode:'recovery',executingWorkflowRunId:proof.artifactUploadRunId,targetWorkflowRunId:sync.workflowRunId});}
  catch{invalid();}
  if(sync.observationRunId!==selected.scope.observationRunId||sync.summaries.length!==selected.bundles.length||
    sync.observationCount!==selected.bundles.reduce((sum,b)=>sum+b.items.length,0))invalid();
  for(const [index,bundle] of selected.bundles.entries()){
    const receipt=sync.summaries[index];
    if(receipt?.runId!==bundle.runId||receipt.pageNumber!==bundle.pageNumber||
      !Number.isInteger(receipt.inserted)||receipt.inserted<0||!Number.isInteger(receipt.unchanged)||receipt.unchanged<0||
      receipt.inserted+receipt.unchanged!==bundle.items.length)invalid();
  }
}
export function freshClaimName(id){
  if(typeof id!=='string'||!/^fresh-[a-z0-9][a-z0-9-]{7,63}$/u.test(id))throw new Stop('FRESH_SESSION_ID_INVALID');
  return `discovery-${id}-claimed`;
}

// Only the operational session is empty. Request accounting comes from a
// verified historical result/checkpoint, never an empty cache or new namespace.
export function createFreshSession(previous,input,{sessionId,workflowRunId,activationSha,proof,freeConfirmed,noCashConfirmed,now=new Date()}={}){
  const claimTag=freshClaimName(sessionId);
  if(!/^[a-f0-9]{40}$/u.test(activationSha??'')||!/^[1-9]\d*$/u.test(String(workflowRunId??'')))throw new Stop('FRESH_ACTIVATION_REQUIRED');
  if(!freeConfirmed||!noCashConfirmed)throw new Stop('FRESH_FREE_CREDIT_CONFIRMATION_REQUIRED');
  if(input.maxRequests!==1||input.maxCredits!==1||input.mode&&input.mode!=='development')throw new Stop('FRESH_ONE_REQUEST_BUDGET_REQUIRED');
  if(previous.freshSession?.id===sessionId||previous.freshSession?.workflowRunId===String(workflowRunId))throw new Stop('FRESH_SESSION_ALREADY_INITIALIZED');
  if(!Array.isArray(previous.jobs)||!Array.isArray(previous.requestLedger)||!previous.orchestration)throw new Stop('FRESH_GLOBAL_AUDIT_REQUIRED');
  if(previous.requestLedger.some(a=>a.workflowRunId===String(workflowRunId)))throw new Stop('FRESH_EXECUTION_ALREADY_RESERVED');
  const copy=structuredClone(previous),sourceFingerprint=hash(previous),oldRun=copy.orchestration;
  const checkpointProof=proof?.provenance==='ACTIONS_CHECKPOINT';
  if(proof?.stateFingerprint!==sourceFingerprint||proof?.workflowRunId!==oldRun.workflowRunId||!positive(proof?.artifactId)||
    !(checkpointProof||proof?.provenance==='ACTIONS_RESULT'&&proof.artifactKind==='RESULT')||
    !checkpointProof&&proof.artifactUploadRunId!==undefined&&proof.artifactUploadRunId!==oldRun.workflowRunId)
    throw new Stop('FRESH_AUDIT_EVIDENCE_INVALID');
  if(checkpointProof)validateCheckpointProof(copy,proof);
  // An old terminal failure is not an operational prerequisite: resolve its
  // audit evidence on a COPY, then discard jobs/plan/cursor, not the ledger.
  if(['CLAIMED','TERMINAL_UNCONFIRMED'].includes(oldRun.phase))reconcileTerminalFailure(copy,proof);
  else if(oldRun.phase==='IN_FLIGHT')throw new Stop('FRESH_AUDIT_AMBIGUOUS');
  else if(oldRun.phase==='COMPLETED_UNCONFIRMED'){
    const slot=copy.requestLedger.slice(0,oldRun.attemptIndex+1).filter(a=>a.workflowRunId===oldRun.workflowRunId).length;
    const attempt=copy.requestLedger[oldRun.attemptIndex];
    if(proof?.workflowRunId!==oldRun.workflowRunId||proof?.attemptIndex!==oldRun.attemptIndex||proof?.stateFingerprint!==sourceFingerprint||
      proof?.artifactKind!=='RESULT'||proof?.provenance!=='ACTIONS_RESULT'||proof?.artifactName!==`socialapis-page-${oldRun.workflowRunId}-${slot}-result`||
      !/^[1-9]\d*$/u.test(String(proof.artifactId??''))||!/^[1-9]\d*$/u.test(String(proof.claimArtifactId??''))||
      String(proof.artifactId)===String(proof.claimArtifactId)||attempt?.actualRequests!==1||attempt.creditsDeducted===null||
      attempt.claimArtifactReceipt!==undefined&&attempt.claimArtifactReceipt!==String(proof.claimArtifactId))throw new Stop('FRESH_AUDIT_EVIDENCE_INVALID');
    acknowledgePersistence(copy,proof.artifactId,proof);
  }
  for(const [index,a] of copy.requestLedger.entries()){
    const closed=a.outcome==='CONFIRMED'||copy.terminalReconciliations?.some(r=>r.attemptIndex===index&&r.workflowRunId===a.workflowRunId&&
      r.actualRequests===a.actualRequests&&r.creditsDeducted===a.creditsDeducted);
    if(!closed||a.actualRequests!==1||!Number.isFinite(a.creditsDeducted)||a.creditsDeducted<0||a.creditsDeducted>1)throw new Stop('FRESH_AUDIT_AMBIGUOUS');
  }
  const historicalJobs=[...new Map([...(copy.historicalJobs??[]),...copy.jobs].map(job=>[job.sourceIdentity,job])).values()];
  const state={evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[],historicalJobs,discoveryYieldHistory:copy.discoveryYieldHistory??[],
    discoveryReviews:copy.discoveryReviews??[],requestLedger:copy.requestLedger,
    terminalReconciliations:copy.terminalReconciliations??[],
    freshSession:{version:1,id:sessionId,workflowRunId:String(workflowRunId),claimTag,activationSha,auditSourceFingerprint:sourceFingerprint,
      sourceArtifactId:proof.artifactId,sourceWorkflowRunId:oldRun.workflowRunId,
      sourceArtifactUploadRunId:checkpointProof?proof.artifactUploadRunId:proof.workflowRunId,
      historicalRequests:copy.requestLedger.reduce((n,a)=>n+a.actualRequests,0),
      historicalCredits:copy.requestLedger.reduce((n,a)=>n+a.creditsDeducted,0),historicalRegistryFingerprint:hash(previous.jobs)}};
  return beginExecution(state,{...input,mode:'development'},String(workflowRunId),{now});
}
