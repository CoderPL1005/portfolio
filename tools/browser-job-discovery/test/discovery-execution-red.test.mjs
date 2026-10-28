import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {parse} from 'yaml';
import {SocialApisCaptureDiscoveryAdapter} from '../src/socialapis-adapter.mjs';
import {JobPostExtractor} from '../src/core.mjs';

// Missing future module is an assertion failure, not ERR_MODULE_NOT_FOUND.
const workerUrl=new URL('../src/discovery-execution-worker.mjs',import.meta.url);
const worker=existsSync(workerUrl)?await import(workerUrl.href):{};
const executionId='11111111-1111-4111-8111-111111111111';
const text='Synthetic Software hiring Fresher .NET Backend Developer in Hanoi. C#, ASP.NET Core. Apply jobs@example.invalid.';
const row=(id='9001',body=text)=>({basic_info:{post_id:id,post_text:body,url:`https://www.facebook.com/synthetic/posts/${id}`}});
const payload=(rows=[row()])=>({data:{items:rows,page_info:{cursor:'SYNTHETIC_CURSOR_NEVER_REQUESTED'}},meta:{creditsCharged:1}});
const sha=value=>createHash('sha256').update(value).digest('hex');
function harness(t,{rows,providerStatus=200,credits='1',timeout=false,semanticFailure=false,claimDenied=false,runAttempt=1,triggerSource='ADMIN_MANUAL'}={}){
  const events=[],saved=[],completions=[],network=[],logs=[];let claimed=false;
  t.mock.method(console,'log',(...values)=>logs.push(values.join(' ')));
  const originalFetch=globalThis.fetch;globalThis.fetch=()=>{throw Error('TEST_REAL_NETWORK_FORBIDDEN');};t.after(()=>{globalThis.fetch=originalFetch;});
  const previous=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='SYNTHETIC_SOCIALAPIS_KEY_NOT_REAL';
  t.after(()=>{if(previous===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=previous;});
  const backend={
    getConfiguration:async id=>{assert.equal(id,executionId);events.push('config');return {executionId,triggerSource,maxRequests:1,maxCredits:1,query:'Backend Fresher Hanoi',
      freePlanConfirmed:true,noCashConfirmed:true,timeoutMs:1000,semanticEnabled:true,profile:{technologies:['C#','ASP.NET Core']}};},
    claim:async()=>{events.push('claim');if(claimDenied||claimed||runAttempt!==1)return {claimed:false};claimed=true;return {claimed:true};},
    saveObservations:async(_id,observations)=>{assert.equal(claimed,true);events.push('save-raw');saved.push(...structuredClone(observations));},
    extract:async()=>{events.push('semantic');if(semanticFailure)throw Error(`SYNTHETIC_GEMINI_${semanticFailure}`);return null;},
    complete:async(_id,result)=>{events.push('complete');completions.push(structuredClone(result));return result;}
  };
  const providerTransport=async(url,options)=>{
    events.push('http');assert.equal(claimed,true,'durable backend claim must precede HTTP');
    assert.equal(new URL(url).host,'api.socialapis.io');assert.equal(options.method,'GET');assert.equal(options.redirect,'manual');
    assert.equal(new URL(url).searchParams.get('end_cursor'),null);network.push(String(url));
    assert.equal(network.length,1,'one HTTP attempt; no retry/pagination');
    if(timeout)throw new DOMException('SYNTHETIC_TIMEOUT','AbortError');
    const headers={'content-type':'application/json','X-SocialAPIs-Credits-Used':'6'};
    if(credits!==null)headers['X-SocialAPIs-Credits-Deducted']=credits;
    return new Response(JSON.stringify(payload(rows)),{status:providerStatus,headers});
  };
  const run=async()=>{
    assert.equal(typeof worker.runDiscoveryExecution,'function','missing independent execution worker behavior');
    return worker.runDiscoveryExecution({executionId,workflowRunId:'10001',runAttempt},{backend,providerTransport});
  };
  return {run,events,saved,completions,network,logs};
}

test('T05/T06 HTTP follows claim, one page even with cursor',async t=>{
  const h=harness(t);await h.run();assert.equal(h.network.length,1);
  assert.ok(h.events.indexOf('claim')<h.events.indexOf('http'));
  assert.ok(h.events.indexOf('save-raw')>h.events.indexOf('http'));assert.equal(h.completions.length,1);
});
test('T05 duplicate execution cannot acquire a second provider attempt',async t=>{
  const h=harness(t);await h.run();await h.run();assert.equal(h.network.length,1);
});
test('T05 concurrency has one atomic-claim winner (fake backend, not DB proof)',async t=>{
  const h=harness(t);await Promise.all([h.run(),h.run()]);assert.equal(h.network.length,1);
});
for(const options of [{claimDenied:true},{runAttempt:2}])test(`T05 rejected claim/rerun never reaches transport ${JSON.stringify(options)}`,async t=>{
  const h=harness(t,options);await h.run();assert.equal(h.network.length,0);assert.equal(h.saved.length,0);
});
test('T06 provider timeout is UNKNOWN, unknown credits and never retried',async t=>{
  const h=harness(t,{timeout:true});await h.run();assert.equal(h.network.length,1);
  assert.equal(h.completions.at(-1).state,'PROVIDER_UNKNOWN');assert.equal(h.completions.at(-1).creditsDeducted,null);
});
test('T06 deducted and alternate usage headers are not summed',async t=>{
  const h=harness(t);await h.run();const r=h.completions.at(-1);
  assert.equal(r.actualRequests,1);assert.equal(r.creditsDeducted,1);assert.equal(r.creditsUsedAlternateHeader,6);
});
test('T06 unknown cost is not zero/free proof',async t=>{
  const h=harness(t,{credits:null});await h.run();assert.equal(h.network.length,1);
  assert.equal(h.completions.at(-1).creditsDeducted,null);assert.notEqual(h.completions.at(-1).state,'SUCCEEDED');
});
test('T06 exhausted credits does not retry or fall back to paid provider',async t=>{
  const h=harness(t,{providerStatus:402});await h.run();assert.equal(h.network.length,1);
  assert.notEqual(h.completions.at(-1).state,'SUCCEEDED');
});
test('T07 every malformed, mismatched and nonrecruitment item is retained',async t=>{
  const items=[row(),null,{basic_info:{post_id:'9002',post_text:'Not a recruitment post',url:'https://www.facebook.com/synthetic/posts/pfbidSynthetic'}},row('9003','Today the weather is fine.')];
  const h=harness(t,{rows:items});await h.run();assert.equal(h.saved.length,items.length);
  assert.deepEqual(h.saved.map(r=>r.itemIndex),[0,1,2,3]);assert.ok(h.saved.every(r=>typeof r.outcome==='string'));
});
test('T08/T13 raw source identity, exact hash and unknown publication date are retained',async t=>{
  const h=harness(t);await h.run();const observation=h.saved[0];
  assert.equal(observation.providerPostId,'9001');assert.equal(observation.rawContent,text);
  assert.equal(observation.contentHash,sha(text));assert.equal(observation.publishedAt,null);
  assert.equal(observation.sourceUrl,'https://www.facebook.com/synthetic/posts/9001');
});
test('T10 multi-vacancy source does not become fabricated independent provider IDs',async t=>{
  const h=harness(t,{rows:[row('9001','Synthetic Software hiring in Hanoi\n1. Fresher .NET Backend Developer\n2. Intern Python Backend Developer')]});
  await h.run();assert.equal(h.saved.length,1);assert.equal(h.saved[0].providerPostId,'9001');
  assert.equal(h.completions.at(-1).results[0].vacancies.length,2);
});
for(const failure of ['429','TIMEOUT','INVALID_JSON'])test(`T11/T12 semantic ${failure} retains raw and explicit reviewable missing facts`,async t=>{
  const h=harness(t,{semanticFailure:failure,rows:[row('9001','Hiring developers. Contact the team for details.')]});
  await h.run();assert.equal(h.saved.length,1);assert.ok(h.events.includes('semantic'));
  assert.ok(h.events.indexOf('save-raw')<h.events.indexOf('semantic'));
  const result=h.completions.at(-1);assert.equal(result.results[0].status,'NEEDS_REVIEW');
  assert.ok(result.results[0].missingFields.length>0);
});
test('T13 every editable JobPosting field has a status, not fabricated defaults',async t=>{
  const fields=['companyName','positionTitle','location','employmentType','workplaceType','salaryMinimum','salaryMaximum','salaryCurrency','salaryPeriod',
    'experienceRequirements','description','technologyStack','applicationEmail','applicationUrl','expiresAt','notes'];
  const h=harness(t);await h.run();const fieldsReport=h.completions.at(-1).results[0].vacancies[0].fields;
  for(const key of fields){assert.ok(Object.hasOwn(fieldsReport,key),key);assert.ok(['EXTRACTED','DERIVED','NOT_PROVIDED','NEEDS_REVIEW'].includes(fieldsReport[key].status));}
  assert.equal(fieldsReport.salaryMinimum.value,null);assert.equal(fieldsReport.expiresAt.value,null);
});
test('T18 worker preserves server-authorized future trigger through the same execution port',async t=>{
  const h=harness(t,{triggerSource:'DAILY_SCHEDULED'});await h.run();assert.equal(h.network.length,1);
  // Backend shared-service existence has its own test; this is worker provenance only.
  assert.equal(h.completions.at(-1).triggerSource,'DAILY_SCHEDULED');
});
test('T17 worker workflow has only execution_id input, manual trigger and no private artifacts',()=>{
  const url=new URL('../../../.github/workflows/job-discovery-ingestion.yml',import.meta.url);
  assert.ok(existsSync(url),'independent execution workflow is not implemented');
  const source=readFileSync(url,'utf8'),workflow=parse(source);
  assert.deepEqual(Object.keys(workflow.on),['workflow_dispatch']);
  assert.deepEqual(Object.keys(workflow.on.workflow_dispatch.inputs),['execution_id']);
  assert.equal(workflow.permissions.contents,'read');
  for(const job of Object.values(workflow.jobs)){
    assert.notEqual(job.permissions?.contents,'write');
    for(const step of job.steps){
      assert.ok(!step.uses?.startsWith('actions/upload-artifact'),'no public JD artifacts');
      assert.doesNotMatch(step.run??'',/--orchestrate|fresh-init/u);
      assert.notEqual(step['continue-on-error'],true);
    }
  }
  assert.match(source,/SOCIALAPIS_API_KEY/u);assert.doesNotMatch(source,/GEMINI_API_KEY/u);
});
test('T17 worker never logs raw recruitment text or provider credentials',async t=>{
  const h=harness(t);await h.run();
  assert.ok(h.logs.every(s=>!s.includes(text)&&!s.includes('SYNTHETIC_SOCIALAPIS_KEY_NOT_REAL')));
});
test('T16 control: existing parser retains a synthetic post without network',()=>{
  const adapter=new SocialApisCaptureDiscoveryAdapter(payload(),{capturedQuery:'Backend Fresher Hanoi'});
  assert.equal(adapter.itemStats.acceptedItems,1);assert.equal(adapter.records.size,1);
});
test('T16 control: current extractor can read a synthetic vacancy with publication UNKNOWN',()=>{
  const job=new JobPostExtractor().extract({id:'9001',provider:'SOCIALAPIS',source:'FACEBOOK',sourceUrl:'https://www.facebook.com/synthetic/posts/9001',body:text,title:'',employer:'',location:'',requirements:'',publishedAt:null});
  assert.ok(job.vacancies.length>=1);assert.equal(job.publishedAt,null);
});
