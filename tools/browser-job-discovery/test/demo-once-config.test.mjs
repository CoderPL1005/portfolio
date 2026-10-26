import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { DEMO_MARKER, DEMO_CONFIG, validateDemoConfig, isNewDemoMarker } from '../src/demo-once-config.mjs';

const root = resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const source = JSON.parse(readFileSync(resolve(root, DEMO_CONFIG), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));

test('fixed one-request development demo maps to safe dispatch inputs', () => {
  const inputs = validateDemoConfig(source);
  assert.equal(inputs.max_requests, '1');
  assert.equal(inputs.max_credits, '1');
  assert.equal(inputs.auto_admin_sync, 'true');
  assert.equal(inputs.initialize_state, 'false');
  assert.equal(inputs.confirm_artifact_data, 'false');
  assert.equal(inputs.query, 'Fresher .NET Hà Nội');
  assert.equal(inputs.start_time, '2026-10-26');
  assert.equal(inputs.end_time, '2026-10-26');
});

test('changes to budget, policy, query or date fail closed', () => {
  for (const mutation of [
    c => { c.maxRequests = 2; },
    c => { c.maxCredits = 2; },
    c => { c.startTime = '2026-10-27'; },
    c => { c.query = 'Other'; },
    c => { c.policy.mode = 'production'; },
    c => { c.policy.planQueries = true; },
    c => { c.policy.profile.technologies.push('Python'); }
  ]) {
    const config = clone(source); mutation(config);
    assert.throws(() => validateDemoConfig(config), /ONE_SHOT_DEMO_CONFIG_MISMATCH/);
  }
});

test('only a newly added marker can arm the trigger', () => {
  assert.equal(isNewDemoMarker([{filename:DEMO_MARKER,status:'added'}]),true);
  assert.equal(isNewDemoMarker([{filename:DEMO_MARKER,status:'modified'}]),false);
  assert.equal(isNewDemoMarker([{filename:DEMO_MARKER,status:'removed'}]),false);
  assert.equal(isNewDemoMarker([{filename:'.github/workflows/other.yml',status:'added'}]),false);
});
