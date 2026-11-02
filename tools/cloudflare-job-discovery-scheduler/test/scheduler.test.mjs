import test from 'node:test';
import assert from 'node:assert/strict';
import handler, { PRIMARY_CRON, FALLBACK_CRON, resolveScheduledSlot } from '../src/index.mjs';

test('grouped cron constants use exact hours and 30-minute fallbacks', () => {
  assert.equal(PRIMARY_CRON, '0 0,3,6,9,12,15 * * *');
  assert.equal(FALLBACK_CRON, '30 0,3,6,9,12 * * *');
});

for (const hour of [0, 3, 6, 9, 12, 15]) {
  test(`primary hour ${hour} at minute 0 gives authorized slot`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:00:00Z`);
    const result = resolveScheduledSlot(PRIMARY_CRON, date, date + 1_000);
    assert.equal(result.state, 'READY');
    assert.equal(result.slot, `0 ${hour} * * *`);
    assert.equal(result.kind, hour === 15 ? 'RESERVED' : 'PRIMARY');
    assert.equal(result.scheduledUtc, new Date(date).toISOString().replace('.000Z', 'Z'));
  });
  if (hour === 15) continue;
  test(`fallback hour ${hour} at minute 30 gives authorized slot`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:30:00Z`);
    const result = resolveScheduledSlot(FALLBACK_CRON, date, date + 100);
    assert.equal(result.state, 'READY');
    assert.equal(result.slot, `30 ${hour} * * *`);
    assert.equal(result.kind, 'FALLBACK');
    assert.equal(result.scheduledUtc, new Date(date).toISOString().replace('.000Z', 'Z'));
  });
}

test('reserve slot6 has no fallback', () => {
  const ts = Date.parse('2026-11-03T15:30:00Z');
  assert.equal(resolveScheduledSlot(FALLBACK_CRON, ts, ts + 1_000).state, 'CRON_TIME_MISMATCH');
});

test('old 11-minute and 41-minute expressions and mismatched minutes are rejected', () => {
  const ts00 = Date.parse('2026-11-03T00:00:00Z');
  const ts11 = Date.parse('2026-11-03T00:11:00Z');
  const ts41 = Date.parse('2026-11-03T00:41:00Z');
  assert.equal(resolveScheduledSlot('11 0,3,6,9,12,15 * * *', ts11, ts11).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot('41 0,3,6,9,12 * * *', ts41, ts41).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot('11 0 * * *', ts11, ts11).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot('41 0 * * *', ts41, ts41).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot(PRIMARY_CRON, ts11, ts11).state, 'CRON_TIME_MISMATCH');
  assert.equal(resolveScheduledSlot(FALLBACK_CRON, ts41, ts41).state, 'CRON_TIME_MISMATCH');
  assert.equal(resolveScheduledSlot(FALLBACK_CRON, ts00, ts00).state, 'CRON_TIME_MISMATCH');
});

test('invalid cron and stale scheduled triggers never dispatch', () => {
  const ts = Date.parse('2026-11-03T00:00:00Z');
  assert.equal(resolveScheduledSlot('* * * * *', ts, ts).state, 'UNRECOGNIZED_CRON');
  assert.equal(resolveScheduledSlot(PRIMARY_CRON, ts, ts + 21 * 60_000).state, 'SKIPPED_DELAYED');
  assert.equal(resolveScheduledSlot(PRIMARY_CRON, ts, ts - 90_000).state, 'SKIPPED_DELAYED');
});

test('no public HTTP trigger', async () => {
  const r = await handler.fetch(new Request('https://example.com/?slot=0%200'));
  assert.equal(r.status, 404);
});

test('disabled mode never fetches', async () => {
  let called = false;
  const old = globalThis.fetch;
  globalThis.fetch = async () => { called = true; throw new Error('unexpected fetch'); };
  try {
    const ts = Date.parse('2026-11-03T00:00:00Z');
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
    assert.deepEqual(body, { ref: 'main', inputs: { mode: 'external_scheduled', slot: '0 0 * * *', scheduled_utc: '2026-11-03T00:00:00Z' } });
    return { ok: true, status: 204 };
  };
  const ts = Date.parse('2026-11-03T00:00:00Z');
  const oldNow = Date.now;
  Date.now = () => ts + 1_000;
  try {
    await handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED: 'true', GITHUB_TOKEN: 'TEST_SECRET' });
    assert.equal(calls, 1);
  } finally { Date.now = oldNow; globalThis.fetch = old; }
});

test('unknown or failed GitHub dispatch outcome is never retried automatically', async () => {
  let calls = 0;
  const old = globalThis.fetch;
  globalThis.fetch = async () => {
    calls++;
    return { ok: false, status: 502 };
  };
  const ts = Date.parse('2026-11-03T03:00:00Z');
  const oldNow = Date.now;
  Date.now = () => ts + 1_000;
  try {
    await assert.rejects(
      () => handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED: 'true', GITHUB_TOKEN: 'TEST_SECRET' }),
      /GITHUB_DISPATCH_FAILED_HTTP_502/
    );
    assert.equal(calls, 1);
  } finally { Date.now = oldNow; globalThis.fetch = old; }
});
