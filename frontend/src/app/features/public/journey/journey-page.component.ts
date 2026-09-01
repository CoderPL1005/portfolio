import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { JourneyDetailInspectorComponent, JourneyPopoverPosition } from './journey-detail-inspector.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { PublicJourneyItem } from '../shared/public.models';
import { formatPortfolioDate } from '../shared/public-utils';

type TrackKey = 'EDUCATION' | 'EXPERIENCE' | 'TRAINING' | 'PROJECTS' | 'MILESTONES';

interface PositionedJourneyItem extends PublicJourneyItem {
  left: number;
  width: number;
  lane: number;
  identifier: string;
  titleSide: 'left' | 'right';
  visualStart: number;
  visualEnd: number;
  startPercent: number;
  endPercent: number | null;
  cardLeft: number;
  cardWidth: number;
  cardRight: number;
}

interface TimelineTrack {
  key: TrackKey;
  label: string;
  items: PositionedJourneyItem[];
  laneCount: number;
  height: number;
}

interface AxisTick { label: string; position: number; }

interface TimelineLayout {
  tracks: TimelineTrack[];
  ticks: AxisTick[];
  mobileItems: PublicJourneyItem[];
  hasDatedItems: boolean;
  canvasWidth: number;
  plotWidth: number;
  headerHeight: number;
  bodyOffset: number;
  bottomPadding: number;
  canvasHeight: number;
}

const TIMELINE_HEADER_HEIGHT = 52;
const TIMELINE_BODY_OFFSET = TIMELINE_HEADER_HEIGHT;
const TIMELINE_LANE_HEIGHT = 76;
const TIMELINE_BOTTOM_PADDING = 32;

const TRACKS: ReadonlyArray<{ key: TrackKey; label: string }> = [
  { key: 'EDUCATION', label: 'Education' },
  { key: 'EXPERIENCE', label: 'Experience' },
  { key: 'TRAINING', label: 'Training' },
  { key: 'PROJECTS', label: 'Projects' },
  { key: 'MILESTONES', label: 'Milestones' },
];

const TRACK_PREFIXES: Record<TrackKey, string> = {
  EDUCATION: 'EDU', EXPERIENCE: 'EXP', TRAINING: 'TRN', PROJECTS: 'PRJ', MILESTONES: 'MIL',
};

