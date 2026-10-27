import { createHash } from 'node:crypto';
import { parse } from 'yaml';
import { Stop } from './core.mjs';

export const WORKFLOW_PATH = '.github/workflows/socialapis-job-discovery.yml';
const PROVIDER_ACTION = './.github/actions/socialapis-discovery-page';
const BARRIER = 'Offline unit and configuration tests (no provider secret)';
export const HISTORY_LIMITS = Object.freeze({ pages: 5, runs: 100, perPage: 20, metadataPages: 3, metadataPerPage: 100 });
// Reviewed workflow source only. Unknown revisions must not become evidence of
// zero HTTP merely because their steps use familiar display names. These
// sources are reviewed to have NO SocialAPIs execution path outside their bounded
// PROVIDER_ACTION steps (init/sync/readiness/claim-tag scripts do not call it).
export const REVIEWED_WORKFLOW_HASHES = new Set([
  'b444787eaa084d3a23672b305f0228b7ffd7c60cc58bc3ab4055fc11467477cf', // 7e057ca (legacy)
  '73f5fcbdbe72f31ecdf63cd1551de56e15f78c00888b9c77132a5a3706409495', // e82298e
  'f89633c41814cbe88d2293dbceec94272b4741daea5ca59ca8664b6a26abce1f', // 3c01c18 / failed offline run
  '3f46bbe3aa6314626041dbe808f8004e055e8877febe6c5681f8b40216e115e8', // 3dfec1c
  '065d445876a9be0b368c42ac8e4167b9ba0e5515c561cdda8ba08f92362094d2', // bounded traversal
  '34ad103f94b45dc8059b0669393864373b47a8dd347f0e3c3a99d96ce1440fd7', // fresh one-shot, same provider barriers
  '61def54a11f9b622015b1ffe1ccf276b81e14969918ed19fb791f6527010fad1', // fresh global accounting + operator balance evidence
  'ff568988e93dc3b835c402cb290917cabc5241b95a06c9c41b01b6ff267c1ade', // V1: one provider slot, encrypted automatic sync, global budget in both init modes
  '2423e8ae2c413fcd68bc8709f281768d6e40b6b8d4f47f8c3af51cddc0d2fb78', // Manual V1: same one-slot/claim/sync boundaries, no balance JSON or rolling quota
  '87fb4aa1a0e1e29539f0eae121b298784432fc83023889790d410253ee2ea771', // Run-scoped Admin sync/recovery only; unchanged single provider boundary
]);
export const workflowHash = text => createHash('sha256').update(text.replace(/\r\n/gu, '\n')).digest('hex');
const positive = value => /^[1-9]\d*$/u.test(String(value ?? ''));
const fail = code => { throw new Stop(code); };

async function metadata(call, field) {
  const rows=[];let total;
  for(let page=1;page<=HISTORY_LIMITS.metadataPages;page++){
    const {data}=await call({page,per_page:HISTORY_LIMITS.metadataPerPage});
    if(!Array.isArray(data?.[field]) || data[field].length>HISTORY_LIMITS.metadataPerPage || !Number.isSafeInteger(data.total_count) || data.total_count<0 ||
      total!==undefined && total!==data.total_count)fail('CHECKPOINT_METADATA_INCOMPLETE');
    total=data.total_count;rows.push(...data[field]);
    if(rows.length>total || rows.some(r=>!positive(r.id)) || new Set(rows.map(r=>r.id)).size!==rows.length)fail('CHECKPOINT_METADATA_INCOMPLETE');
    if(rows.length===total)return rows;
    if(data[field].length!==HISTORY_LIMITS.metadataPerPage)fail('CHECKPOINT_METADATA_INCOMPLETE');
  }
  fail('CHECKPOINT_METADATA_LIMIT_REACHED');
}

