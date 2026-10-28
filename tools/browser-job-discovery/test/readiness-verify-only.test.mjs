import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parse} from 'yaml';
import * as inspection from '../src/checkpoint-inspection.mjs';
import {REVIEWED_WORKFLOW_HASHES,workflowHash} from '../src/checkpoint-history.mjs';

const yaml=await readFile(new URL('../../../.github/workflows/socialapis-job-discovery.yml',import.meta.url),'utf8');
const workflow=parse(yaml),job=workflow.jobs.discover,steps=job.steps;
const cli=await readFile(new URL('../src/pagination-cli.mjs',import.meta.url),'utf8');
const restore=steps.find(s=>s.name==='Decrypt restored checkpoint (legacy plaintext remains supported)');
const excluded=s=>assert.match(s.if??'',/!inputs\.verify_only/u,`${s.name??s.uses??s.run} must explicitly exclude verify-only`);
const report=value=>{assert.equal(typeof inspection.readinessReport,'function','safe readiness report behavior is not implemented');return inspection.readinessReport(value);};

test('T01 verify-only is opt-in boolean, manual and main-only',()=>{
  assert.deepEqual(Object.keys(workflow.on),['workflow_dispatch']);
  assert.equal(workflow.on.workflow_dispatch.inputs.verify_only?.type,'boolean');
  assert.equal(workflow.on.workflow_dispatch.inputs.verify_only.default,false);
  assert.match(job.if,/github\.ref == 'refs\/heads\/main'/u);
  assert.match(job.if,/inputs\.verify_only\s*\|\|/u);
});
test('T02 locator cannot initialize empty state in verify-only',()=>{
  const locate=steps.find(s=>s.id==='checkpoint');
  assert.match(locate.with.script,/locateCheckpoint/u);
  assert.match(locate.env.INITIALIZE_STATE,/!inputs\.verify_only/u);
});
test('T03 authenticated recovery does not depend on a fresh session in verify-only',()=>{
  assert.ok(restore);
  assert.match(restore.env.DISCOVERY_RECOVER_ACKNOWLEDGED,/inputs\.verify_only\s*\|\|/u);
  assert.equal(restore.env.DISCOVERY_VERIFY_ONLY,'${{ inputs.verify_only }}');
  assert.match(cli,/restoreAcknowledgedFromGitHub/u);
  assert.match(cli,/process\.env\.DISCOVERY_VERIFY_ONLY\s*===\s*['"]true['"]/u);
});
for(const action of ['init','fresh-init','sync-ready','sync','seal','summary']){
  test(`T04 ${action} never executes in verify-only`,()=>{
    const matches=steps.filter(s=>s.run?.split(/\r?\n/u).some(line=>line.trim()===`node src/cli.mjs --orchestrate ${action}`));
    assert.ok(matches.length>0);
    for(const step of matches)excluded(step);
  });
}
test('T04 provider composite explicitly skipped (claim/page/ack/sync unreachable)',()=>{
  const provider=steps.filter(s=>s.uses==='./.github/actions/socialapis-discovery-page');
  assert.equal(provider.length,1);for(const step of provider)excluded(step);
});
test('T04 activation writes and all uploads explicitly skipped',()=>{
  const writes=steps.filter(s=>s.uses?.startsWith('actions/upload-artifact')||s.with?.script?.includes('createRef'));
  assert.ok(writes.length>=3);for(const step of writes)excluded(step);
});
test('T05 verify-only has no Fresh Session ID environment dependency',()=>{
  assert.match(job.env.DISCOVERY_FRESH_SESSION_ID,/!inputs\.verify_only/u);
});
test('T06 READY report only exposes allowlisted metadata',()=>{
  const selection={status:'SELECTED',reason:'ACKNOWLEDGED_FINAL_VERIFIED',artifactId:12723681280,
    encryptedCheckpoint:Buffer.from('private'),stateFingerprint:'private',query:'private'};
  assert.deepEqual(report(selection),{status:'READY',reason:'ACKNOWLEDGED_FINAL_VERIFIED',selectedArtifactId:12723681280,providerRequests:0});
});
for(const reason of ['CONTENT_BINDING_UNVERIFIED','CONTENT_INTEGRITY_VIOLATION']){
  test(`T07 ${reason} remains BLOCKED`,()=>{
    const result=report({status:'BLOCKED',reason,artifactId:12723681280});
    assert.equal(result.status,'BLOCKED');assert.equal(result.selectedArtifactId,null);assert.equal(result.providerRequests,0);
    assert.match(result.reason,/^[A-Z][A-Z0-9_]+$/u);
  });
}
test('T06 arbitrary exception content never exposed',()=>{
  const result=report({status:'BLOCKED',reason:'secret JD contact@example.invalid'});
  assert.equal(result.status,'BLOCKED');assert.doesNotMatch(JSON.stringify(result),/secret|@/u);
  assert.deepEqual(Object.keys(result).sort(),['providerRequests','reason','selectedArtifactId','status']);
});
test('T08 current reviewed hash and historical hash both recognized',()=>{
  assert.ok(REVIEWED_WORKFLOW_HASHES.has('e032d3ccbecb17f5b1920cd362f03e1905c9dadeee155c0e1d2d440bc336ea28'));
  assert.ok(REVIEWED_WORKFLOW_HASHES.has(workflowHash(yaml)));
});
test('T09 normal consent and single-request controls preserved',()=>{
  for(const input of ['confirm_live','confirm_free_no_cash','confirm_artifact_data'])assert.match(job.if,new RegExp(`inputs\\.${input}`,'u'));
  assert.deepEqual(workflow.on.workflow_dispatch.inputs.max_requests.options,['1']);
  assert.deepEqual(workflow.on.workflow_dispatch.inputs.max_credits.options,['1']);
  assert.equal(job.env.DISCOVERY_ONE_REQUEST_V1,'true');
  assert.equal(workflow.on.workflow_dispatch.inputs.policy.required,true);
  assert.equal(workflow.concurrency['cancel-in-progress'],false);
});