@Component({
  selector: 'app-journey-page',
  imports: [EmptyStateComponent, JourneyDetailInspectorComponent, LoadingIndicatorComponent],
  template: `
    <div class="public-page journey-page">
      <header class="page-heading journey-heading">
        <span class="eyebrow">Career timeline</span>
        <h1>Journey</h1>
        <p>Education, work, training, projects, and milestones shown in their real temporal context.</p>
      </header>
      @if (store.status() === 'loading' || store.status() === 'idle') {
        <div class="state-panel"><app-loading-indicator label="Loading journey" /></div>
      } @else if (store.status() === 'error') {
        <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div>
      } @else if (!store.data()?.journey?.length) {
        <app-empty-state title="No journey items published" message="Published milestones will appear here." />
      } @else {
        <div class="timeline-scroll" data-horizontal-scroll="true" (scroll)="repositionInspector()">
          <section class="timeline-desktop timeline-canvas" aria-label="Career timeline by track" [attr.data-canvas-height]="layout().canvasHeight" [attr.data-header-height]="layout().headerHeight" [attr.data-body-offset]="layout().bodyOffset" [attr.data-bottom-padding]="layout().bottomPadding" [style.min-height.px]="layout().canvasHeight" [style.min-width.px]="layout().canvasWidth">
            <div class="axis-label timeline-context" aria-hidden="true" [style.height.px]="layout().headerHeight">Track</div>
            <div class="time-axis timeline-context" [class.no-dates]="!layout().hasDatedItems" data-sticky-context="true" [style.height.px]="layout().headerHeight">
              @for (tick of layout().ticks; track tick.label) {
                <span class="axis-tick" [style.left.px]="tick.position * layout().plotWidth / 100">{{ tick.label }}</span>
              }
            </div>
            <div class="first-track-clearance" aria-hidden="true" [style.height.px]="layout().bodyOffset"></div>
            <div class="first-track-clearance" aria-hidden="true" [style.height.px]="layout().bodyOffset"></div>
            @for (track of layout().tracks; track track.key) {
              <h2 class="track-label" [attr.data-first-track]="track.key === 'EDUCATION' ? 'true' : null">{{ track.label }}</h2>
              <div class="track-rail" [attr.data-track]="track.key" [attr.data-first-track]="track.key === 'EDUCATION' ? 'true' : null" [attr.data-plot-width]="layout().plotWidth" [style.height.px]="track.height">
                @for (tick of layout().ticks; track tick.label) { <span class="rail-gridline" aria-hidden="true" [style.left.px]="tick.position * layout().plotWidth / 100"></span> }
                @if (!track.items.length) { <span class="track-empty">No entries</span> }
                @for (item of track.items; track item.id) {
                  <button type="button" class="timeline-item" [class.point]="item.timelineKind === 'POINT'" [class.period]="item.timelineKind === 'PERIOD'" [class.undated]="!item.startAt" [class.unknown-duration]="item.timelineKind === 'PERIOD' && !!item.startAt && item.endPercent === null" [class.active]="activeItem()?.id === item.id" [class.selected]="selectedItem()?.id === item.id" [attr.aria-pressed]="selectedItem()?.id === item.id" [attr.data-entry-id]="item.id" [attr.data-identifier]="item.identifier" [attr.data-kind]="item.timelineKind" [attr.data-lane]="item.lane" [attr.data-visual-start]="item.visualStart" [attr.data-visual-end]="item.visualEnd" [attr.data-start-percent]="item.startPercent" [attr.data-end-percent]="item.endPercent" [attr.data-card-left]="item.cardLeft" [attr.data-card-width]="item.cardWidth" [attr.data-card-right]="item.cardRight" [attr.title]="item.title + ' — ' + range(item)" [attr.aria-label]="entryLabel(item)" [style.left.px]="item.cardLeft" [style.width.px]="item.cardWidth" [style.top.px]="item.lane * 70" (mouseenter)="preview(item, $event.currentTarget)" (mouseleave)="clearPreview(item)" (focus)="preview(item, $event.currentTarget)" (blur)="clearPreview(item)" (click)="openDetails(item, $event.currentTarget)" (keydown.enter)="openDetails(item, $event.currentTarget)" (keydown.space)="openDetails(item, $event.currentTarget); $event.preventDefault()">
                    @if (!item.startAt) {
                      <span class="bar-content"><span class="timeline-title">{{ item.title }}</span><span class="timeline-dates">{{ range(item) }}</span></span>
                    } @else if (item.timelineKind === 'PERIOD') {
                      <span class="period-card-content"><strong>{{ item.title }}</strong><small>{{ range(item) }}</small></span>
                    } @else {
                      <span class="timeline-title-card" [class.title-left]="item.titleSide === 'left'" aria-hidden="true"><strong>{{ item.title }}</strong><small>{{ range(item) }}</small></span>
                    }
                  </button>
                }
              </div>
            }
          </section>
        </div>

        <section class="event-index-panel" aria-label="Journey event index">
          @for (track of layout().tracks; track track.key) {
            @if (track.items.length) {
              <section class="event-index-group" [attr.data-track-index]="track.key">
                <h2>{{ track.label }} index</h2>
                <ol>
                  @for (item of track.items; track item.id) {
                    <li>
                      <button type="button" class="event-index-row" [class.active]="activeItem()?.id === item.id" [class.selected]="selectedItem()?.id === item.id" [attr.aria-pressed]="selectedItem()?.id === item.id" [attr.data-entry-id]="item.id" [attr.data-identifier]="item.identifier" [attr.aria-label]="entryLabel(item)" (mouseenter)="preview(item, $event.currentTarget)" (mouseleave)="clearPreview(item)" (focus)="preview(item, $event.currentTarget)" (blur)="clearPreview(item)" (click)="openDetails(item, $event.currentTarget)" (keydown.enter)="openDetails(item, $event.currentTarget)" (keydown.space)="openDetails(item, $event.currentTarget); $event.preventDefault()">
                        <span class="index-title">{{ item.title }}</span>
                        <time [attr.datetime]="item.startAt">{{ range(item) }}</time>
                      </button>
                    </li>
                  }
                </ol>
              </section>
            }
          }
        </section>

        <ol class="timeline-mobile" aria-label="Chronological career timeline">
          @for (item of layout().mobileItems; track item.id) {
            <li>
              <button type="button" class="mobile-card" [class.selected]="selectedItem()?.id === item.id" [attr.aria-pressed]="selectedItem()?.id === item.id" [attr.aria-label]="entryLabel(item)" (click)="openDetails(item, $event.currentTarget)" (keydown.enter)="openDetails(item, $event.currentTarget)" (keydown.space)="openDetails(item, $event.currentTarget); $event.preventDefault()">
                <span class="mobile-source">{{ trackLabel(item) }}</span>
                <strong>{{ item.title }}</strong>
                <time [attr.datetime]="item.startAt">{{ range(item) }}</time>
                @if (item.subtitle) { <span class="mobile-subtitle">{{ item.subtitle }}</span> }
                @if (item.description) { <span class="mobile-description pre-line">{{ item.description }}</span> }
              </button>
            </li>
          }
        </ol>

        @if (activeItem(); as item) {
          <app-journey-detail-inspector [item]="item" [pinned]="!!selectedItem()" [position]="inspectorPosition()" (closed)="closeDetails()" />
        }
      }
    </div>
  `,
  styles: `
    .journey-page{padding-top:clamp(1.25rem,3vw,2.5rem)}
    .journey-heading{max-width:58rem;margin-bottom:clamp(1.25rem,2.5vw,2rem)}
    .journey-heading h1{margin:.25rem 0 .5rem;font-size:clamp(2.35rem,5vw,4rem)}
    .journey-heading p{margin:0;font-size:clamp(.9rem,1.5vw,1rem);line-height:1.55}
    .timeline-scroll{width:100%;overflow-x:auto;overflow-y:visible;overscroll-behavior-x:contain;scrollbar-gutter:stable}
    .timeline-desktop{display:grid;grid-template-columns:8.5rem minmax(0,1fr);box-sizing:border-box;gap:0 1.25rem;position:relative;padding:0 0 2rem}
    .timeline-context{position:sticky;top:4rem;z-index:12;background:color-mix(in srgb,var(--color-background) 97%,var(--color-surface));box-shadow:0 1px 0 var(--color-border),0 .55rem 1.1rem rgba(0,0,0,.16)}
    .axis-label,.track-label{position:sticky;left:0;background:var(--color-background)}
    .axis-label{top:4rem;z-index:13;display:flex;align-items:center;color:var(--color-text-muted);font-size:.72rem;font-weight:700;text-transform:uppercase;letter-spacing:.08em}
    .time-axis{min-width:0;position:sticky;border-bottom:1px solid var(--color-border)}
    .time-axis.no-dates::after{content:'Dates not set';position:absolute;right:0;bottom:1rem;color:var(--color-text-muted);font-size:.75rem}
    .axis-tick{position:absolute;bottom:1rem;translate:-50% 0;color:var(--color-text-muted);font-size:.72rem;font-weight:750}
    .track-label{z-index:3;align-self:start;margin:0;padding:1.55rem 0;color:var(--color-text);font-size:.78rem;text-transform:uppercase;letter-spacing:.08em}
    .track-rail{position:relative;min-width:0;min-height:76px;border-bottom:1px solid var(--color-border);background:linear-gradient(90deg,color-mix(in srgb,var(--color-surface) 70%,transparent),color-mix(in srgb,var(--color-surface) 22%,transparent))}
    .rail-gridline{position:absolute;inset:0 auto 0 0;border-left:1px dashed color-mix(in srgb,var(--color-border) 65%,transparent);pointer-events:none}
    .track-empty{position:absolute;top:1.5rem;left:0;color:var(--color-text-muted);font-size:.8rem}
    .timeline-item{position:absolute;z-index:1;height:3.25rem;margin-top:.75rem;padding:0;border:1px solid color-mix(in srgb,var(--color-primary) 48%,var(--color-border));border-radius:.55rem;background:color-mix(in srgb,var(--color-primary) 15%,var(--color-surface));box-shadow:0 6px 20px rgba(0,0,0,.13);color:var(--color-text);cursor:pointer;text-align:left;transition:filter .16s ease,transform .16s ease,box-shadow .16s ease}
    .timeline-item::after{content:'';position:absolute;inset:-.45rem -.35rem}
    .timeline-item:hover,.timeline-item:focus-visible,.timeline-item.active{z-index:4;filter:brightness(1.18);transform:translateY(-2px);box-shadow:0 10px 28px rgba(0,0,0,.28)}
    .timeline-item:focus-visible{outline:2px solid var(--color-primary);outline-offset:4px}
    .timeline-item.selected{z-index:5;border-color:var(--color-primary);outline:2px solid color-mix(in srgb,var(--color-primary) 75%,transparent);outline-offset:3px;box-shadow:0 12px 32px color-mix(in srgb,var(--color-primary) 24%,rgba(0,0,0,.35))}
    .bar-content{position:absolute;inset:0;display:grid;align-content:center;padding:.5rem .65rem;overflow:hidden;background:linear-gradient(90deg,color-mix(in srgb,var(--color-primary) 23%,transparent),transparent);border-radius:inherit}
    .timeline-item.point{width:.8rem!important;min-width:.8rem;height:.8rem;margin-top:1.55rem;border:2px solid var(--color-background);border-radius:50%;background:var(--color-primary);box-shadow:0 0 0 2px color-mix(in srgb,var(--color-primary) 55%,transparent)}
    .timeline-item.undated{left:0!important;width:10rem!important;height:3.25rem;margin-top:.75rem;border-style:dashed;border-radius:.55rem;background:var(--color-surface)}
    .timeline-item.undated .bar-content{display:grid}
    .period-card-content{position:absolute;inset:0;display:grid;align-content:center;padding:.35rem .5rem .65rem;overflow:hidden}
    .period-card-content strong,.period-card-content small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.period-card-content strong{font-size:.7rem}.period-card-content small{margin-top:.1rem;color:var(--color-text-muted);font-size:.6rem;font-weight:600}
    .timeline-title-card{position:absolute;top:0;left:0;display:grid;box-sizing:border-box;width:10.5rem;height:2.35rem;padding:.28rem .42rem;border:1px solid color-mix(in srgb,var(--color-primary) 36%,var(--color-border));border-radius:.42rem;background:var(--color-surface);pointer-events:none;text-align:left}
    .timeline-title-card.title-left{right:0;left:auto;text-align:right}.timeline-item.point .timeline-title-card{top:50%;left:calc(100% + .5rem);translate:0 -50%}.timeline-item.point .timeline-title-card.title-left{right:calc(100% + .5rem);left:auto}
    .timeline-title-card strong,.timeline-title-card small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.timeline-title-card strong{font-size:.68rem}.timeline-title-card small{margin-top:.1rem;color:var(--color-text-muted);font-size:.59rem;font-weight:600}
    .timeline-title,.timeline-dates{display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.timeline-title{font-size:.74rem;font-weight:750}.timeline-dates{margin-top:.12rem;color:var(--color-text-muted);font-size:.61rem}
    .event-index-panel{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,22rem),1fr));gap:.85rem;margin-top:1rem}
    .event-index-group{min-width:0;padding:.85rem;border:1px solid var(--color-border);border-radius:.8rem}
    .event-index-group h2{margin:0 0 .55rem;color:var(--color-text-muted);font-size:.7rem;text-transform:uppercase;letter-spacing:.08em}.event-index-group ol{display:grid;gap:.3rem;margin:0;padding:0;list-style:none}
    .event-index-row{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;width:100%;gap:.55rem;padding:.45rem .5rem;border:1px solid transparent;border-radius:.5rem;background:transparent;color:var(--color-text);cursor:pointer;text-align:left}
    .event-index-row:hover,.event-index-row:focus-visible,.event-index-row.active{border-color:color-mix(in srgb,var(--color-primary) 50%,var(--color-border));background:color-mix(in srgb,var(--color-primary) 10%,transparent)}.event-index-row:focus-visible{outline:2px solid var(--color-primary);outline-offset:2px}.event-index-row.selected{border-color:var(--color-primary);background:color-mix(in srgb,var(--color-primary) 16%,transparent)}
    .index-title{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:.78rem;font-weight:650}.event-index-row time{color:var(--color-text-muted);font-size:.68rem;font-weight:600;white-space:nowrap}
    .timeline-mobile{display:none;margin:0;padding:0;list-style:none}.timeline-mobile li{position:relative;padding:0 0 1.25rem 1.4rem;border-left:1px solid var(--color-border)}.timeline-mobile li::before{content:'';position:absolute;left:-.34rem;top:1.2rem;width:.65rem;height:.65rem;border-radius:50%;border:3px solid var(--color-background);background:var(--color-primary)}
    .mobile-card{display:grid;width:100%;gap:.35rem;padding:1rem;border:1px solid var(--color-border);border-radius:.8rem;background:var(--color-surface);color:var(--color-text);cursor:pointer;text-align:left}.mobile-card:hover,.mobile-card:focus-visible,.mobile-card.selected{border-color:var(--color-primary)}.mobile-source{color:var(--color-primary);font-size:.68rem;font-weight:750;text-transform:uppercase;letter-spacing:.08em}.mobile-card strong{font-size:1.1rem}.mobile-card time{color:var(--color-text-muted);font-size:.78rem;font-weight:650}.mobile-subtitle{color:var(--color-text-muted);font-size:.9rem}.mobile-description{margin-top:.35rem;color:var(--color-text-muted);line-height:1.6}
    @media(max-width:900px){.timeline-scroll,.event-index-panel{display:none}.timeline-mobile{display:block}.journey-heading{margin-bottom:1.5rem}}
  `,
})
export class JourneyPageComponent {
  readonly store = inject(PortfolioStore);
  readonly selectedItem = signal<PublicJourneyItem | null>(null);
  readonly hoveredItem = signal<PublicJourneyItem | null>(null);
  readonly activeItem = computed(() => this.selectedItem() ?? this.hoveredItem());
  readonly layout = computed(() => buildTimelineLayout(this.store.data()?.journey ?? []));
  readonly inspectorPosition = signal<JourneyPopoverPosition>({ top: 76, left: 12, placement: 'right' });
  private selectedAnchor: HTMLElement | null = null;
  private hoveredAnchor: HTMLElement | null = null;

