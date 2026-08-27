import { Component, inject, signal } from '@angular/core';
import { take } from 'rxjs';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { ProjectCardComponent } from '../shared/project-card.component';
import { PublicProjectListItem } from '../shared/public.models';
import { PublicPortfolioService } from '../shared/public-portfolio.service';

@Component({
  selector: 'app-projects-page', imports: [EmptyStateComponent, LoadingIndicatorComponent, ProjectCardComponent],
  template: `<div class="public-page"><header class="page-heading"><span class="eyebrow">Selected work</span><h1>Projects</h1><p>Published work, case studies, and the technologies behind them.</p></header>
    <div class="filters" aria-label="Project filters"><button type="button" [class.active]="filter() === undefined" (click)="load(undefined)">All</button><button type="button" [class.active]="filter() === true" (click)="load(true)">Featured</button></div>
    @if (status() === 'loading') { <div class="state-panel"><app-loading-indicator label="Loading projects" /></div> }
    @else if (status() === 'error') { <div class="state-panel" role="alert"><p>{{ error() }}</p><button class="retry-button" type="button" (click)="load(filter())">Try again</button></div> }
    @else if (!projects().length) { <app-empty-state title="No projects yet" message="Published projects will appear here." /> }
    @else { <div class="card-grid">@for (project of projects(); track project.id) { <app-project-card [project]="project" /> }</div> }
  </div>`,
  styles: `.filters { display: flex; gap: .5rem; margin-bottom: 2rem; } .filters button { border: 1px solid var(--color-border); border-radius: 999px; padding: .55rem 1rem; color: var(--color-text-muted); background: var(--color-surface); cursor: pointer; } .filters .active { border-color: var(--color-primary); color: var(--color-primary); }`,
})
export class ProjectsPageComponent {
  private readonly api = inject(PublicPortfolioService);
  readonly projects = signal<PublicProjectListItem[]>([]); readonly status = signal<'loading'|'loaded'|'error'>('loading'); readonly error = signal<string | null>(null); readonly filter = signal<boolean | undefined>(undefined);
  constructor() { this.load(); }
  load(featured?: boolean): void { this.filter.set(featured); this.status.set('loading'); this.error.set(null); this.api.getProjects(featured).pipe(take(1)).subscribe({ next: p => { this.projects.set(p); this.status.set('loaded'); }, error: (e: unknown) => { this.error.set(e instanceof ApiHttpError ? e.apiError.message : 'Unable to load projects.'); this.status.set('error'); } }); }
}
