import test from 'node:test';
import assert from 'node:assert/strict';
import { dailyObservationWindowFilter, observationsFrom, processObservations } from '../src/discovery-execution-worker.mjs';
import { normalizeSocialApisItem } from '../src/socialapis-live-smoke.mjs';

const daily = { dailySearch: { slot: 1, startTime: '2026-11-01', endTime: '2026-11-01', timeZone: 'Asia/Ho_Chi_Minh' }, triggerSource: 'DAILY_SCHEDULED' };
test('yesterday-only excludes out-of-range posts and renumbers items', () => {
  const src = [
    { itemIndex: 0, publishedAt: '2026-10-31T16:00:00Z' }, // Oct 31 in Vietnam
    { itemIndex: 1, publishedAt: '2026-11-01T01:00:00Z' }, // Nov 1 in Vietnam
    { itemIndex: 2, publishedAt: '2026-11-01T18:00:00Z' }, // Nov 2 in Vietnam
    { itemIndex: 3, publishedAt: null }, // unknown: reviewable, not silently invented
  ];
  const result = dailyObservationWindowFilter(src, daily);
  assert.deepEqual(result.map(x => x.itemIndex), [0, 1]);
  assert.equal(result[0].publishedAt, '2026-11-01T01:00:00Z');
  assert.equal(result[0].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');
  assert.equal(result[0].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(result[0].freshness, 'VERIFIED_IN_WINDOW');
  assert.deepEqual(result[0].requestedSearchWindow, { slot: 1, startTime: '2026-11-01', endTime: '2026-11-01', timeZone: 'Asia/Ho_Chi_Minh' });
  assert.equal(result[1].publishedAt, null);
  assert.equal(result[1].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');
  assert.equal(result[1].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(result[1].freshness, 'UNKNOWN');
});
test('slots 1-2 use yesterday+today and slots 3-5 use today-only in Vietnam timezone', () => {
  const src = [
    { itemIndex: 0, publishedAt: '2026-10-31T16:59:59Z' }, // Oct 31 23:59:59 ICT -> out of window
    { itemIndex: 1, publishedAt: '2026-10-31T17:00:00Z' }, // Nov 1 00:00:00 ICT -> yesterday
    { itemIndex: 2, publishedAt: '2026-11-01T17:00:00Z' }, // Nov 2 00:00:00 ICT -> today
    { itemIndex: 3, publishedAt: null }, // unknown date
  ];
  for (const slot of [1, 2]) {
    const res = dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED', dailySearch: { slot, startTime: '2026-11-01', endTime: '2026-11-02', timeZone: 'Asia/Ho_Chi_Minh' } });
    assert.equal(res.length, 3);
    assert.deepEqual(res.map(r => r.windowMatchStatus), ['VERIFIED_IN_WINDOW', 'VERIFIED_IN_WINDOW', 'UNKNOWN_DATE']);
  }
  for (const slot of [3, 4, 5]) {
    const res = dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED', dailySearch: { slot, startTime: '2026-11-02', endTime: '2026-11-02', timeZone: 'Asia/Ho_Chi_Minh' } });
    assert.equal(res.length, 2);
    assert.deepEqual(res.map(r => r.windowMatchStatus), ['VERIFIED_IN_WINDOW', 'UNKNOWN_DATE']);
    assert.equal(res[0].publishedAt, '2026-11-01T17:00:00Z');
  }
});
test('normalizeSocialApisItem records publishedAtProvenance and within-response duplicates skip second Gemini call', async () => {
  const withTime = normalizeSocialApisItem({ id: '9001', created_time: 1762041600, message: 'Hiring Fresher .NET Backend Developer in Hanoi' }, 0);
  const withoutTime = normalizeSocialApisItem({ basic_info: { post_id: '9002', post_text: 'Hiring Fresher .NET Backend Developer in Hanoi', url: 'https://www.facebook.com/fixture/posts/9002' } }, 1);
  assert.equal(withTime.publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');
  assert.equal(withoutTime.publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');
  assert.equal(withoutTime.publishedAt, null);

  const rawObs = observationsFrom([
        { basic_info: { post_id: '9002', post_text: 'Hiring Fresher .NET Backend Developer in Hanoi\nRequirements: C#, .NET\nLocation: Hanoi', url: 'https://www.facebook.com/fixture/posts/9002' } },
        { basic_info: { post_id: '9002', post_text: 'Hiring Fresher .NET Backend Developer in Hanoi\nRequirements: C#, .NET\nLocation: Hanoi', url: 'https://www.facebook.com/fixture/posts/9002' } },
      ]);
  assert.equal(rawObs.length, 2);
  assert.equal(rawObs[0].withinResponseDuplicatePost, false);
  assert.equal(rawObs[1].withinResponseDuplicatePost, true);
  assert.equal(rawObs[0].providerPostIdentity, 'fb:id:9002');

  let geminiCalls = 0;
  const processed = await processObservations({
    executionId: '00000000-0000-4000-8000-000000000001', observations: rawObs,
    config: { semanticEnabled: true, evidencePolicy: 'STRICT' }, policy: null, providerOperation: 'SUCCEEDED',
    backend: { extract: async () => { geminiCalls++; throw new Error('SYNTHETIC_OFFLINE'); } },
  });
  assert.ok(geminiCalls <= 1, 'duplicate post must never trigger a second Gemini call');
  assert.ok(processed.reviewItems[1].sourceWarnings.includes('WITHIN_RESPONSE_DUPLICATE_POST'));
  assert.equal(processed.completed.length, 2); // both observations retained for audit
});
test('manual does not filter, malformed or missing daily snapshot cannot run', () => {
  const src = [{ itemIndex: 0, publishedAt: '2026-01-01' }];
  const manual = dailyObservationWindowFilter(src, { triggerSource: 'ADMIN_MANUAL' });
  assert.equal(manual.length, 1);
  assert.equal(manual[0].windowMatchStatus, 'UNSCOPED_MANUAL');
  assert.throws(() => dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED' }), /DISCOVERY_DAILY_FILTER_INVALID/);
  assert.throws(() => dailyObservationWindowFilter(src, { ...daily, dailySearch: { ...daily.dailySearch, slot: 6 } }), /DISCOVERY_DAILY_FILTER_INVALID/);
});