  constructor() { this.store.load(); }

  @HostListener('document:keydown.escape')
  closeDetails(): void { this.selectedItem.set(null); this.hoveredItem.set(null); this.selectedAnchor = null; this.hoveredAnchor = null; }

  @HostListener('window:resize')
  @HostListener('window:scroll')
  repositionInspector(): void {
    const anchor = this.selectedAnchor ?? this.hoveredAnchor;
    if (!this.activeItem() || !anchor) return;
    if (window.innerWidth <= 900) {
      this.inspectorPosition.set({ top: 0, left: 0, placement: 'mobile' });
      return;
    }
    this.inspectorPosition.set(calculatePopoverPosition(
      anchor.getBoundingClientRect(), window.innerWidth, window.innerHeight));
  }

  openDetails(item: PublicJourneyItem, target: EventTarget | null = null): void {
    this.hoveredItem.set(null);
    this.hoveredAnchor = null;
    this.selectedAnchor = target instanceof HTMLElement ? target : null;
    this.selectedItem.set(item);
    this.repositionInspector();
  }

  preview(item: PublicJourneyItem, target: EventTarget | null = null): void {
    if (this.selectedItem()) return;
    this.hoveredAnchor = target instanceof HTMLElement ? target : null;
    this.hoveredItem.set(item);
    this.repositionInspector();
  }

