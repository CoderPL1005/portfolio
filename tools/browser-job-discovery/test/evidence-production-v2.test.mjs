import test from 'node:test';
import assert from 'node:assert/strict';
import {captureEvidence} from './fixtures/evidence-production-v2.mjs';
import {extractRecruitmentPost} from '../src/ai-job-extraction.mjs';

for(const scenario of ['exact','corrected','bytes','inline','multi','cross','ambiguous','fabricated','missing','unknown','conflict','rejected'])test(`offline worker V2: ${scenario}`,async()=>{
  const wire=await captureEvidence(scenario);const p=wire.processing.at(-1);assert.ok(p);assert.equal(p.evidencePolicy,'V2');
  for(const v of p.vacancies)for(const f of Object.values(v.fields))for(const e of f.evidence)assert.equal(wire.observation.rawContent.slice(e.start,e.end),e.quote);
  if(scenario==='unknown')assert.equal(wire.cacheReads,0);
  if(scenario==='fabricated')assert.ok(p.vacancies.every(v=>v.companyName!=='Fabricated Labs'));
  if(scenario==='ambiguous')assert.ok(p.vacancies.every(v=>v.fields.location.origin!=='GEMINI'));
  if(scenario==='cross')assert.ok(p.vacancies.filter(v=>v.positionTitle?.includes('Backend')).every(v=>!v.technologyStack?.includes('React')));
});
test('default remains strict and rejects unknown policy before any provider call',async()=>{
  const {observation,generation}=await captureEvidence();let calls=0;
  const provider={modelIdentity:generation.modelIdentity,extract:async()=>{calls++;return generation.output;}};
  const input={...observation,sourcePostId:observation.providerPostId};
  const result=await extractRecruitmentPost(input,{provider});assert.equal(result.evidencePolicy,'STRICT');assert.equal(result.employer.value,null);
  const v2=await extractRecruitmentPost(input,{provider,evidencePolicy:'V2'});
  assert.equal(result.cacheKey,v2.cacheKey);assert.notEqual(result.validationFingerprint,v2.validationFingerprint);
  await assert.rejects(extractRecruitmentPost(input,{provider,evidencePolicy:'forged'}),/POLICY_INVALID/);assert.equal(calls,2);
});