function stepName(step){return step.name ?? `Run ${step.uses ?? step.run.split('\n')[0]}`;}
async function executionEvidence(github, repo, run) {
  const response=await github.rest.repos.getContent({...repo,path:WORKFLOW_PATH,ref:run.head_sha});
  const file=response.data;
  if(file?.encoding!=='base64' || typeof file.content!=='string' || file.content.length>90_000 ||
    !Number.isSafeInteger(file.size) || file.size<0 || file.size>64*1024)fail('CHECKPOINT_WORKFLOW_SOURCE_UNVERIFIED');
  const source=Buffer.from(file.content,'base64').toString('utf8');
  if(!REVIEWED_WORKFLOW_HASHES.has(workflowHash(source)))fail('CHECKPOINT_WORKFLOW_SOURCE_UNVERIFIED');
  const workflow=parse(source),definitions=workflow.jobs.discover.steps;
  const jobs=await metadata(params=>github.rest.actions.listJobsForWorkflowRun({...repo,run_id:run.id,filter:'all',...params}),'jobs');
  if(jobs.length!==1 || jobs[0].name!=='discover' || jobs[0].run_id!==run.id || jobs[0].status!=='completed' ||
    jobs[0].run_attempt!==1 || jobs[0].conclusion!==run.conclusion || !Array.isArray(jobs[0].steps))fail('CHECKPOINT_JOB_EVIDENCE_UNVERIFIED');
  const steps=jobs[0].steps.filter(s=>s.name!=='Set up job' && s.name!=='Complete job' && !s.name.startsWith('Post '));
  if(steps.length!==definitions.length || steps.some((s,i)=>s.name!==stepName(definitions[i]) || s.status!=='completed' ||
    !Number.isSafeInteger(s.number) || i>0 && s.number<=steps[i-1].number))fail('CHECKPOINT_JOB_EVIDENCE_UNVERIFIED');
  const barrier=definitions.findIndex(s=>s.name===BARRIER);
  const failed=steps.findIndex(s=>s.conclusion==='failure');
  const providers=steps.filter((_s,i)=>definitions[i].uses===PROVIDER_ACTION);
  if(barrier<0 || ![1,6].includes(providers.length))fail('CHECKPOINT_WORKFLOW_SOURCE_UNVERIFIED');
  return {
    providerSlots:providers.length,
    // Only a hard failure at/before the approved offline barrier, with ALL later
    // steps explicitly skipped, proves this workflow could not reach HTTP.
    preHttp:run.conclusion==='failure' && failed>=0 && failed<=barrier &&
      steps.slice(0,failed).every(s=>s.conclusion==='success') && steps.slice(failed+1).every(s=>s.conclusion==='skipped'),
    // A later non-provider failure is also conclusive when complete metadata
    // proves EVERY reviewed provider action was skipped. Absence of artifacts
    // alone is never proof, nor is a cancelled/unknown/possibly started action.
    zeroProvider:run.conclusion==='failure' && failed>barrier && steps[barrier].conclusion==='success' &&
      steps.every(s=>['success','failure','skipped'].includes(s.conclusion)) && providers.every(s=>s.conclusion==='skipped'),
    startedSlots:providers.flatMap((s,i)=>s.conclusion==='skipped'?[]:[i+1])
  };
}

function selectArtifact(artifacts,run,evidence){
  const related=artifacts.filter(a=>a.name?.startsWith('socialapis-page-') || ['socialapis-discovery-state','socialapis-discovery-checkpoint'].includes(a.name));
  if(related.some(a=>!positive(a.id)||a.expired!==false || a.workflow_run?.id!==undefined && a.workflow_run.id!==run.id) ||
    new Set(related.map(a=>a.name)).size!==related.length)fail('CHECKPOINT_MISSING_OR_EXPIRED_RESTORE_BACKUP_BEFORE_ANY_PROVIDER_REQUEST');
  if(related.length && evidence.preHttp)fail('CHECKPOINT_CONTRADICTORY_EXECUTION_EVIDENCE');
  const pages=related.filter(a=>a.name.startsWith('socialapis-page-')).map(a=>{
    const m=a.name.match(/^socialapis-page-([1-9]\d*)-([1-6])-(claim|result)$/u);
    if(!m || m[1]!==String(run.id) || Number(m[2])>evidence.providerSlots)fail('CHECKPOINT_ARTIFACT_PROVENANCE_INVALID');
    return {...a,slot:Number(m[2]),kind:m[3]};
  });
  if(evidence.zeroProvider && pages.length)fail('CHECKPOINT_CONTRADICTORY_EXECUTION_EVIDENCE');
  pages.sort((a,b)=>b.slot-a.slot || Number(b.kind==='result')-Number(a.kind==='result'));
  const selected=pages[0] ?? related.find(a=>a.name==='socialapis-discovery-state') ?? related.find(a=>a.name==='socialapis-discovery-checkpoint');
  if(!selected)return null;
  // A missing later claim/result must not make an earlier checkpoint look safe.
  if(evidence.startedSlots.some(slot=>slot>(selected.slot??0)))fail('CHECKPOINT_LATER_ATTEMPT_UNACCOUNTED');
  if(selected.slot){
    for(let slot=1;slot<=selected.slot;slot++){
      if(!pages.some(a=>a.slot===slot&&a.kind==='claim') || slot<selected.slot&&!pages.some(a=>a.slot===slot&&a.kind==='result'))
        fail('CHECKPOINT_ARTIFACT_CHAIN_INCOMPLETE');
    }
  }
  const claim=selected.kind==='result'?pages.find(a=>a.slot===selected.slot&&a.kind==='claim'):null;
  return {run_id:String(run.id),artifact_name:selected.name,artifact_id:String(selected.id),
    claim_artifact_id:claim?String(claim.id):'',initialize:'false'};
}

