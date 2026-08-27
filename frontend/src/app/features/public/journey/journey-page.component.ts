import { Component, inject } from '@angular/core';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { formatPortfolioDate } from '../shared/public-utils';

@Component({ selector: 'app-journey-page', imports: [EmptyStateComponent, LoadingIndicatorComponent], template: `
  <div class="public-page"><header class="page-heading"><span class="eyebrow">Milestones</span><h1>Journey</h1><p>A timeline of published moments along the way.</p></header>
  @if (store.status() === 'loading' || store.status() === 'idle') { <div class="state-panel"><app-loading-indicator label="Loading journey" /></div> }
  @else if (store.status() === 'error') { <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div> }
  @else if (!store.data()?.journey?.length) { <app-empty-state title="No journey items published" message="Published milestones will appear here." /> }
  @else { <ol class="timeline">@for (item of store.data()!.journey; track item.id) { <li><article>@if (date(item.occurredAt); as occurred) { <time [attr.datetime]="item.occurredAt">{{ occurred }}</time> }<h2>{{ item.title }}</h2>@if (item.subtitle) { <h3>{{ item.subtitle }}</h3> }@if (item.description) { <p class="pre-line">{{ item.description }}</p> }</article></li> }</ol> }
  </div>`, styles: `.timeline { max-width: 55rem; margin: 0; padding: 0; list-style: none; border-left: 1px solid var(--color-border); } li { position: relative; padding: 0 0 3.5rem 2.25rem; } li::before { content: ''; position: absolute; left: -.42rem; top: .2rem; width: .8rem; height: .8rem; border-radius: 50%; border: 3px solid var(--color-background); background: var(--color-primary); } time { color: var(--color-primary); font-size: .78rem; font-weight: 700; } h2 { margin: .4rem 0; font-size: 1.6rem; } h3 { color: var(--color-text-muted); font-size: 1rem; } p { color: var(--color-text-muted); line-height: 1.75; }` })
export class JourneyPageComponent { readonly store = inject(PortfolioStore); readonly date = formatPortfolioDate; constructor() { this.store.load(); } }