  clearPreview(item: PublicJourneyItem): void {
    if (this.hoveredItem()?.id !== item.id) return;
    this.hoveredItem.set(null);
    this.hoveredAnchor = null;
  }

  range(item: PublicJourneyItem): string {
    if (!item.startAt) return 'Date not set';
    const start = formatPortfolioDate(item.startAt) ?? item.startAt;
    if (item.timelineKind === 'POINT') return start;
    if (item.isOngoing) return `${start} → Present`;
    return `${start} → ${formatPortfolioDate(item.endAt) ?? 'End date not set'}`;
  }

  trackLabel(item: PublicJourneyItem): string { return TRACKS.find(track => track.key === trackKey(item))?.label ?? 'Milestones'; }
  entryLabel(item: PublicJourneyItem): string { return `View details for ${item.title}, ${this.trackLabel(item)}, ${this.range(item)}`; }
}

export function buildTimelineLayout(items: PublicJourneyItem[], today = new Date()): TimelineLayout {
  const dated = items.filter(item => item.startAt);
  const starts = dated.map(item => dateValue(item.startAt!));
  const ends = dated.map(item => item.isOngoing ? startOfToday(today) : dateValue(item.endAt ?? item.startAt!));
  const minimum = starts.length ? Math.min(...starts) : 0;
  const maximum = ends.length ? Math.max(...starts, ...ends) : minimum;
  const span = Math.max(1, maximum - minimum);
  const ticks = axisTicks(minimum, maximum);
  const plotWidth = Math.max(704, ticks.length * 180);
  const fallbackCardWidth = 168;
  const trailingSpace = fallbackCardWidth + 16;
  const canvasWidth = 156 + plotWidth + trailingSpace;
  const pointWidth = 13;
  const pointGap = 8;
  const collisionGap = 8;
  const tracks = TRACKS.map(definition => {
    const sourceItems = items.filter(item => trackKey(item) === definition.key);
    const laneFootprints: Array<Array<{ start: number; end: number }>> = [];
    const positioned = sourceItems.map((item, index) => {
      const identifier = `${TRACK_PREFIXES[definition.key]}${index + 1}`;
      if (!item.startAt) {
        const footprint = { start: 0, end: fallbackCardWidth };
        const lane = findLane(laneFootprints, footprint, collisionGap);
        reserveLane(laneFootprints, lane, footprint);
        return { ...item, left: 0, width: 0, lane, identifier, titleSide: 'right' as const, visualStart: 0, visualEnd: fallbackCardWidth, startPercent: 0, endPercent: null, cardLeft: 0, cardWidth: fallbackCardWidth, cardRight: fallbackCardWidth };
      }
      const start = dateValue(item.startAt);
      const end = item.isOngoing ? startOfToday(today) : dateValue(item.endAt ?? item.startAt);
      const left = ((start - minimum) / span) * 100;
      const width = item.timelineKind === 'POINT' ? 0 : ((Math.max(start, end) - start) / span) * 100;
      const boundedLeft = Math.min(100, left);
      const boundedWidth = Math.min(100 - left, width);
      const temporalX = (boundedLeft / 100) * plotWidth;
      const temporalEndX = ((boundedLeft + boundedWidth) / 100) * plotWidth;
      const hasKnownPeriodEnd = item.timelineKind === 'PERIOD' && (item.isOngoing || !!item.endAt);
      const periodCardWidth = hasKnownPeriodEnd ? temporalEndX - temporalX : pointWidth;
      const integrated = item.timelineKind === 'PERIOD'
        ? { start: temporalX, end: temporalX + periodCardWidth }
        : { start: temporalX, end: temporalX + pointWidth };
      const right = { start: temporalX, end: temporalX + pointWidth + pointGap + fallbackCardWidth };
      const leftSide = { start: temporalX - pointGap - fallbackCardWidth, end: temporalX + pointWidth };
      let titleSide: 'left' | 'right' = 'right';
      let footprint = integrated;
      let lane: number;
      if (item.timelineKind === 'PERIOD') {
        lane = findLane(laneFootprints, integrated, collisionGap);
      } else {
        const rightLane = findExistingLane(laneFootprints, right, collisionGap);
        const leftLane = leftSide.start >= 0 ? findExistingLane(laneFootprints, leftSide, collisionGap) : -1;
        if (rightLane >= 0) {
          lane = rightLane; footprint = right;
        } else if (leftLane >= 0) {
          lane = leftLane; footprint = leftSide; titleSide = 'left';
        } else {
          lane = laneFootprints.length;
          if (right.end <= plotWidth + trailingSpace || leftSide.start < 0) footprint = right;
          else { footprint = leftSide; titleSide = 'left'; }
        }
      }
      reserveLane(laneFootprints, lane, footprint);
      return {
        ...item,
        left: boundedLeft,
        width: boundedWidth,
        lane,
        identifier,
        titleSide,
        visualStart: footprint.start,
        visualEnd: footprint.end,
        startPercent: boundedLeft,
        endPercent: hasKnownPeriodEnd ? boundedLeft + boundedWidth : null,
        cardLeft: temporalX,
        cardWidth: item.timelineKind === 'PERIOD' ? periodCardWidth : 0,
        cardRight: item.timelineKind === 'PERIOD' ? temporalX + periodCardWidth : temporalX,
      };
    });
    const laneCount = Math.max(1, laneFootprints.length);
    return { ...definition, items: positioned, laneCount, height: laneCount * TIMELINE_LANE_HEIGHT };
  });
  const canvasHeight = TIMELINE_HEADER_HEIGHT
    + TIMELINE_BODY_OFFSET
    + tracks.reduce((height, track) => height + track.height, 0)
    + TIMELINE_BOTTOM_PADDING;
  return {
    tracks,
    ticks,
    mobileItems: [...items].sort(compareChronologically),
    hasDatedItems: dated.length > 0,
    canvasWidth,
    plotWidth,
    headerHeight: TIMELINE_HEADER_HEIGHT,
    bodyOffset: TIMELINE_BODY_OFFSET,
    bottomPadding: TIMELINE_BOTTOM_PADDING,
    canvasHeight,
  };
}

