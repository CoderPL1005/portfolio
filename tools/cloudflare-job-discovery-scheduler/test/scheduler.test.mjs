import test from 'node:test';
import assert from 'node:assert/strict';
import handler, { PRIMARY_CRON, FALLBACK_CRON, resolveScheduledSlot, buildScheduleDiagnostics } from '../src/index.mjs';

test('grouped cron constants use exact hours and 30-minute fallbacks', () => {
  assert.equal(PRIMARY_CRON, '0 0,3,6,9,12,15 * * *');
  assert.equal(FALLBACK_CRON, '30 0,3,6,9,12 * * *');
});

for (const hour of [0, 3, 6, 9, 12, 15]) {
  test(`primary hour ${hour} at minute 0 gives authorized slot and canonical :00Z scheduledUtc`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:00:32.654Z`);
    const result = resolveScheduledSlot(PRIMARY_CRON, date, date + 1_000);
    assert.equal(result.state, 'READY');
    assert.equal(result.slot, `0 ${hour} * * *`);
    assert.equal(result.kind, hour === 15 ? 'RESERVED' : 'PRIMARY');
    assert.equal(result.scheduledUtc, `2026-11-03T${String(hour).padStart(2, '0')}:00:00Z`);
    assert.match(result.scheduledUtc, /:00Z$/);
  });
  if (hour === 15) continue;
  test(`fallback hour ${hour} at minute 30 gives authorized slot and canonical :00Z scheduledUtc`, () => {
    const date = Date.parse(`2026-11-03T${String(hour).padStart(2, '0')}:30:32.654Z`);
    const result = resolveScheduledSlot(FALLBACK_CRON, date, date + 100);
    assert.equal(result.state, 'READY');
    assert.equal(result.slot, `30 ${hour} * * *`);
    assert.equal(result.kind, 'FALLBACK');
    assert.equal(result.scheduledUtc, `2026-11-03T${String(hour).padStart(2, '0')}:30:00Z`);
    assert.match(result.scheduledUtc, /:00Z$/);
  });
}

test('production regression: 2026-11-02T15:00:32.654Z resolves to reserved READY with canonical 15:00:00Z', () => {
  const ts = Date.parse('2026-11-02T15:00:32.654Z');
  const result = resolveScheduledSlot(PRIMARY_CRON, ts, ts + 250);
  assert.deepEqual(result, {
    state: 'READY',
    slot: '0 15 * * *',
    scheduledUtc: '2026-11-02T15:00:00Z',
    kind: 'RESERVED',
  });
});

test('primary 2026-11-03T00:00:32.654Z and fallback 2026-11-03T00:30:32.654Z resolve to READY with canonical :00Z', () => {
  const primaryTs = Date.parse('2026-11-03T00:00:32.654Z');
  assert.deepEqual(resolveScheduledSlot(PRIMARY_CRON, primaryTs, primaryTs + 500), {
    state: 'READY',
    slot: '0 0 * * *',
    scheduledUtc: '2026-11-03T00:00:00Z',
    kind: 'PRIMARY',
  });

  const fallbackTs = Date.parse('2026-11-03T00:30:32.654Z');
  assert.deepEqual(resolveScheduledSlot(FALLBACK_CRON, fallbackTs, fallbackTs + 500), {
    state: 'READY',
    slot: '30 0 * * *',
    scheduledUtc: '2026-11-03T00:30:00Z',
    kind: 'FALLBACK',
  });
});

test('wrong minute andwrong hour are rejected without rounding into another slot', () => {
  const wrongMinutePrimary = Date.parse('2026-11-03T00:01:00Z');
  const m1 = resolveScheduledSlot(PRIMARY_CRON, wrongMinutePrimary, wrongMinutePrimary);
  assert.equal(m1.state, 'CRON_TIME_MISMATCH');
  assert.equal(m1.reason, 'SCHEDULED_MINUTE_MISMATCH');
  assert.equal(m1.expectedMinute, 0);
  assert.equal(m1.actualMinute, 1);

  const nearHourEnd = Date.parse('2026-11-02T14:59:59.999Z');
  const m2 = resolveScheduledSlot(PRIMARY_CRON, nearHourEnd, nearHourEnd);
  assert.equal(m2.state, 'CRON_TIME_MISMATCH');
  assert.equal(m2.reason, 'UNAUTHORIZED_UTC_HOUR');

  const nearSlotEnd = Date.parse('2026-11-03T00:29:59.999Z');
  const m3 = resolveScheduledSlot(FALLBACK_CRON, nearSlotEnd, nearSlotEnd);
  assert.equal(m3.state, 'CRON_TIME_MISMATCH');
  assert.equal(m3.reason, 'SCHEDULED_MINUTE_MISMATCH');

  for (const badHour of [1, 2, 4, 5, 14, 16, 23]) {
    const ts = Date.parse(`2026-11-03T${String(badHour).padStart(2, '0')}:00:00Z`);
    const r = resolveScheduledSlot(PRIMARY_CRON, ts, ts);
    assert.equal(r.state, 'CRON_TIME_MISMATCH');
    assert.equal(r.reason, 'UNAUTHORIZED_UTC_HOUR');
    assert.equal(r.actualHour, badHour);
  }
});

test('reserve slot6 has no fallback even with seconds/milliseconds', () => {
  for (const iso of ['2026-11-03T15:30:00Z', '2026-11-03T15:30:32.654Z']) {
    const ts = Date.parse(iso);
    const r = resolveScheduledSlot(FALLBACK_CRON, ts, ts + 1_000);
    assert.equal(r.state, 'CRON_TIME_MISMATCH');
    assert.equal(r.reason, 'RESERVED_SLOT_HAS_NO_FALLBACK');
  }
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

test('invalid cron and stale or future scheduled triggers never dispatch', () => {
  const ts = Date.parse('2026-11-03T00:00:32.654Z');
  assert.equal(resolveScheduledSlot('* * * * *', ts, ts).state, 'UNRECOGNIZED_CRON');
  const expired = resolveScheduledSlot(PRIMARY_CRON, ts, ts + 21 * 60_000);
  assert.equal(expired.state, 'SKIPPED_DELAYED');
  assert.equal(expired.reason, 'EXPIRED_SCHEDULED_TIME');
  const future = resolveScheduledSlot(PRIMARY_CRON, ts, ts - 90_000);
  assert.equal(future.state, 'SKIPPED_DELAYED');
  assert.equal(future.reason, 'FUTURE_SCHEDULED_TIME');
});

test('safe diagnostics include cron, raw/parsed/invocation times, expected/actual minutes and rejection reason without secrets', () => {
  const ts = Date.parse('2026-11-02T15:01:32.654Z');
  const nowMs = Date.parse('2026-11-02T15:01:33.100Z');
  const diag = buildScheduleDiagnostics(PRIMARY_CRON, ts, nowMs);
  assert.deepEqual(diag, {
    cron: PRIMARY_CRON,
    rawScheduledTime: ts,
    parsedScheduledUtc: '2026-11-02T15:01:32.654Z',
    actualInvocationUtc: '2026-11-02T15:01:33.100Z',
    expectedCronMinute: 0,
    actualParsedMinute: 1,
    canonicalScheduledUtc: null,
    state: 'CRON_TIME_MISMATCH',
    rejectionReason: 'SCHEDULED_MINUTE_MISMATCH',
  });
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
    const ts = Date.parse('2026-11-03T00:00:32.654Z');
    const oldNow = Date.now;
    Date.now = () => ts + 1_000;
    try {
      await handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED: 'false' });
      assert.equal(called, false);
    } finally { Date.now = oldNow; }
  } finally { globalThis.fetch = old; }
});

test('enabled dispatch normalizes non-zero seconds/milliseconds to :00Z and never logs secrets', async () => {
  let calls = 0;
  const logs = [];
  const oldFetch = globalThis.fetch;
  const oldLog = console.log;
  console.log = (...args) => logs.push(args.join(' '));
  globalThis.fetch = async (url, opts) => {
    calls++;
    assert.match(url, /\/portfolio\/actions\/workflows\/job-discovery-daily\.yml\/dispatches$/);
    assert.equal(opts.method, 'POST');
    assert.equal(opts.headers.Authorization, 'Bearer TEST_SECRET_DO_NOT_LOG');
    const body = JSON.parse(opts.body);
    assert.deepEqual(body, { ref: 'main', inputs: { mode: 'external_scheduled', slot: '0 0 * * *', scheduled_utc: '2026-11-03T00:00:00Z' } });
    return { ok: true, status: 204 };
  };
  const ts = Date.parse('2026-11-03T00:00:32.654Z');
  const oldNow = Date.now;
  Date.now = () => ts + 1_000;
  try {
    await handler.scheduled({ cron: PRIMARY_CRON, scheduledTime: ts }, { DISPATCH_ENABLED: 'true', GITHUB_TOKEN: 'TEST_SECRET_DO_NOT_LOG' });
    assert.equal(calls, 1);
    const combinedLogs = logs.join('\n');
    assert.match(combinedLogs, /SCHEDULER_DIAGNOSTICS/);
    assert.match(combinedLogs, /2026-11-03T00:00:32\.654Z/);
    assert.doesNotMatch(combinedLogs, /TEST_SECRET_DO_NOT_LOG/);
    assert.doesNotMatch(combinedLogs, /Bearer/);
  } finally { Date.now = oldNow; globalThis.fetch = oldFetch; console.log = oldLog; }
});

test('unknown or failed GitHub dispatch outcome is never retried automatically', async () => {
  let calls = 0;
  const old = globalThis.fetch;
  globalThis.fetch = async () => {
    calls++;
    return { ok: false, status: 502 };
  };
  const ts = Date.parse('2026-11-03T03:00:32.654Z');
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