// GET-only metadata port; no provider, artifact download, state mutation or cache.
export async function locateCheckpoint({github,context,initialize=false,attempt='1',currentRunNumber}) {
  if(String(attempt)!=='1')fail('RERUN_DISABLED_USE_A_NEW_MANUAL_RUN');
  if(!positive(context.runId)||!positive(currentRunNumber))fail('CHECKPOINT_CURRENT_RUN_UNVERIFIED');
  try{
    const {data:repository}=await github.rest.repos.get(context.repo);
    if(repository.private!==false)fail('PUBLIC_REPOSITORY_REQUIRED_FOR_THIS_ZERO_COST_PLAN');
    let total,seen=0,lastNumber=Number(currentRunNumber)+1;const ids=new Set(),skipped=[];
    for(let page=1;page<=HISTORY_LIMITS.pages;page++){
      const {data}=await github.rest.actions.listWorkflowRuns({...context.repo,workflow_id:'socialapis-job-discovery.yml',
        branch:'main',event:'workflow_dispatch',per_page:HISTORY_LIMITS.perPage,page});
      if(!Array.isArray(data?.workflow_runs)||data.workflow_runs.length>HISTORY_LIMITS.perPage||!Number.isSafeInteger(data.total_count)||data.total_count<1 ||
        total!==undefined&&total!==data.total_count)fail('CHECKPOINT_HISTORY_CHANGED_OR_INCOMPLETE');
      total=data.total_count;
      for(const run of data.workflow_runs){
        if(++seen>HISTORY_LIMITS.runs)fail('CHECKPOINT_HISTORY_LIMIT_REACHED');
        if(!positive(run.id)||ids.has(run.id)||!Number.isSafeInteger(run.run_number)||run.run_number>=lastNumber)
          fail('CHECKPOINT_HISTORY_CHANGED_OR_INCOMPLETE');
        ids.add(run.id);lastNumber=run.run_number;
        if(run.id===context.runId){if(run.run_number!==Number(currentRunNumber))fail('CHECKPOINT_CURRENT_RUN_UNVERIFIED');continue;}
        if(initialize)fail('CANNOT_RESET_EXISTING_DISCOVERY_HISTORY');
        if(run.status!=='completed')fail('PREVIOUS_RUN_NOT_COMPLETED');
        if(run.run_attempt!==1 || run.path!==WORKFLOW_PATH || run.event!=='workflow_dispatch' || run.head_branch!=='main' ||
          run.head_repository?.full_name!==`${context.repo.owner}/${context.repo.repo}` || !/^[a-f0-9]{40}$/u.test(run.head_sha??''))
          fail('CHECKPOINT_RUN_PROVENANCE_UNVERIFIED');
        const artifacts=await metadata(params=>github.rest.actions.listWorkflowRunArtifacts({...context.repo,run_id:run.id,...params}),'artifacts');
        const evidence=await executionEvidence(github,context.repo,run);
        const selected=selectArtifact(artifacts,run,evidence);
        if(selected)return {...selected,skipped_runs:JSON.stringify(skipped)};
        if(!evidence.preHttp && !evidence.zeroProvider)fail('CHECKPOINT_NO_ARTIFACT_HTTP_UNPROVEN');
        skipped.push({runId:String(run.id),reason:evidence.preHttp?'VERIFIED_PRE_HTTP_FAILURE':'VERIFIED_ZERO_PROVIDER_FAILURE'});
      }
      if(seen>total)fail('CHECKPOINT_HISTORY_CHANGED_OR_INCOMPLETE');
      if(seen===total){
        if(seen===1&&ids.has(context.runId)&&initialize)return {initialize:'true'};
        fail('CHECKPOINT_MISSING_RESTORE_VERIFIED_BACKUP_BEFORE_ANY_PROVIDER_REQUEST');
      }
      if(data.workflow_runs.length!==HISTORY_LIMITS.perPage)fail('CHECKPOINT_HISTORY_CHANGED_OR_INCOMPLETE');
    }
    fail('CHECKPOINT_HISTORY_LIMIT_REACHED');
  }catch(error){if(error instanceof Stop)throw error;throw new Stop('CHECKPOINT_HISTORY_UNAVAILABLE');}
}