function findLane(lanes: Array<Array<{ start: number; end: number }>>, footprint: { start: number; end: number }, gap: number): number {
  const existing = findExistingLane(lanes, footprint, gap);
  return existing >= 0 ? existing : lanes.length;
}

function findExistingLane(lanes: Array<Array<{ start: number; end: number }>>, footprint: { start: number; end: number }, gap: number): number {
  return lanes.findIndex(entries => entries.every(entry => footprint.end + gap <= entry.start || footprint.start >= entry.end + gap));
}

function reserveLane(lanes: Array<Array<{ start: number; end: number }>>, lane: number, footprint: { start: number; end: number }): void {
  (lanes[lane] ??= []).push(footprint);
}

function trackKey(item: PublicJourneyItem): TrackKey {
  if (item.sourceType === 'PROJECT') return 'PROJECTS';
  if (item.sourceType === 'MANUAL' || item.sourceType === 'CERTIFICATE') return 'MILESTONES';
  return item.sourceType;
}

function axisTicks(minimum: number, maximum: number): AxisTick[] {
  if (!minimum) return [];
  const span = Math.max(1, maximum - minimum);
  const firstYear = new Date(minimum).getUTCFullYear();
  const lastYear = new Date(maximum).getUTCFullYear();
  const ticks: AxisTick[] = [];
  for (let year = firstYear; year <= lastYear; year++) {
    const value = year === firstYear ? minimum : Date.UTC(year, 0, 1);
    ticks.push({ label: String(year), position: ((value - minimum) / span) * 100 });
  }
  return ticks;
}

