// Synthetic GitHub metadata, never a claim that a live run was inspected.
import {readFileSync} from 'node:fs';
import {parse} from 'yaml';
export const source=readFileSync(new URL('../../../../.github/workflows/socialapis-job-discovery.yml',import.meta.url),'utf8');
const definitions=parse(source).jobs.discover.steps;
export function run(id,options={}){
  return {id,run_number:id,status:'completed',run_attempt:1,path:'.github/workflows/socialapis-job-discovery.yml',
    event:'workflow_dispatch',head_branch:'main',head_sha:'a'.repeat(40),head_repository:{full_name:'fixture/fixture'},
    conclusion:'failure',...options};
}
export function artifact(id,name,options={}){return {id,name,expired:false,...options};}
export function jobsFor(record,mode='PRE_REQUEST_CHECKPOINT',startedSlots=[1]){
  const barrier=definitions.findIndex(s=>s.run==='npm test');
  const checkpoint=definitions.findIndex(s=>s.name==='Persist initialized state before any HTTP');
  let slot=0;
  const steps=definitions.map((d,index)=>{
    let conclusion=index<=checkpoint?'success':'skipped';
    if(mode==='PRE_HTTP_FAILURE')conclusion=index<barrier?'success':index===barrier?'failure':'skipped';
    if(mode==='UNPROVEN')conclusion='skipped';
    if(d.uses==='./.github/actions/socialapis-discovery-page' && mode==='PROVIDER')conclusion=startedSlots.includes(++slot)?'failure':'skipped';
    return {name:d.name??`Run ${d.uses??d.run.split('\n')[0]}`,number:index+2,status:'completed',conclusion};
  });
  return [{id:record.id*10,run_id:record.id,run_attempt:record.run_attempt,name:'discover',status:'completed',conclusion:record.conclusion,
    steps:[{name:'Set up job',number:1,status:'completed',conclusion:'success'},...steps,
      {name:'Complete job',number:steps.length+2,status:'completed',conclusion:'success'}]}];
}
export function historyFixture({runs=[run(200),run(100)],artifacts={100:[artifact(11,'socialapis-discovery-state')]},
  modes={},startedSlots={},sources={},jobRows={},artifactRows=artifacts,runTotal=runs.length,privateRepo=false,failMethod}={}){
  const calls=[];
  const paginate=(rows,params)=>rows.slice((params.page-1)*params.per_page,params.page*params.per_page);
  const call=(name,params)=>{calls.push({name,...params});if(failMethod===name)throw Error('PRIVATE_RESPONSE_MUST_NOT_LEAK');};
  const github={rest:{repos:{
    get:async params=>{call('repository',params);return {data:{private:privateRepo}};},
    getContent:async params=>{call('source',params);return {data:{encoding:'base64',content:Buffer.from(sources[params.ref]??source).toString('base64'),size:source.length}};}
  },actions:{
    listWorkflowRuns:async params=>{call('runs',params);return {data:{total_count:runTotal,workflow_runs:paginate(runs,params)}};},
    listWorkflowRunArtifacts:async params=>{call('artifacts',params);const rows=artifactRows[params.run_id]??[];return {data:{total_count:rows.length,artifacts:paginate(rows,params)}};},
    listJobsForWorkflowRun:async params=>{call('jobs',params);const record=runs.find(r=>r.id===params.run_id);
      const rows=jobRows[params.run_id]??jobsFor(record,modes[record.id],startedSlots[record.id]);
      return {data:{total_count:rows.length,jobs:paginate(rows,params)}};}
  }}};
  return {github,calls,context:{repo:{owner:'fixture',repo:'fixture'},runId:200},currentRunNumber:200};
}
