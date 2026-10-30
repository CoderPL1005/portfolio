import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {replayDiscoveryExecution} from '../src/discovery-execution-worker.mjs';
import {POST_FIELDS,VACANCY_FIELDS} from '../src/ai-extraction-schema.mjs';
import {supportedPostingFacts} from '../src/discovery-posting-facts.mjs';

// Synthetic recruitment, never a captured post or a claim of real Gemini quality.
const id='11111111-1111-4111-8111-111111111111';
const originalFetch=globalThis.fetch;
const raw='Company: Original Labs\nHiring\nIntern Backend Developer\nPartner: TAX NET\nLocation: Hanoi\nReference: 35 Lê Văn Thiêm\nRequirements: C# and .NET\nBenefits: Lunch\nApply: jobs@example.invalid';
const unknown=()=>({value:null,status:'UNKNOWN',evidence:[]});
const fact=(value,quote=value)=>{const start=raw.indexOf(quote);assert.ok(start>=0);return {value,status:'POST_MENTIONED',evidence:[{start,end:start+quote.length,quote}]};};
function output(){return {schemaVersion:'ai-job-extraction-v1',...Object.fromEntries(POST_FIELDS.map(k=>[k,unknown()])),
  employer:fact('TAX NET'),sharedLocation:fact('35 Lê Văn Thiêm'),sharedBenefits:fact('Lunch'),
  vacancies:[{...Object.fromEntries(VACANCY_FIELDS.map(k=>[k,unknown()])),title:fact('Intern Backend Developer'),technologies:fact(['C#','.NET'],'C# and .NET')}]};}
function harness(text=raw,result=output(),semanticState='NOT_REQUESTED'){
  let calls=0,version=10;const saves=[];
  const observation={itemIndex:0,providerPostId:'9001',sourceUrl:'https://www.facebook.com/fixture/posts/9001',providerSourceUrl:null,
    rawContent:text,contentHash:createHash('sha256').update(text).digest('hex'),publishedAt:null,outcome:'READY_FOR_EXTRACTION'};
  const backend={getPersisted:async()=>({id,workflowRunId:'10001',version,configuration:{semanticEnabled:false},semanticReplay:{enabled:true,modelIdentity:'offline-mock-v1'},results:[{observation,semanticState}]}),
    extractPersisted:async()=>{calls++;return {version:++version,generation:{modelIdentity:'offline-mock-v1',output:typeof result==='string'?result:JSON.stringify(result)}};},
    saveProcessing:async(_id,p)=>{saves.push(structuredClone(p));return {version:++version};}};
  return {backend,saves,get calls(){return calls;},observation};
}
test.before(()=>{globalThis.fetch=()=>{throw Error('NETWORK_FORBIDDEN');};});
test.after(()=>{globalThis.fetch=originalFetch;});
test('uncertain recruitment never automatically spends Gemini attempts',async()=>{
  const h=harness('Backend discussion');await replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},h);
  assert.equal(h.calls,0);assert.equal(h.saves.at(-1).review.contentValidity.status,'NEEDS_REVIEW');
});
test('supported deterministic facts win a semantic conflict; supplementation retains origin and exact evidence',async()=>{
  const h=harness();await replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},h);
  assert.equal(h.calls,1);const v=h.saves.at(-1).vacancies[0];
  assert.equal(v.companyName,'Original Labs');assert.equal(v.location,'Location: Hanoi');
  assert.equal(v.fields.companyName.origin,'DETERMINISTIC');assert.equal(v.fields.companyName.conflict,true);
  assert.equal(v.fields.technologyStack.status,'EXTRACTED');
  for(const f of Object.values(v.fields))for(const e of f.evidence)assert.equal(raw.slice(e.start,e.end),e.quote);
  assert.equal(h.saves.at(-1).review.rawContent,raw);assert.equal(h.saves.at(-1).review.publishedAt,null);
});
test('a validated new company/location supplements missing fields with Gemini provenance',async()=>{
  const text=raw.replace('Company: Original Labs\n','').replace('Location: Hanoi\n','');
  const o=output();const rebase=n=>{if(!n||typeof n!=='object')return;for(const e of n.evidence??[]){e.start=text.indexOf(e.quote);e.end=e.start+e.quote.length;}for(const v of Object.values(n))rebase(v);};rebase(o);
  const h=harness(text,o);await replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},h);
  const fields=h.saves.at(-1).vacancies[0].fields;
  assert.equal(fields.companyName.value,'TAX NET');assert.equal(fields.companyName.origin,'GEMINI');
  assert.equal(fields.location.value,'35 Lê Văn Thiêm');assert.equal(fields.location.origin,'GEMINI');
});
for(const bad of ['{bad JSON',JSON.stringify({...output(),employer:fact('Unsupported employer','TAX NET')})])
test(`bad or unsupported Gemini output preserves classification and original content: ${bad.slice(0,12)}`,async()=>{
  const h=harness(raw,bad);await replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},h);
  const p=h.saves.at(-1);assert.equal(p.review.contentValidity.status,'VALID_RECRUITMENT');assert.equal(p.review.rawContent,raw);
  assert.ok(p.vacancies.every(v=>v.companyName!=='Unsupported employer'));
});
test('default saved replay remains Gemini off and no provider port is needed',async()=>{
  const h=harness();await replayDiscoveryExecution({executionId:id},h);assert.equal(h.calls,0);
  assert.equal(h.saves.at(-1).review.rawContent,raw);
});
test('literal employment/workplace/application instructions keep exact evidence, unknowns abstain',()=>{
  const text='Employment type: INTERNSHIP\nWorkplace type: HYBRID\nApplication instructions: Send a portfolio to the hiring team.';
  const supported=supportedPostingFacts(text);assert.equal(supported.employmentType.value,'INTERNSHIP');assert.equal(supported.workplaceType.value,'HYBRID');
  assert.equal(supported.notes.value,'Send a portfolio to the hiring team.');
  for(const f of Object.values(supported))assert.ok(text.includes(f.quote));
  assert.deepEqual(supportedPostingFacts('Employment type: perhaps full-time\nWorkplace type: sometimes elsewhere'),{});
});
