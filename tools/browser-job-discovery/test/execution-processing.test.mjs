import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import * as worker from '../src/discovery-execution-worker.mjs';
import * as review from '../src/discovery-review.mjs';
import {JobPostExtractor} from '../src/core.mjs';
import {rawContent,mockOutput,address,unknown} from './fixtures/ai-extraction-v1.mjs';
const id='11111111-1111-4111-8111-111111111111';
const originalFetch=globalThis.fetch;
test.before(()=>{globalThis.fetch=()=>{throw Error('LIVE_NETWORK_FORBIDDEN');};});
test.after(()=>{globalThis.fetch=originalFetch;});
const source=text=>({itemIndex:0,providerPostId:'9001',sourceUrl:'https://www.facebook.com/fixture/posts/9001',providerSourceUrl:'https://www.facebook.com/fixture/posts/9001',rawContent:text,contentHash:createHash('sha256').update(text).digest('hex'),publishedAt:null,outcome:'READY_FOR_EXTRACTION'});
test('classification exception is isolated without inventing a decision',async t=>{
  const extract=JobPostExtractor.prototype.extract;
  JobPostExtractor.prototype.extract=function(post){if(post.body==='SYNTHETIC_BROKEN')throw Error('private source must not escape');return extract.call(this,post);};
  t.after(()=>{JobPostExtractor.prototype.extract=extract;});
  const observations=[source('SYNTHETIC_BROKEN'),{...source('Hiring Backend Developer'),itemIndex:1}],saved=[];
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{},results:observations.map(observation=>({observation}))}),
    saveProcessing:async(_id,p)=>{saved.push(p);return {version:8};}};
  const result=await worker.replayDiscoveryExecution({executionId:id},{backend});
  assert.equal(saved.length,2);assert.equal(saved[0].review,null);assert.equal(saved[0].extractionError,'CLASSIFICATION_FAILED');
  assert.equal(saved[1].review.contentValidity.status,'VALID_RECRUITMENT');assert.equal(result.providerRequests,0);
});
test('deterministic extraction exception preserves classification and continues the next observation',async t=>{
  const extract=JobPostExtractor.prototype.extract;let hits=0;
  JobPostExtractor.prototype.extract=function(post){if(post.body==='Hiring Backend Developer'&&++hits===2)throw Error('SYNTHETIC_EXTRACTION_FAILURE');return extract.call(this,post);};
  t.after(()=>{JobPostExtractor.prototype.extract=extract;});
  const observations=[source('Hiring Backend Developer'),{...source('Hiring Python Developer'),itemIndex:1}],saved=[];
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{},results:observations.map(observation=>({observation}))}),
    saveProcessing:async(_id,p)=>{saved.push(p);return {version:8};}};
  await worker.replayDiscoveryExecution({executionId:id},{backend});
  assert.equal(saved.length,2);assert.equal(saved[0].review.contentValidity.status,'VALID_RECRUITMENT');
  assert.equal(saved[0].extractionError,'DETERMINISTIC_EXTRACTION_FAILED');assert.deepEqual(saved[0].vacancies,[]);
  assert.equal(saved[1].extractionError,null);assert.ok(saved[1].vacancies.length>0);
});
test('explicit replay consent uses current gates, not frozen semanticEnabled=false',async()=>{
  let calls=0;
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{semanticEnabled:false},
    semanticReplay:{enabled:true,modelIdentity:'offline-mock-v1'},results:[{observation:source('Hiring Backend Developer')}]}),
    extractPersisted:async()=>{calls++;return {version:8,generation:null};},saveProcessing:async()=>({version:9})};
  await worker.replayDiscoveryExecution({executionId:id},{backend});assert.equal(calls,0);
  await worker.replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},{backend});assert.equal(calls,1);
});
for(const [text,enabled,model,expected] of [
  ['Hiring Backend Developer',false,'offline-mock-v1',0],
  ['Hiring Backend Developer',true,null,0],
  ['I am looking for a job.',true,'offline-mock-v1',0],
  ['Company: Synthetic Software\nHiring Fresher Backend Developer\nLocation: Hanoi',true,'offline-mock-v1',1]
])test(`replay gates and meaningful field gaps: ${text}/${enabled}/${model}`,async()=>{
  let calls=0,staged;
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{semanticEnabled:true},semanticReplay:{enabled,modelIdentity:model},results:[{observation:source(text)}]}),
    extractPersisted:async()=>{
      calls++;
      if(text.startsWith('Company:'))for(const field of ['companyName','positionTitle','location'])assert.ok(staged.vacancies[0].fields[field].value);
      return {version:8,generation:null};
    },saveProcessing:async(_id,p)=>{staged=p;return {version:9};}};
  await worker.replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},{backend});assert.equal(calls,expected);
});
for(const [text,status] of [['Hiring Backend Developer. Contact our team.','VALID_RECRUITMENT'],['I am looking for a job.','INVALID_RECRUITMENT'],['Backend discussion','NEEDS_REVIEW']])test(`persisted classification: ${status}`,()=>{
  assert.equal(typeof review.buildReviewItemsFromObservations,'function');
  const [item]=review.buildReviewItemsFromObservations([source(text)]);
  assert.equal(item.contentValidity.status,status);assert.equal(item.rawContent,text);
});
for(const fails of [false,true])test(`Gemini mocked ${fails?'failure':'success'} preserves classification and source`,async()=>{
  // Omit location so supplementation is necessary; a complete deterministic
  // fixture correctly avoids an unnecessary generation call.
  const body=rawContent.replace(`Địa chỉ: ${address}\n`,'');
  const output=mockOutput();output.sharedLocation=unknown();
  const rebase=node=>{if(!node||typeof node!=='object')return;for(const span of node.evidence??[]){span.start=body.indexOf(span.quote);assert.ok(span.start>=0);span.end=span.start+span.quote.length;}for(const value of Object.values(node))rebase(value);};rebase(output);
  const observations=[source(body)];let calls=0,saved;
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{semanticEnabled:true,semanticModel:'offline-mock-v1'},semanticReplay:{enabled:true,modelIdentity:'offline-mock-v1'},results:observations.map(observation=>({observation}))}),
    extractPersisted:async()=>{calls++;return {version:8,generation:fails?null:{modelIdentity:'offline-mock-v1',output:JSON.stringify(output)}};},
    saveProcessing:async(_id,body)=>{saved=body;return {version:9};}};
  await worker.replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},{backend});
  assert.equal(calls,1);assert.equal(saved.review.contentValidity.status,'VALID_RECRUITMENT');
  assert.equal(saved.review.rawContent,body);assert.equal(saved.review.publishedAt,null);
  assert.equal(saved.extractionError,fails?'SEMANTIC_UNAVAILABLE':null);
  if(!fails){assert.ok(saved.vacancies.some(v=>v.positionTitle==='Backend'));assert.equal(saved.vacancies.length,3);}
});
test('final callback failure cannot erase incrementally saved classification',async t=>{
  const previous=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='SYNTHETIC_OFFLINE_PROVIDER_KEY';
  t.after(()=>{if(previous===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=previous;});
  const events=[];
  const backend={getConfiguration:async()=>({maxRequests:1,maxCredits:1,freePlanConfirmed:true,noCashConfirmed:true,query:'Backend Fresher Hanoi'}),
    claim:async()=>({claimed:true}),saveObservations:async()=>events.push('raw'),saveProcessing:async(_id,p)=>events.push(p.review.contentValidity.status),
    complete:async()=>{throw Error('SYNTHETIC_FINAL_CALLBACK_FAILURE');}};
  await assert.rejects(worker.runDiscoveryExecution({executionId:id,workflowRunId:'10001'},{backend,providerTransport:async()=>new Response(JSON.stringify({data:{items:[{basic_info:{post_id:'9001',post_text:'Hiring Backend Developer. Contact our team.',url:'https://www.facebook.com/fixture/posts/9001'}}]},meta:{creditsCharged:1}}),{headers:{'X-SocialAPIs-Credits-Deducted':'1'}})}),/SYNTHETIC_FINAL_CALLBACK_FAILURE/);
  assert.deepEqual(events,['raw','VALID_RECRUITMENT']);
});
test('persisted replay has no provider/claim/finalize port and saves before returning',async()=>{
  assert.equal(typeof worker.replayDiscoveryExecution,'function');
  const observations=[source('Hiring Backend Developer. Contact our team.')];let saves=0;
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{},results:observations.map(observation=>({observation}))}),
    saveProcessing:async(_id,body)=>{saves++;assert.equal(body.review.contentValidity.status,'VALID_RECRUITMENT');assert.equal(body.expectedWorkflowRunId,'10001');return {version:8};},
    extract:()=>{throw Error('GEMINI_FORBIDDEN');},claim:()=>{throw Error('CLAIM_FORBIDDEN');},complete:()=>{throw Error('FINALIZE_FORBIDDEN');}};
  const result=await worker.replayDiscoveryExecution({executionId:id},{backend});
  assert.equal(result.providerRequests,0);assert.equal(saves,1);
});
test('one rejected processing callback does not discard later posts or claim success',async()=>{
  const observations=[source('Hiring Backend Developer'),{...source('I am looking for a job.'),itemIndex:1}];const seen=[];
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version:7,configuration:{},results:observations.map(observation=>({observation}))}),
    saveProcessing:async(_id,p)=>{seen.push(p.itemIndex);if(p.itemIndex===0)throw Error('SYNTHETIC_ITEM_VALIDATION');return {version:8};}};
  const result=await worker.replayDiscoveryExecution({executionId:id},{backend});
  assert.deepEqual(seen,[0,1]);assert.equal(result.state,'PROCESSING_INCOMPLETE');assert.deepEqual(result.failedItemIndexes,[0]);assert.equal(result.providerRequests,0);
});
