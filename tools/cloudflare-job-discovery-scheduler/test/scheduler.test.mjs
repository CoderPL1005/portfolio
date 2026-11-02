import test from 'node:test';
import assert from 'node:assert/strict';
import handler, { PRIMARY_CRON, FALLBACK_CRON, resolveScheduledSlot } from '../src/index.mjs';

for (const hour of [0, 3, 6, 9, 12, 15]) {
  test(`primary hour ${hour} gives authorized slot`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:11:00Z`);
    const result = resolveScheduledSlot(PRIMARY_CRON, date, date + 1_000);
    assert.equal(result.state, 'READY');
    assert.equal(result.slot, `11 ${hour} * * *`);
    assert.equal(result.scheduledUtc, new Date(date).toISOString().replace('.000Z', 'Z'));
  });
  if (hour === 15) continue;
  test(`fallback hour ${hour} gives authorized slot`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:41:00Z`);
    assert.equal(resolveScheduledSlot(FALLBACK_CRON, date, date + 100).slot, `41 ${hour} * * *`);
  });
}
test('reserve slot6 has no fallback', () => {
  const ts = Date.parse('2026-11-03T15:41:00Z');
  assert.equal(resolveScheduledSlot(FALLBACK_CRON, ts, ts + 1_000).state, 'CRON_TIME_MISMATCH');
});
test('invalid cron and stale scheduled triggers never dispatch', () => {
  const ts = Date.parse('2026-11-03T00:11:00Z');
  assert.equal(resolveScheduledSlot('* * * * *', ts, ts).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot(PRIMARY_CRON, ts, ts + 21 * 60_000).state, 'SKIPPED_DELAYED');
  assert.equal(resolveScheduledSlot(PRIMARY_CRON, ts, ts - 90_000).state, 'SKIPPED_DELAYED');
});
test('no public HTTP trigger', async () => {
  const r = await handler.fetch(new Request('https://example.com/?slot=11%200'));
  assert.equal(r.status, 404);
});
test('disabled mode never fetches', async () => {
  let called = false;
  const old = globalThis.fetch;
  globalThis.fetch = async () => { called = true; throw new Error('unexpected fetch'); };
  try {
    const ts = Date.parse('2026-11-03T00:11:00Z');
    // Inject Date.now for deterministic scheduled handler check.
    const oldNow = Date.now;
    Date.now = () => ts + 1_000;
    try {
      await handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED: 'false' });
      assert.equal(called, false);
    } finally { Date.now = oldNow; }
  } finally { globalThis.fetch = old; }
});
test('enabled dispatch passes only fixed GitHub workflow inputs, exactly once', async () => {
  let calls = 0;
  const old = globalThis.fetch;
  globalThis.fetch = async (url, opts) => {
    calls++;
    assert.match(url, /\/portfolio\/actions\/workflows\/job-discovery-daily\.yml\/dispatches$/);
    assert.equal(opts.method, 'POST');
    assert.equal(opts.headers.Authorization, 'Bearer TEST_SECRET');
    const body = JSON.parse(opts.body);
    assert.deepEqual(body, { ref:'main', inputs: { mode:'external_scheduled', slot:'11 3 * * *', scheduled_utc:'2026-11-03T03:11:00Z' }});
    return { ok: true, status: 204 };
  };
  const ts = Date.parse('2026-11-03T03:11:00Z');
  const oldNow = Date.now;
  Date.now = () => ts + 1_000;
  try {
    await handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED:'true', GITHUB_TOKEN:'TEST_SECRET' });
    assert.equal(calls, 1);
  } finally { Date.now = oldNow; globalThis.fetch = old; }
});
