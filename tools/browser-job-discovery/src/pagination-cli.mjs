import { mkdir, readFile, stat, writeFile, rename, unlink, appendFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { randomUUID, createHash } from 'node:crypto';
import { resolve, relative, isAbsolute } from 'node:path';
import { Stop } from './core.mjs';
import { beginExecution, claimPage, performPage, acknowledgePersistence, workflowMetrics, reconcileTerminalFailure } from './pagination-orchestrator.mjs';
import { resolveDiscoveryPolicy, planFacebookQueries, resolveOneRequestPolicy } from './candidate-technology-profile.mjs';
import {encryptCheckpoint,decryptCheckpoint,syncConfiguration,preflightAdminSync,syncReviews} from './admin-sync.mjs';
import {createFreshSession} from './fresh-demo.mjs';

const hash=value=>createHash('sha256').update(value).digest('hex');
function actionsResultEvidence(state) {
  return {artifactKind:'RESULT',provenance:'ACTIONS_RESULT',artifactName:process.env.ARTIFACT_NAME,
    workflowRunId:process.env.ARTIFACT_RUN_ID,claimArtifactId:process.env.CLAIM_ARTIFACT_RECEIPT,
    attemptIndex:state.orchestration.attemptIndex,stateFingerprint:hash(JSON.stringify(state))};
}
// Offline recovery uses an immutable source + operator-provided observed result
// metadata. It never downloads, overwrites evidence, or performs a provider call.
async function reconcileLocalResult() {
  const source=process.env.DISCOVERY_RECONCILE_SOURCE,metadata=process.env.DISCOVERY_RECONCILE_METADATA,output=process.env.DISCOVERY_RECONCILE_OUTPUT;
  if(!source || !metadata || !output)throw new Stop('RECONCILIATION_FILES_REQUIRED');
  const target=resolve(output),repo=fileURLToPath(new URL('../../../',import.meta.url)),inside=relative(repo,target);
  if(target===resolve(source) || target===resolve(metadata) || !inside.startsWith('..') && !isAbsolute(inside))throw new Stop('PRIVATE_RECOVERY_COPY_REQUIRED');
  if((await stat(source)).size>1_048_576 || (await stat(metadata)).size>65_536)throw new Stop('RECONCILIATION_EVIDENCE_TOO_LARGE');
  const bytes=await readFile(source),observed=JSON.parse(await readFile(metadata,'utf8')),state=JSON.parse(bytes.toString('utf8'));
  const run=state.orchestration,attempt=state.requestLedger?.[run?.attemptIndex],values=observed.effectiveValuesFromPersistedState;
  if(observed.metadataKind!=='OBSERVED_FROM_PERSISTED_RESULT_NOT_DISPATCH_INPUTS' || observed.resultUploaded!==true ||
    observed.stateJsonSHA256!==hash(bytes) || observed.workflowRunId!==run?.workflowRunId ||
    !observed.observedRun || Object.entries(observed.observedRun).some(([key,value])=>run[key]!==value) ||
    !values || Object.entries(values).some(([key,value])=>attempt?.[key]!==value) ||
    JSON.stringify(observed.itemStats)!==JSON.stringify(attempt?.itemStats))throw new Stop('TERMINAL_RECONCILIATION_EVIDENCE_INVALID');
  reconcileTerminalFailure(state,{artifactId:observed.resultArtifactId,claimArtifactId:observed.claimArtifactId,
    workflowRunId:observed.workflowRunId,attemptIndex:run.attemptIndex,artifactKind:'RESULT',provenance:'LOCAL_OBSERVED_RESULT_HASH',
    stateFingerprint:hash(JSON.stringify(state))});
  state.workflowMetrics=workflowMetrics(state);
  // Exclusive create also rejects aliases/hardlinks to either existing source.
  const json=JSON.stringify(state,null,2)+'\n';
  if(Buffer.byteLength(json)>1_048_576)throw new Stop('CHECKPOINT_CAPACITY_REACHED');
  await writeFile(target,json,{flag:'wx',mode:0o600});
  console.log(JSON.stringify({offlineRecovery:true,workflowRunId:run.workflowRunId,phase:run.phase,stopReason:run.stopReason,
    actualRequests:attempt.actualRequests,creditsDeducted:attempt.creditsDeducted,rejectedItems:attempt.itemStats.rejectedItems,
    registryJobs:state.jobs.length,providerRequestsMade:0,originalEvidenceModified:false}));
}

function dateSelection(name,policyValue) {
  const value=process.env[name],inputState=value===undefined?'OMITTED':value===''?'BLANK':'PROVIDED';
  return {value:inputState==='PROVIDED'?value:policyValue??undefined,inputState,
    source:inputState==='PROVIDED'?'ENV_INPUT':policyValue!==undefined && policyValue!==null?'POLICY':'UNSET'};
}

// Existing CLI entrypoint; action commands make the artifact/HTTP boundary explicit.
export async function paginationCli(args) {
  const action = args[args.indexOf('--orchestrate') + 1];
  if (args.length !== 2 || !['init','fresh-init', 'claim', 'page', 'ack', 'summary','reconcile','seal','unseal','sync-ready','sync'].includes(action)) throw new Stop('INVALID_ORCHESTRATION_COMMAND');
  if(action==='reconcile')return reconcileLocalResult();
  if(action==='sync-ready'){
    // No provider request: validate import-only credentials and readiness first.
    encryptCheckpoint(Buffer.from('{}'),process.env.DISCOVERY_CHECKPOINT_KEY);
    await preflightAdminSync(syncConfiguration());console.log(JSON.stringify({adminSyncReady:true,providerRequests:0}));return;
  }
  const directory = fileURLToPath(new URL('../.debug/', import.meta.url));
  const output = resolve(process.env.DISCOVERY_STATE_FILE ?? fileURLToPath(new URL('../.debug/socialapis-discovery.json', import.meta.url)));
  const local = relative(directory, output);
  if (!local || local.startsWith('..') || isAbsolute(local) || !output.endsWith('.json') ||
      output === resolve(directory, 'socialapis-response.json')) throw new Stop('PRIVATE_REPLAY_OUTPUT_REQUIRED');
  if(action==='seal'){
    if((await stat(output)).size>12*1024*1024)throw new Stop('CHECKPOINT_CAPACITY_REACHED');
    await writeFile(output+'.enc',encryptCheckpoint(await readFile(output),process.env.DISCOVERY_CHECKPOINT_KEY),{mode:0o600});return;
  }
  if(action==='unseal'){
    try{
      if((await stat(output+'.enc')).size>12*1024*1024+32)throw new Stop('CHECKPOINT_CAPACITY_REACHED');
      const plaintext=decryptCheckpoint(await readFile(output+'.enc'),process.env.DISCOVERY_CHECKPOINT_KEY);
      await writeFile(output,plaintext,{mode:0o600});
    }catch(error){if(error.code!=='ENOENT')throw error;else await stat(output);} // Explicit legacy plaintext compatibility, never initialize here.
    return;
  }
  let state;
  try {
    if ((await stat(output)).size > 12 * 1024 * 1024) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
    state = JSON.parse(await readFile(output, 'utf8'));
    if (!['SOCIALAPIS_DISCOVERY_STATE', 'LIVE_PROVIDER_RESPONSE', 'LOCAL_PROVIDER_CAPTURE_REPLAY'].includes(state.evidenceKind) || !Array.isArray(state.jobs)) throw new Stop('INVALID_REPLAY_STORE');
  } catch (error) {
    if (error.code !== 'ENOENT' || action !== 'init' || process.env.INITIALIZE_STATE !== 'true') throw error;
    state = { evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] };
  }
  if((state.freshSession||state.privateResponseAudit)&&process.env.DISCOVERY_AUTO_ADMIN_SYNC!=='true')
    throw new Stop('PRIVATE_FRESH_CHECKPOINT_REQUIRES_ENCRYPTED_MODE');
  const receipt = process.env.ARTIFACT_RECEIPT;
  const oneRequest=process.env.DISCOVERY_ONE_REQUEST_V1==='true';
  const resolvePolicy=async(policy,history)=>{
    if(!oneRequest)return resolveDiscoveryPolicy(policy,process.env.DISCOVERY_QUERY,history);
    if(process.env.DISCOVERY_AUTO_ADMIN_SYNC!=='true'||process.env.DISCOVERY_MAX_REQUESTS!=='1'||process.env.DISCOVERY_MAX_CREDITS!=='1'||
      process.env.RESUME_CONTINUATION==='true'||process.env.DISCOVERY_QUERY?.trim())throw new Stop('V1_ONE_REQUEST_CONFIGURATION_REQUIRED');
    const preferences=JSON.parse(await readFile(new URL('../config/backend-target-preferences.json',import.meta.url),'utf8'));
    return resolveOneRequestPolicy(policy,preferences,history);
  };
  if(action==='fresh-init'){
    syncConfiguration();encryptCheckpoint(Buffer.from('{}'),process.env.DISCOVERY_CHECKPOINT_KEY);
    if(process.env.DISCOVERY_AUTO_ADMIN_SYNC!=='true'||process.env.DISCOVERY_MAX_REQUESTS!=='1'||process.env.DISCOVERY_MAX_CREDITS!=='1'||
      process.env.RESUME_CONTINUATION==='true'||process.env.INITIALIZE_STATE==='true')throw new Stop('FRESH_CONFIGURATION_INVALID');
    const policy=JSON.parse(process.env.DISCOVERY_POLICY??'{}');
    const resolved=await resolvePolicy(policy,state.discoveryYieldHistory??[]);
    const start=dateSelection('DISCOVERY_START_TIME',resolved.startTime),end=dateSelection('DISCOVERY_END_TIME',resolved.endTime);
    state=createFreshSession(state,{...resolved,startTime:start.value,endTime:end.value,maxRequests:1,maxCredits:1},{
      sessionId:process.env.DISCOVERY_FRESH_SESSION_ID,workflowRunId:process.env.GITHUB_RUN_ID,activationSha:process.env.DISCOVERY_FRESH_ACTIVATION_SHA,
      proof:{...actionsResultEvidence(state),artifactId:receipt},
      freeConfirmed:process.env.SOCIALAPIS_FREE_PLAN_CONFIRMED==='true',noCashConfirmed:process.env.SOCIALAPIS_NO_CASH_CONFIRMED==='true'});
    state.orchestration.dateFilterAudit={startTime:state.orchestration.config.startTime,endTime:state.orchestration.config.endTime,
      startInputState:start.inputState,endInputState:end.inputState,startSource:start.source,endSource:end.source,providerFreshnessVerified:false};
    if(oneRequest)state.oneRequestExecution={workflowRunId:state.orchestration.workflowRunId,querySelection:resolved.querySelection};
    console.log(JSON.stringify({freshSessionId:state.freshSession.id,maximumHttpRequests:1,maximumPermittedDeductedCredits:1,historicalRequests:state.freshSession.historicalRequests,
      historicalCredits:state.freshSession.historicalCredits,providerRequests:0}));
  } else if (action === 'init') {
    if(process.env.DISCOVERY_AUTO_ADMIN_SYNC==='true'){
      syncConfiguration();encryptCheckpoint(Buffer.from('{}'),process.env.DISCOVERY_CHECKPOINT_KEY);
      if(!oneRequest && (process.env.DISCOVERY_MAX_REQUESTS!=='1'||process.env.DISCOVERY_MAX_CREDITS!=='1'||process.env.RESUME_CONTINUATION==='true'||
        process.env.DISCOVERY_QUERY!=='Fresher .NET Hà Nội'||process.env.DISCOVERY_START_TIME!=='2026-10-26'||process.env.DISCOVERY_END_TIME!=='2026-10-26')
        )throw new Stop('ONE_REQUEST_DEMO_CONFIGURATION_REQUIRED');
    }
    if (process.env.RESUME_CONTINUATION !== undefined && !['true','false',''].includes(process.env.RESUME_CONTINUATION)) throw new Stop('INVALID_RESUME_OPTION');
    // Downloading this exact result artifact is proof of persistence. A claim
    // artifact is NEVER acknowledged, even if the prior workflow reported PASS.
    if (state.orchestration?.phase === 'COMPLETED_UNCONFIRMED') acknowledgePersistence(state, receipt);
    if(process.env.RECONCILE_TERMINAL_FAILURE!==undefined && !['true','false',''].includes(process.env.RECONCILE_TERMINAL_FAILURE))throw new Stop('INVALID_RECONCILIATION_OPTION');
    if(process.env.RECONCILE_TERMINAL_FAILURE==='true')reconcileTerminalFailure(state,{...actionsResultEvidence(state),artifactId:receipt});
    const policy = JSON.parse(process.env.DISCOVERY_POLICY ?? '{}');
    // The manual runner deliberately cannot enable unattended production mode.
    if (policy.mode && policy.mode !== 'development') throw new Stop('PRODUCTION_EXECUTION_NOT_ENABLED');
    const resumeContinuation = process.env.RESUME_CONTINUATION === 'true';
    if (resumeContinuation && process.env.INITIALIZE_STATE === 'true') throw new Stop('CANNOT_INITIALIZE_WHILE_RESUMING');
    const resolved=oneRequest ? await resolvePolicy(policy,state.discoveryYieldHistory??[]) : resolveDiscoveryPolicy(policy,process.env.DISCOVERY_QUERY,state.discoveryYieldHistory??[],{resume:resumeContinuation,previous:state.orchestration});
    const start=dateSelection('DISCOVERY_START_TIME',resolved.startTime),end=dateSelection('DISCOVERY_END_TIME',resolved.endTime);
    beginExecution(state, { ...resolved,
      startTime:start.value,endTime:end.value,
      maxRequests: process.env.DISCOVERY_MAX_REQUESTS ?? '1', maxCredits: process.env.DISCOVERY_MAX_CREDITS ?? '1' }, process.env.GITHUB_RUN_ID ?? 'local', { resumeContinuation });
    state.orchestration.dateFilterAudit={startTime:state.orchestration.config.startTime,endTime:state.orchestration.config.endTime,
      startInputState:start.inputState,endInputState:end.inputState,startSource:start.source,endSource:end.source,providerFreshnessVerified:false};
    if(resolved.profile?.technologyProfile)state.queryPlan={version:'facebook-query-v1',candidates:planFacebookQueries(resolved.profile.technologyProfile,state.discoveryYieldHistory??[],resolved.targetJobPreferences)};
    if(oneRequest)state.oneRequestExecution={workflowRunId:state.orchestration.workflowRunId,querySelection:resolved.querySelection};
    console.log(JSON.stringify({ maximumHttpRequests: state.orchestration.config.maxRequests,
      dateFilters:state.orchestration.dateFilterAudit,
      maximumPermittedDeductedCredits: state.orchestration.config.maxCredits,
      maximumContractCreditExposure: Math.min(state.orchestration.config.maxRequests, state.orchestration.config.maxCredits),
      warning: 'Free credits only; provider overcharge cannot be undone; operator must verify no auto-top-up. No schedule.' }));
  } else if (action === 'claim') {
    const active = claimPage(state);
    if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT, `active=${active}\n`);
  } else if (action === 'page') {
    if (process.env.SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT !== 'true' && !(process.env.DISCOVERY_AUTO_ADMIN_SYNC==='true' &&
        process.env.SOCIALAPIS_PRIVATE_CHECKPOINT_CONFIRMED==='true')) throw new Stop('PUBLIC_ARTIFACT_CONSENT_REQUIRED');
    await performPage(state, { receipt, enabled: process.env.SOCIALAPIS_LIVE_ENABLED === 'true',
      claimArtifactName:process.env.ARTIFACT_NAME,captureReviews:process.env.DISCOVERY_AUTO_ADMIN_SYNC==='true',
      freePlanConfirmed: process.env.SOCIALAPIS_FREE_PLAN_CONFIRMED === 'true', noCashConfirmed: process.env.SOCIALAPIS_NO_CASH_CONFIRMED === 'true' });
  } else if (action === 'ack') {
    // Result has already been uploaded even when HTTP/parsing failed.
    acknowledgePersistence(state, receipt, actionsResultEvidence(state));
  } else if(action==='sync'){
    // Only saved observations are replayed. This branch never constructs a provider adapter.
    const summaries=await syncReviews(state,syncConfiguration());
    console.log(JSON.stringify({adminSync:summaries,providerRequests:0}));
  }
  state.workflowMetrics = workflowMetrics(state);
  await mkdir(directory, { recursive: true });
  const json = JSON.stringify(state, null, 2) + '\n';
  if (Buffer.byteLength(json) > (process.env.DISCOVERY_AUTO_ADMIN_SYNC==='true' ? 12*1024*1024 : 1_048_576)) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
  const temporary = `${output}.${randomUUID()}.tmp`;
  try { await writeFile(temporary, json, { mode: 0o600, flag: 'wx' }); await rename(temporary, output); }
  finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
  console.log(JSON.stringify({ action, metrics: state.workflowMetrics }));
}