function compareChronologically(left: PublicJourneyItem, right: PublicJourneyItem): number {
  if (!left.startAt && right.startAt) return -1;
  if (left.startAt && !right.startAt) return 1;
  const dateDifference = dateValue(left.startAt) - dateValue(right.startAt);
  if (dateDifference) return dateDifference;
  const trackDifference = TRACKS.findIndex(track => track.key === trackKey(left)) - TRACKS.findIndex(track => track.key === trackKey(right));
  return trackDifference || left.sourceId.localeCompare(right.sourceId);
}

function dateValue(value: string | null): number { return value ? Date.parse(`${value}T00:00:00Z`) : 0; }
function startOfToday(value: Date): number { return Date.UTC(value.getFullYear(), value.getMonth(), value.getDate()); }

export function calculatePopoverPosition(
  anchor: Pick<DOMRect, 'top' | 'right' | 'bottom' | 'left' | 'width' | 'height'>,
  viewportWidth: number,
  viewportHeight: number,
  popoverWidth = 400,
  popoverHeight = 320): JourneyPopoverPosition {
  const gap = 12;
  const margin = 12;
  const stickyTop = 76;
  const top = clamp(anchor.top + anchor.height / 2 - popoverHeight / 2,
    stickyTop, Math.max(stickyTop, viewportHeight - popoverHeight - margin));

  if (viewportWidth - anchor.right - gap >= popoverWidth)
    return { top, left: anchor.right + gap, placement: 'right' };
  if (anchor.left - gap >= popoverWidth)
    return { top, left: anchor.left - popoverWidth - gap, placement: 'left' };

  const left = clamp(anchor.left + anchor.width / 2 - popoverWidth / 2,
    margin, Math.max(margin, viewportWidth - popoverWidth - margin));
  if (viewportHeight - anchor.bottom - gap >= popoverHeight)
    return { top: anchor.bottom + gap, left, placement: 'below' };
  if (anchor.top - stickyTop - gap >= popoverHeight)
    return { top: anchor.top - popoverHeight - gap, left, placement: 'above' };

  return { top, left, placement: anchor.left > viewportWidth / 2 ? 'left' : 'right' };
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
