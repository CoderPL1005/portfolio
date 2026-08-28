import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PublicImageComponent } from '../../../shared/components/public-image/public-image.component';
import { PublicProjectListItem } from './public.models';

@Component({
  selector: 'app-project-card',
  imports: [RouterLink, PublicImageComponent],
  template: `
    <article class="project-card">
      <a class="project-media" [routerLink]="['/projects', project().slug]" [attr.aria-label]="'View ' + project().title">
        <app-public-image [src]="project().thumbnailUrl" [alt]="project().title" [fallbackLabel]="project().title.slice(0, 1)" />
      </a>
      <div class="project-body">
        <div class="project-meta"><span>{{ project().status }}</span>@if (project().featured) { <span>Featured</span> }</div>
        <h3><a [routerLink]="['/projects', project().slug]">{{ project().title }}</a></h3>
        @if (project().subtitle || project().shortDescription) { <p>{{ project().subtitle || project().shortDescription }}</p> }
        @if (project().technologies.length) {
          <ul class="chip-list" aria-label="Technologies">
            @for (technology of project().technologies; track technology.id) { <li>{{ technology.name }}</li> }
          </ul>
        }
      </div>
    </article>
  `,
  styles: `
    :host { display: block; height: 100%; }
    .project-card { height: 100%; overflow: hidden; border: 1px solid var(--color-border); border-radius: var(--radius-xl); background: var(--color-surface); transition:transform .25s,border-color .25s,box-shadow .25s; }
    .project-card:hover{transform:translateY(-4px);border-color:var(--color-outline-variant);box-shadow:0 1.5rem 3rem #0004}.project-media { display: block; height: 14rem; border-bottom:1px solid var(--color-border);text-decoration: none; }
    .project-body { padding: 1.5rem; }
    .project-meta { display: flex; gap: .75rem; color: var(--color-primary); font-size: .72rem; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }
    h3 { margin: .55rem 0; font-size: 1.3rem; } h3 a { color: var(--color-text); text-decoration: none; }
    h3 a:hover { color: var(--color-primary); } p { color: var(--color-text-muted); overflow-wrap: anywhere; }
  `,
})
export class ProjectCardComponent { readonly project = input.required<PublicProjectListItem>(); }
