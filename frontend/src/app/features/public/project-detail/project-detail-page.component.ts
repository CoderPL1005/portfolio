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
      <header class="detail-heading"><div class="breadcrumbs"><a routerLink="/projects">&larr; Projects</a><span>/</span><b>{{ item.title }}</b></div><div class="title-row"><div><p class="eyebrow">{{ item.status }}</p><h1>{{ item.title }}</h1>@if (item.subtitle || item.shortDescription) { <p class="lead">{{ item.subtitle || item.shortDescription }}</p> }</div><div class="actions">@if (http(item.liveUrl); as live) { <a class="primary-button" [href]="live" target="_blank" rel="noopener noreferrer">Live project</a> }@if (http(item.githubUrl); as github) { <a class="secondary-button" [href]="github" target="_blank" rel="noopener noreferrer">Source</a> }</div></div>
        <dl class="facts">@if (item.role) { <div><dt>Role</dt><dd>{{ item.role }}</dd></div> }@if (date(item.startDate); as start) { <div><dt>Timeline</dt><dd>{{ start }}@if (date(item.endDate); as end) { &ndash; {{ end }} }</dd></div> }@if (item.teamSize !== null) { <div><dt>Team</dt><dd>{{ item.teamSize }}</dd></div> }<div><dt>Status</dt><dd>{{ item.status }}</dd></div></dl>
      </header>
      @if (item.thumbnailUrl) { <div class="cover"><app-public-image [src]="item.thumbnailUrl" [alt]="item.title" [fallbackLabel]="item.title.slice(0, 1)" /></div> }
      <div class="overview-grid">@if (item.overviewMarkdown) { <section class="content-section overview"><p class="section-label">Platform overview</p><h2>Context &amp; engineering approach</h2><p class="pre-line">{{ item.overviewMarkdown }}</p></section> }@if (item.technologies.length) { <aside class="technology-panel"><p class="section-label">Technical environment</p><h2>Technologies</h2><ul class="chip-list">@for (technology of item.technologies; track technology.id) { <li>{{ technology.name }}</li> }</ul></aside> }</div>
      @if (item.sections.length) { <section class="engineering"><div class="section-heading"><div><span class="eyebrow">Case study</span><h2>Engineering focus</h2></div></div><div class="section-grid">@for (section of item.sections; track section.id) { <article class="content-section"><span class="section-index">0{{ $index + 1 }}</span>@if (section.title) { <h3>{{ section.title }}</h3> }@if (section.subtitle) { <h4>{{ section.subtitle }}</h4> }@if (section.contentMarkdown) { <p class="pre-line">{{ section.contentMarkdown }}</p> } @else if (contentText(section.content); as text) { <p class="pre-line">{{ text }}</p> }</article> }</div></section> }
      @if (item.media.length) { <section class="content-section"><h2>Project media</h2><div class="media-grid">@for (media of item.media; track media.id) { <figure><div><app-public-image [src]="media.url" [alt]="media.altText || item.title" [fallbackLabel]="item.title.slice(0, 1)" /></div>@if (media.caption) { <figcaption>{{ media.caption }}</figcaption> }</figure> }</div></section> }
    }
  </div>`,
  styles: `
    .detail-heading{position:relative;padding:1rem 0 3rem}.breadcrumbs{display:flex;gap:.6rem;margin-bottom:2.5rem;color:var(--color-text-dim);font-size:.8rem}.breadcrumbs a{color:var(--color-text-muted);text-decoration:none}.breadcrumbs b{color:var(--color-text);font-weight:500}.title-row{display:flex;justify-content:space-between;align-items:end;gap:2rem}.detail-heading h1{max-width:14ch;margin:.45rem 0;font-size:clamp(3rem,7vw,5rem);line-height:1;letter-spacing:-.045em;overflow-wrap:anywhere}.lead{max-width:48rem;padding-left:1rem;border-left:2px solid var(--color-primary);color:var(--color-text-muted);font-size:1.18rem;line-height:1.6}
    .actions{display:flex;flex-wrap:wrap;gap:.75rem;flex-shrink:0}.facts{display:grid;grid-template-columns:repeat(4,1fr);gap:1rem;margin:3rem 0 0;padding:2rem 0 0;border-top:1px solid var(--color-border)}.facts div{display:grid;gap:.35rem}.facts dt,.section-label{color:var(--color-text-dim);font-size:.68rem;font-weight:700;letter-spacing:.12em;text-transform:uppercase}.facts dd{margin:0;color:var(--color-text)}
    .cover { height: min(65vw, 38rem); overflow: hidden; border-radius: var(--radius-lg); }
    .overview-grid{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(18rem,.7fr);gap:clamp(2rem,6vw,5rem);padding:var(--section-gap) 0}.content-section p{color:var(--color-text-muted);line-height:1.8}.overview{padding:0}.overview h2,.technology-panel h2{font-size:clamp(1.65rem,3vw,2rem)}.technology-panel{align-self:start;padding:1.5rem;border:1px solid var(--color-border);border-radius:var(--radius-xl);background:var(--color-surface-high)}.engineering{padding:var(--section-gap) 0;border-top:1px solid var(--color-border)}.section-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.section-grid article{position:relative;padding:1.5rem;border:1px solid var(--color-border);border-radius:var(--radius-xl);background:var(--color-surface)}.section-index{color:var(--color-primary);font:700 .72rem ui-monospace,monospace}.section-grid h3{font-size:1.25rem}.section-grid h4{color:var(--color-text-muted);font-size:.95rem}
    .media-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); gap: 1rem; } figure { margin: 0; } figure > div { height: 18rem; overflow: hidden; border-radius: var(--radius-lg); } figcaption { margin-top: .5rem; color: var(--color-text-muted); }
    @media(max-width:800px){.title-row{align-items:flex-start;flex-direction:column}.facts{grid-template-columns:repeat(2,1fr)}.overview-grid,.section-grid{grid-template-columns:1fr}.cover{height:min(75vw,28rem)}}@media(max-width:440px){.facts{grid-template-columns:1fr}.actions{width:100%}.actions a{flex:1}}
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
