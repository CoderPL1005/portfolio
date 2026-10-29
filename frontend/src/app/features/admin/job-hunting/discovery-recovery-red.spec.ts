import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { DiscoveryReviewDetail, DiscoveryReviewList } from './discovery-review.models';
import { JobHuntingService } from './job-hunting.service';

// Test-only wire JSON, deliberately not a new production DTO/type. Synthetic only.
// Existing component must branch on origin rather than dereference a fake Item.
const recovered = () => ({
  id: '11111111-1111-1111-1111-111111111111', runId: '22222222-2222-2222-2222-222222222222',
  query: '', queryAvailability: 'UNAVAILABLE', originalQuery: null, originalClassifierResult: null,
  origin: 'PERSISTED_OBSERVATION_RECOVERY', item: null, pageNumber: 1, duplicateCount: 0,
  ingestedAt: '2026-10-29T01:00:00Z', identityWarning: true,
  humanReview: { status: 'UNREVIEWED', version: 0, contentStatus: null, targetStatus: null,
    notes: null, reviewedAt: null, reviewedByAdminId: null },
  recovery: {
    origin: 'PERSISTED_OBSERVATION_RECOVERY', version: 'recovery-review-v1',
    executionId: '22222222-2222-2222-2222-222222222222', observationId: '33333333-3333-3333-3333-333333333333',
    workflowRunId: '10001', operationId: '44444444-4444-4444-4444-444444444444', itemIndex: 0,
    originalQuery: null, originalClassifierResult: null, originalReviewBundle: null,
    rawContent: 'Synthetic Backend Fresher Hanoi source <img src=x onerror=alert(1)>',
    providerPostId: '9001', sourceUrl: null,
    providerSourceUrl: 'https://www.facebook.com/fixture/posts/pfbidSyntheticMismatch',
    publishedAt: null, sourceWarnings: ['POST_ID_URL_MISMATCH'], missingFields: ['ORIGINAL_QUERY', 'ORIGINAL_CLASSIFIER'],
    recoveryDecision: { contentStatus: 'NEEDS_REVIEW', targetStatus: 'INSUFFICIENT_DATA', verificationStatus: 'NOT_CHECKED' },
    recoveryAt: '2026-10-29T01:00:00Z', receiptId: '55555555-5555-5555-5555-555555555555'
  }
});

