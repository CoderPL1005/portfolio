import { Component, input, output } from '@angular/core';
import { PublicJourneyItem } from '../shared/public.models';
import { formatPortfolioDate } from '../shared/public-utils';

export type JourneyPopoverPlacement = 'right' | 'left' | 'below' | 'above' | 'mobile';
export interface JourneyPopoverPosition { top: number; left: number; placement: JourneyPopoverPlacement; }

@Component({
  selector: 'app-journey-detail-inspector',
  host: {
    '[style.top.px]': 'position().top',
    '[style.left.px]': 'position().left',
    '[attr.data-placement]': 'position().placement',
    '[attr.data-anchor-id]': 'item().id',
    '[class.mobile-overlay]': 'position().placement === "mobile"',
  },
  template: `
    <aside class="inspector" [class.pinned]="pinned()" [class.preview]="!pinned()" [attr.role]="pinned() ? 'dialog' : 'status'" [attr.aria-modal]="pinned() ? 'true' : null" [attr.aria-label]="'Journey details for ' + item().title">
      <div class="inspector-heading">
        <span class="source-badge">{{ item().sourceType }}</span>
        @if (pinned()) { <button type="button" class="close-button" aria-label="Close journey details" (click)="closed.emit()">Close</button> }
      </div>
      <h2>{{ item().title }}</h2>
      <p class="date-range">{{ range() }}</p>
      @if (item().subtitle) { <p class="subtitle">{{ item().subtitle }}</p> }
      @if (item().description) { <p class="description pre-line">{{ item().description }}</p> }
      <dl>
        <div><dt>Timeline type</dt><dd>{{ item().timelineKind === 'PERIOD' ? 'Period' : 'Point' }}</dd></div>
        <div><dt>Start</dt><dd>{{ date(item().startAt) || 'Not set' }}</dd></div>
        <div><dt>End</dt><dd>{{ item().isOngoing ? 'Present' : (date(item().endAt) || 'Not set') }}</dd></div>
      </dl>
    </aside>
  `,
  styles: `
    :host{position:fixed;z-index:70;width:min(25rem,calc(100vw - 2rem))}
    .inspector{padding:1.25rem;border:1px solid color-mix(in srgb,var(--color-primary) 42%,var(--color-border));border-radius:1rem;background:color-mix(in srgb,var(--color-surface) 96%,var(--color-background));box-shadow:0 1.25rem 4rem rgba(0,0,0,.42);color:var(--color-text)}
    .preview{pointer-events:none;opacity:.96}.preview .description,.preview dl{display:none}.pinned{box-shadow:0 1.5rem 5rem rgba(0,0,0,.58)}
    .inspector-heading{display:flex;align-items:center;justify-content:space-between;gap:1rem}
    .source-badge{color:var(--color-primary);font-size:.68rem;font-weight:800;letter-spacing:.12em}
    .close-button{border:1px solid var(--color-border);border-radius:.55rem;padding:.35rem .6rem;background:transparent;color:var(--color-text-muted);cursor:pointer}.close-button:hover{color:var(--color-text);border-color:var(--color-primary)}
    h2{margin:.65rem 0 .35rem;font-size:clamp(1.25rem,4vw,1.75rem);line-height:1.15;overflow-wrap:anywhere}
    p{margin:.4rem 0}.date-range{color:var(--color-primary);font-size:.82rem;font-weight:750}.subtitle{color:var(--color-text);font-weight:650}.description{margin-top:.9rem;color:var(--color-text-muted);line-height:1.65}
    dl{display:grid;grid-template-columns:repeat(3,1fr);gap:.75rem;margin:1rem 0 0;padding-top:.9rem;border-top:1px solid var(--color-border)}dl div{min-width:0}dt{color:var(--color-text-muted);font-size:.65rem;text-transform:uppercase;letter-spacing:.08em}dd{margin:.25rem 0 0;font-size:.75rem;overflow-wrap:anywhere}
    :host(.mobile-overlay){top:auto!important;left:.75rem!important;right:.75rem;bottom:.75rem;width:auto}
    @media(max-width:900px){.inspector{max-height:min(70vh,34rem);overflow-y:auto}dl{grid-template-columns:1fr 1fr}.preview{display:none}}
  `,
})
export class JourneyDetailInspectorComponent {
  readonly item = input.required<PublicJourneyItem>();
  readonly pinned = input(false);
  readonly position = input.required<JourneyPopoverPosition>();
  readonly closed = output<void>();
  readonly date = formatPortfolioDate;

  range(): string {
    if (!this.item().startAt) return 'Date not set';
    const start = formatPortfolioDate(this.item().startAt) ?? this.item().startAt!;
    if (this.item().timelineKind === 'POINT') return start;
    if (this.item().isOngoing) return `${start} → Present`;
    return `${start} → ${formatPortfolioDate(this.item().endAt) ?? 'End date not set'}`;
  }
}
