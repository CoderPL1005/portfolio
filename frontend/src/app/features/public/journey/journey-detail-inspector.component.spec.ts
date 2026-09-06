import { TestBed } from '@angular/core/testing';
import { PublicJourneyItem } from '../shared/public.models';
import { portfolioDurationDays } from '../shared/public-utils';
import { JourneyDetailInspectorComponent } from './journey-detail-inspector.component';

describe('JourneyDetailInspectorComponent', () => {
  function render(item: PublicJourneyItem) {
    const fixture = TestBed.createComponent(JourneyDetailInspectorComponent);
    fixture.componentRef.setInput('item', item);
    fixture.componentRef.setInput('position', { top: 76, left: 12, placement: 'right' });
    fixture.componentRef.setInput('pinned', true);
    fixture.detectChanges();
    return fixture;
  }

  function period(
    title: string,
    startAt: string | null,
    endAt: string | null,
    isOngoing = false,
  ): PublicJourneyItem {
    return {
      id: `${title}-id`, title, subtitle: 'Backend Developer', description: 'Project details.',
      occurredAt: startAt, iconKey: null, sourceType: 'PROJECT', sourceId: `${title}-source`,
      startAt, endAt, isOngoing, timelineKind: 'PERIOD',
    };
  }

  it('shows exact Hotel dates and the exclusive-end 52-day duration', () => {
    const fixture = render(period('Hotel Management System', '2025-04-11', '2025-06-02'));

    expect(fixture.nativeElement.querySelector('.date-range')?.textContent.trim())
      .toBe('Apr 11, 2025 → Jun 2, 2025');
    expect(fixture.nativeElement.querySelector('.duration')?.textContent.trim()).toBe('52 days');
    expect(portfolioDurationDays('2025-04-11', '2025-04-12')).toBe(1);
    expect(portfolioDurationDays('2025-02-30', '2025-03-02')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Backend Developer');
    expect(fixture.nativeElement.textContent).toContain('Period');
  });

  it('shows exact PolicyMeta dates and its 36-day duration', () => {
    const fixture = render(period('PolicyMeta AI', '2026-07-27', '2026-09-01'));

    expect(fixture.nativeElement.querySelector('.date-range')?.textContent.trim())
      .toBe('Jul 27, 2026 → Sep 1, 2026');
    expect(fixture.nativeElement.querySelector('.duration')?.textContent.trim()).toBe('36 days');
  });

  it('does not invent durations for point, ongoing, or undated entries', () => {
    const point = period('Milestone', '2026-08-01', null);
    point.timelineKind = 'POINT';
    let fixture = render(point);
    expect(fixture.nativeElement.querySelector('.date-range')?.textContent.trim()).toBe('Aug 2026');
    expect(fixture.nativeElement.querySelector('.duration')).toBeNull();

    fixture = render(period('SchoolSaaS', '2026-05-01', null, true));
    expect(fixture.nativeElement.querySelector('.date-range')?.textContent.trim())
      .toBe('May 2026 → Present');
    expect(fixture.nativeElement.querySelector('.duration')).toBeNull();

    fixture = render(period('Undated project', null, null));
    expect(fixture.nativeElement.querySelector('.date-range')?.textContent.trim()).toBe('Date not set');
    expect(fixture.nativeElement.querySelector('.duration')).toBeNull();
  });
});