function mount(detail = true) {
  const row = recovered();
  const api = {
    discovery: vi.fn(() => of({ results: { items: [row], page: 1, pageSize: 20, total: 1, totalPages: 1 },
      counts: { total: 1, valid: 0, invalid: 0, needsReview: 1, targetMatch: 0, identityWarnings: 1, duplicateItems: 0 } } as unknown as DiscoveryReviewList)),
    discoveryItem: vi.fn(() => of(row as unknown as DiscoveryReviewDetail)),
    saveDiscoveryReview: vi.fn(() => of({ status: 'REVIEWED_RECOVERY', version: 1, contentStatus: 'VALID_RECRUITMENT',
      targetStatus: 'NOT_MATCHED', notes: 'Synthetic human reason', reviewedAt: '2026-10-29T01:10:00Z', reviewedByAdminId: 'synthetic-admin' })),
    discoveryHistory: vi.fn(() => of({ items: [], page: 1, pageSize: 20, total: 0, totalPages: 0, current: row.humanReview })),
    discoveryMetrics: vi.fn(() => of({ recoveryCoverage: { total: 1, reviewed: 0, predictionUnavailable: true, precision: null, recall: null },
      counts: { total: 1, reviewed: 0, unreviewed: 1, confirmed: 0, corrected: 0, uncertain: 0 },
      cohorts: [], overallMatricesAvailable: false, notice: 'No original classifier predictions for recovery.' })),
    startDiscovery: vi.fn(() => { throw new Error('TEST_DISPATCH_FORBIDDEN'); })
  };
  const params = convertToParamMap(detail ? { id: row.id } : {});
  const query = new BehaviorSubject(convertToParamMap({ runId: row.runId }));
  TestBed.configureTestingModule({ imports: [DiscoveryReviewPageComponent], providers: [provideRouter([]),
    { provide: JobHuntingService, useValue: api },
    { provide: ActivatedRoute, useValue: { paramMap: of(params), queryParamMap: query, snapshot: { paramMap: params, queryParamMap: query.value } } }
  ] });
  const fixture = TestBed.createComponent(DiscoveryReviewPageComponent);
  // A missing recovery rendering branch must fail an assertion, not leak an
  // unhandled template exception which could be mistaken for runner setup RED.
  expect(() => fixture.detectChanges(), 'Recovery wire payload must render without a fabricated Item').not.toThrow();
  return { fixture, api, row, query, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

afterEach(() => TestBed.resetTestingModule());

describe('T17/T18 recovery-only Admin projection (test-only RED)', () => {
  it('T17 renders recovery detail without an original query or classifier prediction', () => {
    const { el, row, api } = mount();
    expect(el.textContent).toContain('Khôi phục để đánh giá');
    expect(el.textContent).toContain('Không có dữ liệu truy vấn gốc');
    expect(el.textContent).toContain('Không có kết quả phân loại gốc');
    expect(el.textContent).toContain(row.recovery.rawContent);
    expect(el.textContent).toContain('Chưa xác định ngày đăng');
    expect(el.querySelector('img')).toBeNull();
    expect(api.startDiscovery).not.toHaveBeenCalled();
  });

  it('T18 renders warning but never promotes an unverified provider permalink to a clickable link', () => {
    const { el, row } = mount();
    expect(el.textContent).toContain('Nguồn chưa xác minh');
    expect(Array.from(el.querySelectorAll('a')).some(a => a.href === row.recovery.providerSourceUrl)).toBe(false);
    expect(el.querySelectorAll('.source-link')).toHaveLength(0);
  });

  it('T17 offers explicit human labeling, not confirmation/correction of a nonexistent prediction', async () => {
    const { el, fixture, api, row } = mount();
    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('.human-review button'));
    expect(buttons.some(b => /Xác nhận nhận định gốc|Lưu sửa phân loại/.test(b.textContent ?? ''))).toBe(false);
    const reason = el.querySelector<HTMLTextAreaElement>('#human-notes')!;
    expect(reason).not.toBeNull(); reason.value = 'Synthetic human reason'; reason.dispatchEvent(new Event('input'));
    fixture.detectChanges(); await fixture.whenStable();
    const save = buttons.find(b => /Lưu đánh giá/.test(b.textContent ?? ''));
    expect(save, 'Recovery must have an explicit human-label action').toBeDefined(); save!.click(); fixture.detectChanges();
    expect(api.saveDiscoveryReview).toHaveBeenCalledWith(row.id, expect.objectContaining({ action: 'REVIEW_RECOVERY', expectedVersion: 0, notes: 'Synthetic human reason' }));
  });

  it('T17 preserves the draft and never retries when a recovery review conflicts', async () => {
    const { el, fixture, api } = mount();
    api.saveDiscoveryReview.mockReturnValue(throwError(() => ({ status: 409, error: { error: { code: 'DISCOVERY_REVIEW_CONFLICT' } } })));
    const reason = el.querySelector<HTMLTextAreaElement>('#human-notes')!; reason.value = 'Synthetic draft kept'; reason.dispatchEvent(new Event('input'));
    fixture.detectChanges(); await fixture.whenStable();
    const save = Array.from(el.querySelectorAll<HTMLButtonElement>('.human-review button')).find(b => /Lưu đánh giá/.test(b.textContent ?? ''));
    expect(save).toBeDefined(); save!.click(); fixture.detectChanges();
    expect(api.saveDiscoveryReview).toHaveBeenCalledTimes(1); expect(reason.value).toBe('Synthetic draft kept');
    expect(el.textContent).toContain('Draft');
  });

  it('T17 renders list/filter/page changes for the recovery branch without accessing Item', () => {
    const { el, c, api } = mount(false);
    expect(el.querySelectorAll('.job-card')).toHaveLength(1);
    expect(el.textContent).toContain('Khôi phục để đánh giá');
    c.reviewState = 'UNREVIEWED'; c.filter();
    expect(api.discovery).toHaveBeenLastCalledWith(expect.objectContaining({ reviewState: 'UNREVIEWED', page: 1 }));
    expect(api.startDiscovery).not.toHaveBeenCalled();
  });

  it('T17 does not display recovery-only coverage as classifier accuracy', () => {
    const { c, fixture, el, api } = mount(false); c.loadMetrics(); fixture.detectChanges();
    expect(api.discoveryMetrics).toHaveBeenCalledTimes(1);
    expect(el.textContent).toContain('Không có kết quả phân loại gốc');
    expect(el.textContent).not.toMatch(/Precision\s*[:=]?\s*0%|Recall\s*[:=]?\s*0%/i);
  });
});
