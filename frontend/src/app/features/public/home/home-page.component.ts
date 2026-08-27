import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PublicImageComponent } from '../../../shared/components/public-image/public-image.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { ProjectCardComponent } from '../shared/project-card.component';
import { PublicSkill } from '../shared/public.models';
import { formatPortfolioDate, isExternalUrl, safeHttpUrl, safeSocialUrl } from '../shared/public-utils';

@Component({
  selector: 'app-home-page',
  imports: [RouterLink, LoadingIndicatorComponent, PublicImageComponent, ProjectCardComponent],
  template: `
    <div class="public-page">
      @if (store.status() === 'loading' || store.status() === 'idle') {
        <div class="state-panel"><app-loading-indicator label="Loading portfolio" /></div>
      } @else if (store.status() === 'error') {
        <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div>
      } @else if (store.data(); as portfolio) {
        <section class="hero" aria-labelledby="home-title">
          <div>
            @if (portfolio.profile.availabilityStatus) { <p class="eyebrow">{{ portfolio.profile.availabilityStatus }}</p> }
            <h1 id="home-title">{{ portfolio.profile.fullName }}</h1>
            @if (portfolio.profile.heroHeadline || portfolio.profile.professionalTitle) { <h2>{{ portfolio.profile.heroHeadline || portfolio.profile.professionalTitle }}</h2> }
            @if (portfolio.profile.heroSummary || portfolio.profile.aboutMarkdown) { <p class="hero-copy pre-line">{{ portfolio.profile.heroSummary || portfolio.profile.aboutMarkdown }}</p> }
            <div class="actions"><a class="primary-button" routerLink="/projects">View projects</a><a class="secondary-button" routerLink="/contact">Get in touch</a>@if (http(portfolio.profile.cvUrl); as cv) { <a class="secondary-button" [href]="cv" target="_blank" rel="noopener noreferrer">View CV</a> }</div>
          </div>
          @if (portfolio.profile.profileImageUrl) { <div class="portrait"><app-public-image [src]="portfolio.profile.profileImageUrl" [alt]="portfolio.profile.fullName" [fallbackLabel]="portfolio.profile.fullName.slice(0, 1)" /></div> }
        </section>

        @if (portfolio.featuredProjects.length) {
          <section class="section-block"><div class="section-heading"><div><span class="eyebrow">Selected work</span><h2>Featured projects</h2></div><a routerLink="/projects">All projects</a></div><div class="card-grid">@for (project of portfolio.featuredProjects; track project.id) { <app-project-card [project]="project" /> }</div></section>
        }
        @if (portfolio.experiences.length) {
          <section class="section-block"><div class="section-heading"><div><span class="eyebrow">Experience</span><h2>Recent roles</h2></div><a routerLink="/experience">Full experience</a></div><div class="preview-list">@for (item of portfolio.experiences.slice(0, 3); track item.id) { <article><div><h3>{{ item.roleTitle }}</h3><p>{{ item.companyName }}</p></div><span>{{ date(item.startDate) }} @if (item.isCurrent) { &ndash; Present }</span></article> }</div></section>
        }
        @if (skillGroups().length) {
          <section class="section-block"><div class="section-heading"><div><span class="eyebrow">Capabilities</span><h2>Skills</h2></div><a routerLink="/skills">All skills</a></div><div class="skill-preview">@for (group of skillGroups(); track group.category) { <article><h3>{{ group.category }}</h3><ul class="chip-list">@for (skill of group.skills; track skill.id) { <li>{{ skill.name }}</li> }</ul></article> }</div></section>
        }
        @if (portfolio.journey.length) {
          <section class="section-block"><div class="section-heading"><div><span class="eyebrow">Path</span><h2>Journey</h2></div><a routerLink="/journey">Full journey</a></div><div class="preview-list">@for (item of portfolio.journey.slice(0, 3); track item.id) { <article><div><h3>{{ item.title }}</h3>@if (item.subtitle) { <p>{{ item.subtitle }}</p> }</div>@if (date(item.occurredAt); as occurred) { <span>{{ occurred }}</span> }</article> }</div></section>
        }
        @if (portfolio.socialLinks.length) {
          <section class="section-block"><div class="section-heading"><div><span class="eyebrow">Connect</span><h2>Find me online</h2></div></div><div class="socials">@for (social of portfolio.socialLinks; track social.id) { @if (socialUrl(social.url); as url) { <a [href]="url" [attr.target]="external(url) ? '_blank' : null" [attr.rel]="external(url) ? 'noopener noreferrer' : null">{{ social.label || social.platform }}</a> } }</div></section>
        }
      }
    </div>
  `,
  styles: `
    .hero { min-height: min(46rem, calc(100vh - 4rem)); display: grid; grid-template-columns: minmax(0, 1.3fr) minmax(15rem, .7fr); align-items: center; gap: clamp(2rem, 7vw, 6rem); }
    h1 { max-width: 13ch; margin: .5rem 0; font-size: clamp(3rem, 9vw, 7rem); line-height: .95; overflow-wrap: anywhere; }
    .hero h2 { color: var(--color-primary); font-size: clamp(1.2rem, 3vw, 2rem); } .hero-copy { max-width: 45rem; color: var(--color-text-muted); font-size: 1.08rem; }
    .actions, .socials { display: flex; flex-wrap: wrap; gap: .75rem; margin-top: 1.5rem; } .portrait { aspect-ratio: 4 / 5; overflow: hidden; border-radius: var(--radius-lg); }
    .preview-list { display: grid; } .preview-list article { display: flex; justify-content: space-between; gap: 1rem; padding: 1.25rem 0; border-top: 1px solid var(--color-border); }
    .preview-list h3, .preview-list p { margin: 0; } .preview-list p, .preview-list span { color: var(--color-text-muted); }
    .skill-preview { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 1rem; } .skill-preview article { padding: 1.25rem; border: 1px solid var(--color-border); border-radius: var(--radius-lg); }
    .socials a { color: var(--color-text); padding: .7rem 1rem; border: 1px solid var(--color-border); border-radius: var(--radius-md); }
    @media (max-width: 760px) { .hero { grid-template-columns: 1fr; padding: 3rem 0; } .portrait { max-width: 22rem; } }
  `,
})
export class HomePageComponent {
  readonly store = inject(PortfolioStore);
  readonly http = safeHttpUrl; readonly socialUrl = safeSocialUrl; readonly external = isExternalUrl; readonly date = formatPortfolioDate;
  readonly skillGroups = computed(() => groupSkills(this.store.data()?.skills ?? []).slice(0, 4));
  constructor() { this.store.load(); }
}

function groupSkills(skills: PublicSkill[]): { category: string; skills: PublicSkill[] }[] {
  const groups = new Map<string, PublicSkill[]>();
  for (const skill of skills) groups.set(skill.category, [...(groups.get(skill.category) ?? []), skill]);
  return [...groups].map(([category, items]) => ({ category, skills: items }));
}
