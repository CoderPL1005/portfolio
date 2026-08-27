import { Component, computed, inject } from '@angular/core';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { PublicSkill } from '../shared/public.models';

@Component({ selector: 'app-skills-page', imports: [EmptyStateComponent, LoadingIndicatorComponent], template: `
  <div class="public-page"><header class="page-heading"><span class="eyebrow">Capabilities</span><h1>Skills</h1><p>Technologies and practices grouped by their published category.</p></header>
  @if (store.status() === 'loading' || store.status() === 'idle') { <div class="state-panel"><app-loading-indicator label="Loading skills" /></div> }
  @else if (store.status() === 'error') { <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div> }
  @else if (!groups().length) { <app-empty-state title="No skills published" message="Published skills will appear here." /> }
  @else { <div class="groups">@for (group of groups(); track group.category) { <section><h2>{{ group.category }}</h2><div class="skills">@for (skill of group.skills; track skill.id) { <article><div><h3>{{ skill.name }}</h3><span>{{ label(skill.experienceLevel) }}</span></div>@if (skill.description) { <p>{{ skill.description }}</p> }</article> }</div></section> }</div> }
  </div>`, styles: `.groups { display: grid; gap: 3rem; } .groups section { padding-top: 2rem; border-top: 1px solid var(--color-border); } .skills { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 17rem), 1fr)); gap: .75rem; } article { padding: 1.15rem; border: 1px solid var(--color-border); border-radius: var(--radius-lg); background: var(--color-surface); } article > div { display: flex; justify-content: space-between; gap: 1rem; } h3 { margin: 0; overflow-wrap: anywhere; } span { color: var(--color-primary); font-size: .72rem; font-weight: 700; } p { color: var(--color-text-muted); }` })
export class SkillsPageComponent {
  readonly store = inject(PortfolioStore);
  readonly groups = computed(() => groupSkills(this.store.data()?.skills ?? []));
  constructor() { this.store.load(); }
  label(level: PublicSkill['experienceLevel']): string { return level === 'USED' ? 'Used' : level === 'LEARNING' ? 'Learning' : 'Exploring'; }
}
export function groupSkills(skills: PublicSkill[]): { category: string; skills: PublicSkill[] }[] { const map = new Map<string, PublicSkill[]>(); for (const skill of skills) map.set(skill.category, [...(map.get(skill.category) ?? []), skill]); return [...map].map(([category, items]) => ({ category, skills: items })); }
