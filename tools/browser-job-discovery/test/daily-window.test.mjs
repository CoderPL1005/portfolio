import test from 'node:test';
import assert from 'node:assert/strict';
import { dailyObservationWindowFilter } from '../src/discovery-execution-worker.mjs';

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
  assert.equal(result[1].publishedAt, null);
});
test('yesterday-and-today and today-only windows', () => {
  const src = [
    { itemIndex: 0, publishedAt: '2026-11-01T08:00:00Z' },
    { itemIndex: 1, publishedAt: '2026-11-02T05:00:00Z' },
  ];
  assert.equal(dailyObservationWindowFilter(src, { ...daily, dailySearch: { ...daily.dailySearch, slot: 2, endTime: '2026-11-02' } }).length, 2);
  assert.equal(dailyObservationWindowFilter(src, { ...daily, dailySearch: { ...daily.dailySearch, slot: 4, startTime: '2026-11-02', endTime: '2026-11-02' } }).length, 1);
});
test('manual does not filter, malformed or missing daily snapshot cannot run', () => {
  const src = [{ itemIndex: 0, publishedAt: '2026-01-01' }];
  assert.equal(dailyObservationWindowFilter(src, { triggerSource: 'ADMIN_MANUAL' }).length, 1);
  assert.throws(() => dailyObservationWindowFilter(src, { triggerSource: 'DAILY_SCHEDULED' }), /DISCOVERY_DAILY_FILTER_INVALID/);
  assert.throws(() => dailyObservationWindowFilter(src, { ...daily, dailySearch: { ...daily.dailySearch, slot: 6 } }), /DISCOVERY_DAILY_FILTER_INVALID/);
});
