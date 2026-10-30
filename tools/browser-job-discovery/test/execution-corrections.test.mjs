import test from 'node:test';
import assert from 'node:assert/strict';
import {captureExecution} from './fixtures/execution-corrections.mjs';
test('R2 typed provider error survives successful HTTP transport',async()=>{
  const {result,requests}=await captureExecution('provider-error');assert.equal(requests,1);
  assert.equal(result.state,'FAILED');assert.equal(result.errorCode,'PROVIDER_ERROR');
  assert.equal(result.providerOperation,'FAILED');
});
test('R3 rejected URL keeps original provider provenance without promoting it to a canonical URL',async()=>{
  const {result}=await captureExecution('invalid-url');const o=result.observations[0];
  assert.equal(o.sourceUrl,null);assert.equal(o.providerSourceUrl,'https://example.invalid/not-facebook');
  assert.equal(o.outcome,'POST_URL_INVALID_OR_UNSUPPORTED');
});
for(const scenario of ['unknown','contradictory'])test(`R4 ${scenario} cost stops extraction before normalization`,async()=>{
  const {result,semantic,requests,saved}=await captureExecution(scenario);
  assert.equal(requests,1);assert.equal(semantic,0);assert.equal(result.state,'PROVIDER_UNKNOWN');
  assert.equal(result.providerOperation,'UNKNOWN');assert.deepEqual(result.observations[0].vacancies,[]);
  assert.equal(saved.usageEvidence.bodyCreditsCharged,scenario==='contradictory'?2:1);
  assert.equal(result.creditsUsedAlternateHeader,6);
});
for(const scenario of ['zero','success'])test(`R4 bounded known usage ${scenario} remains usable`,async()=>{
  const {result,requests}=await captureExecution(scenario);assert.equal(requests,1);
  assert.equal(result.state,'PARTIAL');assert.equal(result.providerOperation,'SUCCEEDED');assert.equal(result.creditsDeducted,scenario==='zero'?0:1);
});
for(const overrides of [{bodyCost:0},{bodyCost:-1},{bodyCost:'1'},{bodyCost:null},{headerCost:'invalid'},{headerCost:'0.5',bodyCost:1}])
  test(`R4 contradictory/malformed usage ${JSON.stringify(overrides)} stops safely`,async()=>{
    const {result,semantic,requests}=await captureExecution('success',overrides);
    assert.equal(requests,1);assert.equal(semantic,0);assert.equal(result.state,'PROVIDER_UNKNOWN');
    assert.deepEqual(result.observations[0].vacancies,[]);
  });
test('R4 known fractional cost and absent optional body evidence do not invent charge',async()=>{
  const {result}=await captureExecution('success',{headerCost:'0.5',bodyCost:0.5});assert.equal(result.creditsDeducted,0.5);assert.equal(result.state,'PARTIAL');assert.equal(result.providerOperation,'SUCCEEDED');
  const absent=await captureExecution('success',{omitBodyCost:true});assert.equal(absent.result.usageEvidence.bodyCostPresent,false);assert.equal(absent.result.state,'PARTIAL');assert.equal(absent.result.providerOperation,'SUCCEEDED');
});
test('R2 headers received followed by ambiguous body failure holds execution',async()=>{
  const {result,requests,semantic}=await captureExecution('body-read-failure');
  assert.equal(requests,1);assert.equal(semantic,0);assert.equal(result.httpStatus,200);assert.equal(result.creditsDeducted,1);
  assert.equal(result.state,'PROVIDER_UNKNOWN');assert.equal(result.providerOperation,'UNKNOWN');
});
test('R4 preserves usage independently and never forwards a reflected credential',async()=>{
  const {result}=await captureExecution('success',{headerCost:'SYNTHETIC_OFFLINE_PROVIDER_KEY'});
  assert.equal(result.state,'PROVIDER_UNKNOWN');assert.equal(result.usageEvidence.deductedHeader,'INVALID_OR_REDACTED');
  assert.equal(result.usageEvidence.alternateHeader,'6');assert.equal(result.usageEvidence.bodyCreditsCharged,1);
  assert.ok(!JSON.stringify(result).includes('SYNTHETIC_OFFLINE_PROVIDER_KEY'));
});
for(const scenario of ['success','multi'])test(`R5 ${scenario} array evidence supports every element in its role`,async()=>{
  const {result}=await captureExecution(scenario);const o=result.observations[0];
  assert.equal(o.vacancies.length,scenario==='multi'?2:1);
  for(const v of o.vacancies){const field=v.fields.technologyStack;assert.ok(field.value.length>0);
    for(const technology of field.value)assert.ok(field.evidence.some(e=>e.quote.toLowerCase()===technology.toLowerCase()&&o.rawContent.slice(e.start,e.end)===e.quote));
  }
});
