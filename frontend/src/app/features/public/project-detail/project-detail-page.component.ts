import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, filter, map, switchMap } from 'rxjs';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PublicImageComponent } from '../../../shared/components/public-image/public-image.component';
import { PublicProjectDetail } from '../shared/public.models';
import { PublicPortfolioService } from '../shared/public-portfolio.service';
import { formatPortfolioDate, safeHttpUrl } from '../shared/public-utils';
import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-project-detail-page', imports: [RouterLink, LoadingIndicatorComponent, PublicImageComponent],
  template: `<div class="public-page">
    @if (status() === 'loading') { <div class="state-panel"><app-loading-indicator label="Loading project" /></div> }
    @else if (status() === 'not-found') { <section class="state-panel"><div><p class="eyebrow">404</p><h1>Project not found</h1><p>This project is unavailable or no longer published.</p><a class="secondary-button" routerLink="/projects">Back to projects</a></div></section> }
    @else if (status() === 'error') { <div class="state-panel" role="alert"><p>{{ error() }}</p><button class="retry-button" type="button" (click)="retry()">Try again</button></div> }
    @else if (project(); as item) {
      <header class="detail-heading"><a class="text-link" routerLink="/projects">&larr; Projects</a><p class="eyebrow">{{ item.status }}</p><h1>{{ item.title }}</h1>@if (item.subtitle || item.shortDescription) { <p class="lead">{{ item.subtitle || item.shortDescription }}</p> }
        <div class="facts">@if (item.role) { <span>Role: {{ item.role }}</span> }@if (item.teamSize !== null) { <span>Team: {{ item.teamSize }}</span> }@if (date(item.startDate); as start) { <span>{{ start }}@if (date(item.endDate); as end) { &ndash; {{ end }} }</span> }</div>
        <div class="actions">@if (http(item.githubUrl); as github) { <a class="secondary-button" [href]="github" target="_blank" rel="noopener noreferrer">Repository</a> }@if (http(item.liveUrl); as live) { <a class="primary-button" [href]="live" target="_blank" rel="noopener noreferrer">Live project</a> }</div>
      </header>
      @if (item.thumbnailUrl) { <div class="cover"><app-public-image [src]="item.thumbnailUrl" [alt]="item.title" [fallbackLabel]="item.title.slice(0, 1)" /></div> }
      @if (item.overviewMarkdown) { <section class="content-section"><h2>Overview</h2><p class="pre-line">{{ item.overviewMarkdown }}</p></section> }
      @if (item.technologies.length) { <section class="content-section"><h2>Technologies</h2><ul class="chip-list">@for (technology of item.technologies; track technology.id) { <li>{{ technology.name }}</li> }</ul></section> }
      @for (section of item.sections; track section.id) { <section class="content-section">@if (section.title) { <h2>{{ section.title }}</h2> }@if (section.subtitle) { <h3>{{ section.subtitle }}</h3> }@if (section.contentMarkdown) { <p class="pre-line">{{ section.contentMarkdown }}</p> } @else if (contentText(section.content); as text) { <p class="pre-line">{{ text }}</p> }</section> }
      @if (item.media.length) { <section class="content-section"><h2>Project media</h2><div class="media-grid">@for (media of item.media; track media.id) { <figure><div><app-public-image [src]="media.url" [alt]="media.altText || item.title" [fallbackLabel]="item.title.slice(0, 1)" /></div>@if (media.caption) { <figcaption>{{ media.caption }}</figcaption> }</figure> }</div></section> }
    }
  </div>`,
  styles: `
    .detail-heading { max-width: 58rem; padding: 2rem 0 4rem; } .detail-heading h1 { margin: .6rem 0; font-size: clamp(3rem, 9vw, 7rem); line-height: .95; overflow-wrap: anywhere; } .lead { color: var(--color-text-muted); font-size: 1.2rem; }
    .facts, .actions { display: flex; flex-wrap: wrap; gap: .75rem 1.5rem; margin-top: 1.5rem; } .facts { color: var(--color-text-muted); }
    .cover { height: min(65vw, 38rem); overflow: hidden; border-radius: var(--radius-lg); }
    .content-section { max-width: 54rem; padding: 3rem 0; border-bottom: 1px solid var(--color-border); } .content-section p { color: var(--color-text-muted); line-height: 1.8; }
    .media-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); gap: 1rem; } figure { margin: 0; } figure > div { height: 18rem; overflow: hidden; border-radius: var(--radius-lg); } figcaption { margin-top: .5rem; color: var(--color-text-muted); }
  `,
})
export class ProjectDetailPageComponent {
  private readonly api = inject(PublicPortfolioService); private readonly route = inject(ActivatedRoute); private readonly destroyRef = inject(DestroyRef);
  readonly project = signal<PublicProjectDetail | null>(null); readonly status = signal<'loading'|'loaded'|'error'|'not-found'>('loading'); readonly error = signal<string | null>(null); private slug = '';
  readonly http = safeHttpUrl; readonly date = formatPortfolioDate; readonly contentText = contentToText;
  constructor() { this.route.paramMap.pipe(map(p => p.get('slug') ?? ''), filter(Boolean), distinctUntilChanged(), switchMap(slug => { this.slug = slug; this.status.set('loading'); this.error.set(null); return this.api.getProject(slug); }), takeUntilDestroyed(this.destroyRef)).subscribe({ next: item => { this.project.set(item); this.status.set('loaded'); }, error: (e: unknown) => { if (e instanceof ApiHttpError && (e.status === 404 || e.apiError.code === 'PROJECT_NOT_FOUND')) this.status.set('not-found'); else { this.error.set(e instanceof ApiHttpError ? e.apiError.message : 'Unable to load this project.'); this.status.set('error'); } } }); }
  retry(): void { if (!this.slug) return; this.status.set('loading'); this.api.getProject(this.slug).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: item => { this.project.set(item); this.status.set('loaded'); }, error: () => { this.error.set('Unable to load this project.'); this.status.set('error'); } }); }
}

function contentToText(value: unknown): string | null {
  if (typeof value === 'string') return value;
  if (value === null || value === undefined) return null;
  if (Array.isArray(value)) { const parts = value.map(contentToText).filter((part): part is string => !!part); return parts.length ? parts.join('\n') : null; }
  if (typeof value === 'object') { const parts = Object.values(value).map(contentToText).filter((part): part is string => !!part); return parts.length ? parts.join('\n') : null; }
  return typeof value === 'number' || typeof value === 'boolean' ? String(value) : null;
}
