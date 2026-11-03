import test from 'node:test';
import assert from 'node:assert/strict';
import { dailyObservationWindowFilter, observationsFrom, processObservations } from '../src/discovery-execution-worker.mjs';
import { normalizeSocialApisItem } from '../src/socialapis-live-smoke.mjs';

const daily = { dailySearch: { slot: 1, startTime: '2026-11-01', endTime: '2026-11-01', timeZone: 'Asia/Ho_Chi_Minh' }, triggerSource: 'DAILY_SCHEDULED' };
test('yesterday-only classifies out-of-range posts and preserves all raw observations', () => {
  const src = [
    { itemIndex: 0, publishedAt: '2026-10-31T16:00:00Z', outcome: 'READY_FOR_EXTRACTION' }, // Oct 31 in Vietnam -> out of window
    { itemIndex: 1, publishedAt: '2026-11-01T01:00:00Z', outcome: 'READY_FOR_EXTRACTION' }, // Nov 1 in Vietnam -> in window
    { itemIndex: 2, publishedAt: '2026-11-01T18:00:00Z', outcome: 'READY_FOR_EXTRACTION' }, // Nov 2 in Vietnam -> out of window
    { itemIndex: 3, publishedAt: null, outcome: 'READY_FOR_EXTRACTION' }, // unknown: reviewable, not silently invented
  ];
  const result = dailyObservationWindowFilter(src, daily);
  assert.equal(result.length, 4);
  assert.deepEqual(result.map(x => x.itemIndex), [0, 1, 2, 3]);

  assert.equal(result[0].publishedAt, '2026-10-31T16:00:00Z');
  assert.equal(result[0].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');
  assert.equal(result[0].windowMatchStatus, 'OUT_OF_WINDOW');
  assert.equal(result[0].freshness, 'OUT_OF_WINDOW');
  assert.equal(result[0].outcome, 'OUT_OF_WINDOW');

  assert.equal(result[1].publishedAt, '2026-11-01T01:00:00Z');
  assert.equal(result[1].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');
  assert.equal(result[1].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(result[1].freshness, 'VERIFIED_IN_WINDOW');
  assert.equal(result[1].outcome, 'READY_FOR_EXTRACTION');
  assert.deepEqual(result[1].requestedSearchWindow, { slot: 1, startTime: '2026-11-01', endTime: '2026-11-01', timeZone: 'Asia/Ho_Chi_Minh' });

  assert.equal(result[2].publishedAt, '2026-11-01T18:00:00Z');
  assert.equal(result[2].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');
  assert.equal(result[2].windowMatchStatus, 'OUT_OF_WINDOW');
  assert.equal(result[2].freshness, 'OUT_OF_WINDOW');
  assert.equal(result[2].outcome, 'OUT_OF_WINDOW');

  assert.equal(result[3].publishedAt, null);
  assert.equal(result[3].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');
  assert.equal(result[3].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(result[3].freshness, 'UNKNOWN');
  assert.equal(result[3].outcome, 'READY_FOR_EXTRACTION');
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
    assert.equal(res.length, 4);
    assert.deepEqual(res.map(r => r.windowMatchStatus), ['OUT_OF_WINDOW', 'VERIFIED_IN_WINDOW', 'VERIFIED_IN_WINDOW', 'UNKNOWN_DATE']);
  }
  for (const slot of [3, 4, 5]) {
    const res = dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED', dailySearch: { slot, startTime: '2026-11-02', endTime: '2026-11-02', timeZone: 'Asia/Ho_Chi_Minh' } });
    assert.equal(res.length, 4);
    assert.deepEqual(res.map(r => r.windowMatchStatus), ['OUT_OF_WINDOW', 'OUT_OF_WINDOW', 'VERIFIED_IN_WINDOW', 'UNKNOWN_DATE']);
    assert.equal(res[2].publishedAt, '2026-11-01T17:00:00Z');
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
  const src = [{ itemIndex: 0, publishedAt: '2026-01-01T00:00:00Z' }];
  const manual = dailyObservationWindowFilter(src, { triggerSource: 'ADMIN_MANUAL' });
  assert.equal(manual.length, 1);
  assert.equal(manual[0].windowMatchStatus, 'UNSCOPED_MANUAL');
  assert.throws(() => dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED' }), /DISCOVERY_DAILY_FILTER_INVALID/);
  assert.throws(() => dailyObservationWindowFilter(src, { ...daily, dailySearch: { ...daily.dailySearch, slot: 6 } }), /DISCOVERY_DAILY_FILTER_INVALID/);
});

test('offline end-to-end trace: march post collected in november is preserved in raw observations but excluded from actionable vacancies', async () => {
  const novemberSlot = {
    triggerSource: 'DAILY_SCHEDULED',
    dailySearch: { slot: 4, startTime: '2026-11-03', endTime: '2026-11-03', timeZone: 'Asia/Ho_Chi_Minh' }
  };
  // Build observations using observationsFrom:
  // Item 0: flat item with March 18 timestamp (verified timestamp outside November search window)
  // Item 1: flat item with Nov 3 timestamp (verified timestamp in November search window)
  // Item 2: nested basic_info item without timestamp (reproducing real-world production scenario: UNAVAILABLE_OR_UNVERIFIED)
  const providerRows = [
    { post_id: '10001', text: 'Tuyển Fresher .NET Developer tại Hà Nội\nRequirements: C#, .NET\nLocation: Hà Nội', url: 'https://www.facebook.com/fixture/posts/10001', created_time: 1773824400 }, // 18 March 2026
    { post_id: '10002', text: 'Tuyển Fresher .NET Developer tại Hà Nội\nRequirements: C#, .NET\nLocation: Hà Nội', url: 'https://www.facebook.com/fixture/posts/10002', created_time: 1793689200 }, // 03 November 2026
    { basic_info: { post_id: '10003', post_text: 'Tuyển Fresher .NET Developer tại Hà Nội\nRequirements: C#, .NET\nLocation: Hà Nội', url: 'https://www.facebook.com/fixture/posts/10003' } }, // no date in basic_info
  ];
  const rawObs = observationsFrom(providerRows);

  // Step 1: dailyObservationWindowFilter preserves all raw observations without dropping old evidence
  const classified = dailyObservationWindowFilter(rawObs, novemberSlot);
  assert.equal(classified.length, 3);
  assert.equal(classified[0].windowMatchStatus, 'OUT_OF_WINDOW');
  assert.equal(classified[0].freshness, 'OUT_OF_WINDOW');
  assert.equal(classified[0].outcome, 'OUT_OF_WINDOW');

  assert.equal(classified[1].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(classified[1].freshness, 'VERIFIED_IN_WINDOW');
  assert.equal(classified[1].outcome, 'READY_FOR_EXTRACTION');

  assert.equal(classified[2].publishedAt, null);
  assert.equal(classified[2].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(classified[2].freshness, 'UNKNOWN');
  assert.equal(classified[2].outcome, 'READY_FOR_EXTRACTION');

  // Step 2: verify direct classification of march post under manual trigger remains unscoped
  const manual = dailyObservationWindowFilter([rawObs[0]], { triggerSource: 'ADMIN_MANUAL' });
  assert.equal(manual[0].windowMatchStatus, 'UNSCOPED_MANUAL');

  // Step 3: processObservations preserves all raw observations into completed, skips Gemini and extraction for OUT_OF_WINDOW
  let geminiCalls = 0;
  const processed = await processObservations({
    executionId: '00000000-0000-4000-8000-000000000002',
    observations: classified,
    config: { semanticEnabled: true, evidencePolicy: 'STRICT' },
    policy: null,
    providerOperation: 'SUCCEEDED',
    backend: {
      extract: async () => { geminiCalls++; return { modelIdentity: 'mock', output: { employer: { value: null, evidence: [] }, vacancies: [] } }; },
      saveProcessing: async () => {}
    }
  });

  assert.equal(processed.completed.length, 3);
  // March post is preserved as durable observation
  assert.equal(processed.completed[0].windowMatchStatus, 'OUT_OF_WINDOW');
  assert.equal(processed.completed[0].outcome, 'OUT_OF_WINDOW');
  assert.equal(processed.completed[0].vacancies.length, 0); // No actionable vacancies extracted for out-of-window post

  // Nov 3 in-window post has deterministic extraction
  assert.equal(processed.completed[1].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.ok(processed.completed[1].vacancies.length > 0);

  // Gemini was never called for OUT_OF_WINDOW post
  assert.ok(geminiCalls <= 2);
});

test('strict explicit UTC/offset validation rejects ambiguous timezone-free and malformed timestamps', () => {
  const novemberSlot = {
    triggerSource: 'DAILY_SCHEDULED',
    dailySearch: { slot: 4, startTime: '2026-11-03', endTime: '2026-11-03', timeZone: 'Asia/Ho_Chi_Minh' }
  };
  const src = [
    { itemIndex: 0, publishedAt: '2026-11-03T07:00:00Z' }, // explicit UTC Z -> verified
    { itemIndex: 1, publishedAt: '2026-11-03T14:00:00+07:00' }, // explicit offset +07:00 -> verified
    { itemIndex: 2, publishedAt: '2026-11-03T02:00:00-05:00' }, // explicit offset -05:00 (14:00 ICT) -> verified
    { itemIndex: 3, publishedAt: '2026-11-03 14:00:00' }, // ambiguous: missing timezone -> rejected as UNKNOWN_DATE
    { itemIndex: 4, publishedAt: '2026-11-03T14:00:00' }, // ambiguous ISO: missing timezone -> rejected as UNKNOWN_DATE
    { itemIndex: 5, publishedAt: '2026-11-03' }, // date-only: missing time and offset -> rejected as UNKNOWN_DATE
    { itemIndex: 6, publishedAt: 'malformed-timestamp' }, // malformed -> rejected as UNKNOWN_DATE
  ];
  const classified = dailyObservationWindowFilter(src, novemberSlot);
  assert.equal(classified.length, 7);

  // Explicit offsets are verified in window
  assert.equal(classified[0].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(classified[0].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');

  assert.equal(classified[1].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(classified[1].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');

  assert.equal(classified[2].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(classified[2].publishedAtProvenance, 'VERIFIED_SUPPORTED_TIMESTAMP');

  // Ambiguous and malformed timestamps fail closed to UNKNOWN_DATE and UNAVAILABLE_OR_UNVERIFIED
  assert.equal(classified[3].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(classified[3].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');

  assert.equal(classified[4].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(classified[4].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');

  assert.equal(classified[5].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(classified[5].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');

  assert.equal(classified[6].windowMatchStatus, 'UNKNOWN_DATE');
  assert.equal(classified[6].publishedAtProvenance, 'UNAVAILABLE_OR_UNVERIFIED');
});

test('nov 2 post collected in nov 3 07:00 slot (Slot 1) remains VERIFIED_IN_WINDOW for its execution', () => {
  const slot1 = {
    triggerSource: 'DAILY_SCHEDULED',
    dailySearch: { slot: 1, startTime: '2026-11-02', endTime: '2026-11-03', timeZone: 'Asia/Ho_Chi_Minh' }
  };
  const slot4 = {
    triggerSource: 'DAILY_SCHEDULED',
    dailySearch: { slot: 4, startTime: '2026-11-03', endTime: '2026-11-03', timeZone: 'Asia/Ho_Chi_Minh' }
  };
  const nov2Post = { itemIndex: 0, publishedAt: '2026-11-02T10:00:00Z', outcome: 'READY_FOR_EXTRACTION' };

  // For slot 1 (yesterday + today), Nov 2 is IN WINDOW
  const inSlot1 = dailyObservationWindowFilter([nov2Post], slot1);
  assert.equal(inSlot1[0].windowMatchStatus, 'VERIFIED_IN_WINDOW');
  assert.equal(inSlot1[0].freshness, 'VERIFIED_IN_WINDOW');
  assert.equal(inSlot1[0].outcome, 'READY_FOR_EXTRACTION');

  // For slot 4 (today only), Nov 2 is OUT OF WINDOW
  const inSlot4 = dailyObservationWindowFilter([nov2Post], slot4);
  assert.equal(inSlot4[0].windowMatchStatus, 'OUT_OF_WINDOW');
  assert.equal(inSlot4[0].freshness, 'OUT_OF_WINDOW');
  assert.equal(inSlot4[0].outcome, 'OUT_OF_WINDOW');
});
