import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { ProjectCardComponent } from '../shared/project-card.component';
import { PublicSkill } from '../shared/public.models';
import { formatPortfolioDate, isExternalUrl, safeHttpUrl, safeSocialUrl } from '../shared/public-utils';

@Component({
  selector: 'app-home-page',
  imports: [RouterLink, LoadingIndicatorComponent, ProjectCardComponent],
  template: `
    <div class="home-page">
      @if (store.status() === 'loading' || store.status() === 'idle') {
        <div class="state-panel"><app-loading-indicator label="Loading portfolio" /></div>
      } @else if (store.status() === 'error') {
        <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div>
      } @else if (store.data(); as portfolio) {
        <section class="hero" aria-labelledby="home-title">
          <div class="hero-glow" aria-hidden="true"></div>
          <div class="hero-copy-block">
            @if (portfolio.profile.availabilityStatus) { <p class="availability"><span></span>{{ portfolio.profile.availabilityStatus }}</p> }
            <p class="identity">{{ portfolio.profile.fullName }}</p>
            <h1 id="home-title">{{ portfolio.profile.heroHeadline || portfolio.profile.professionalTitle || portfolio.profile.fullName }}</h1>
            @if (portfolio.profile.heroSummary || portfolio.profile.aboutMarkdown) { <p class="hero-copy pre-line">{{ portfolio.profile.heroSummary || portfolio.profile.aboutMarkdown }}</p> }
            <div class="actions"><a class="primary-button" routerLink="/projects">View projects <span aria-hidden="true">&rarr;</span></a><a class="secondary-button" routerLink="/contact">Contact me</a></div>
            @if (coreSkills().length) { <div class="core-stack"><span>Core Stack</span><ul>@for (skill of coreSkills(); track skill.id) { <li>{{ skill.name }}</li> }</ul></div> }
          </div>
          <div class="identity-card" aria-label="Technical identity">
            <div class="terminal-bar"><i></i><i></i><i></i><span>engineer_identity.json</span></div>
            <div class="terminal-body"><strong>{{ portfolio.profile.professionalTitle || 'Software Engineer' }}</strong>@for (group of skillGroups().slice(0, 3); track group.category) { <div class="tree"><span>{{ group.category }}</span>@for (skill of group.skills.slice(0, 2); track skill.id) { <small>{{ skill.name }}</small> }</div> }</div>
          </div>
        </section>

        <section class="fact-strip" aria-label="Portfolio quick facts">
          @if (portfolio.profile.availabilityStatus) { <div><span>Status</span><strong>{{ portfolio.profile.availabilityStatus }}</strong></div> }
          @if (coreSkills().length) { <div><span>Primary Stack</span><strong>{{ coreSkills().slice(0, 3).map(skillName).join(' + ') }}</strong></div> }
          @if (portfolio.featuredProjects[0]; as project) { <div><span>Featured Project</span><strong>{{ project.title }}</strong></div> }
          @if (portfolio.profile.secondaryTitle || portfolio.profile.professionalTitle; as focus) { <div><span>Current Focus</span><strong>{{ focus }}</strong></div> }
        </section>

        <section class="about" id="about"><div class="about-copy"><div class="section-title"><span></span><h2>System Logic &amp; Data Flow</h2></div><p class="pre-line">{{ portfolio.profile.aboutMarkdown || portfolio.profile.heroSummary }}</p></div><aside><h3>Quick Facts</h3>@if (portfolio.profile.professionalTitle) { <div><span>Focus</span><strong>{{ portfolio.profile.professionalTitle }}</strong></div> }@if (coreSkills().length) { <div><span>Stack</span><strong>{{ coreSkills().slice(0, 3).map(skillName).join(', ') }}</strong></div> }@if (portfolio.profile.university || portfolio.profile.major) { <div><span>Education</span><strong>{{ portfolio.profile.major || portfolio.profile.university }}</strong></div> }@if (portfolio.profile.location) { <div><span>Location</span><strong>{{ portfolio.profile.location }}</strong></div> }</aside></section>

        @if (portfolio.featuredProjects.length) {
          <section class="section-block" id="projects"><div class="section-heading"><div><span class="eyebrow">Selected work</span><h2>Featured projects</h2></div><a routerLink="/projects">All projects</a></div><div class="card-grid">@for (project of portfolio.featuredProjects; track project.id) { <app-project-card [project]="project" /> }</div></section>
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
    .home-page{width:100%}.home-page>.state-panel,.section-block,.about{width:min(100%,var(--container-max));margin-inline:auto;padding-inline:var(--page-gutter)}
    .hero { position:relative;width:min(100%,var(--container-max));margin:auto;padding:clamp(4rem,8vw,8rem) var(--page-gutter) var(--section-gap);display:grid;grid-template-columns:minmax(0,1fr) minmax(22rem,.82fr);align-items:center;gap:clamp(2.5rem,6vw,5rem);overflow:visible }
    .hero-glow{position:absolute;z-index:0;inset:2rem auto auto -12rem;width:44rem;height:36rem;border-radius:50%;background:color-mix(in srgb,var(--color-primary-strong) 12%,transparent);filter:blur(90px);pointer-events:none}.hero-copy-block,.identity-card{position:relative;z-index:1}.availability{display:inline-flex;align-items:center;gap:.55rem;margin:0 0 1.5rem;padding:.38rem .75rem;border:1px solid var(--color-border);border-radius:999px;color:var(--color-primary);background:var(--color-surface-high);font-size:.72rem;font-weight:700}.availability span{width:.45rem;height:.45rem;border-radius:50%;background:var(--color-primary);box-shadow:0 0 0 .25rem color-mix(in srgb,var(--color-primary) 12%,transparent)}.identity{margin:0 0 .45rem;color:var(--color-text-dim);font-size:.75rem;font-weight:700;letter-spacing:.12em;text-transform:uppercase}
    h1 { max-width:14ch;margin:0 0 1rem;font-size:clamp(2.75rem,5.2vw,4.5rem);line-height:1.04;letter-spacing:-.045em;overflow-wrap:anywhere }.hero-copy{max-width:42rem;margin:0;color:var(--color-text-muted);font-size:clamp(1rem,1.7vw,1.125rem);line-height:1.65}
    .actions,.socials{display:flex;flex-wrap:wrap;gap:.75rem;margin-top:1.75rem}.core-stack{margin-top:2.25rem}.core-stack>span{display:block;margin-bottom:.65rem;color:var(--color-text-dim);font-size:.64rem;font-weight:700;letter-spacing:.14em;text-transform:uppercase}.core-stack ul{display:flex;flex-wrap:wrap;gap:.45rem;margin:0;padding:0;list-style:none}.core-stack li{padding:.38rem .7rem;border:1px solid var(--color-border);border-radius:.4rem;color:var(--color-text-muted);background:var(--color-surface-high);font-size:.75rem}
    .identity-card{min-height:25rem;border:1px solid var(--color-border);border-radius:var(--radius-xl);background:var(--color-surface-high);padding:1rem;box-shadow:0 0 3rem color-mix(in srgb,var(--color-primary-strong) 15%,transparent)}.identity-card:before{content:'';position:absolute;inset:-2px;z-index:-1;border-radius:inherit;background:linear-gradient(135deg,color-mix(in srgb,var(--color-primary) 40%,transparent),transparent 42%)}.terminal-bar{display:flex;align-items:center;gap:.45rem;padding:.75rem;border:1px solid var(--color-border);border-bottom:0;border-radius:.65rem .65rem 0 0;background:color-mix(in srgb,var(--color-surface-high) 40%,var(--color-background))}.terminal-bar i{width:.65rem;height:.65rem;border-radius:50%;background:var(--color-border)}.terminal-bar span{margin-left:auto;color:var(--color-text-dim);font:500 .68rem var(--font-body)}.terminal-body{min-height:20rem;padding:2rem;border:1px solid var(--color-border);border-radius:0 0 .65rem .65rem;background:var(--color-background);font-family:ui-monospace,SFMono-Regular,Consolas,monospace}.terminal-body>strong{display:block;margin-bottom:1.25rem;color:var(--color-primary)}.tree{display:grid;gap:.35rem;margin:.8rem 0;padding-left:1.35rem;border-left:1px solid var(--color-border)}.tree span{color:var(--color-text)}.tree small{padding-left:1rem;color:var(--color-text-dim)}
    .fact-strip{width:100%;display:grid;grid-template-columns:repeat(4,1fr);padding:1.5rem max(var(--page-gutter),calc((100% - var(--container-max))/2));border-block:1px solid var(--color-border);background:color-mix(in srgb,var(--color-surface-high) 34%,transparent)}.fact-strip div{display:grid;gap:.3rem;padding:0 1.5rem;border-left:1px solid var(--color-border)}.fact-strip div:first-child{padding-left:0;border-left:0}.fact-strip span{color:var(--color-text-dim);font-size:.65rem;font-weight:700;letter-spacing:.1em;text-transform:uppercase}.fact-strip strong{font-size:.9rem;font-weight:600;overflow-wrap:anywhere}
    .about{display:grid;grid-template-columns:minmax(0,2fr) minmax(18rem,1fr);gap:clamp(2rem,7vw,6rem);padding-block:var(--section-gap)}.section-title{display:flex;align-items:center;gap:1rem}.section-title>span{width:2rem;height:1px;background:var(--color-primary)}.about h2{font-size:clamp(1.75rem,3vw,2rem)}.about-copy>p{color:var(--color-text-muted);font-size:1.08rem;line-height:1.8}.about aside{display:grid;align-content:start;gap:0;padding:1.5rem;border:1px solid var(--color-border);border-radius:var(--radius-xl);background:var(--color-surface-high)}.about aside h3{margin:0 0 .75rem;padding-bottom:1rem;border-bottom:1px solid var(--color-border);font-size:.72rem;letter-spacing:.14em;text-transform:uppercase}.about aside div{display:flex;justify-content:space-between;gap:1rem;padding:.65rem 0}.about aside span{color:var(--color-text-dim)}.about aside strong{text-align:right;font-size:.88rem}
    .preview-list { display: grid; } .preview-list article { display: flex; justify-content: space-between; gap: 1rem; padding: 1.25rem 0; border-top: 1px solid var(--color-border); }
    .preview-list h3, .preview-list p { margin: 0; } .preview-list p, .preview-list span { color: var(--color-text-muted); }
    .skill-preview { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 1rem; } .skill-preview article { padding: 1.25rem; border: 1px solid var(--color-border); border-radius: var(--radius-lg); }
    .socials a { color: var(--color-text); padding: .7rem 1rem; border: 1px solid var(--color-border); border-radius: var(--radius-md); }
    @media(max-width:900px){.hero{grid-template-columns:1fr}.identity-card{width:min(100%,36rem)}.fact-strip{grid-template-columns:repeat(2,1fr)}.fact-strip div:nth-child(3){border-left:0}.about{grid-template-columns:1fr}}
    @media(max-width:560px){.hero{padding-top:3rem}.identity-card{min-height:auto}.terminal-body{min-height:18rem;padding:1.25rem}.fact-strip{grid-template-columns:1fr}.fact-strip div,.fact-strip div:first-child,.fact-strip div:nth-child(3){padding:.8rem 0;border-left:0;border-top:1px solid var(--color-border)}.fact-strip div:first-child{border-top:0}h1{font-size:clamp(2.45rem,12vw,3.4rem)}}
  `,
})
export class HomePageComponent {
  readonly store = inject(PortfolioStore);
  readonly http = safeHttpUrl; readonly socialUrl = safeSocialUrl; readonly external = isExternalUrl; readonly date = formatPortfolioDate;
  readonly skillGroups = computed(() => groupSkills(this.store.data()?.skills ?? []).slice(0, 4));
  readonly coreSkills = computed(() => (this.store.data()?.skills ?? []).slice(0, 6));
  readonly skillName = (skill: PublicSkill) => skill.name;
  constructor() { this.store.load(); }
}

function groupSkills(skills: PublicSkill[]): { category: string; skills: PublicSkill[] }[] {
  const groups = new Map<string, PublicSkill[]>();
  for (const skill of skills) groups.set(skill.category, [...(groups.get(skill.category) ?? []), skill]);
  return [...groups].map(([category, items]) => ({ category, skills: items }));
}
