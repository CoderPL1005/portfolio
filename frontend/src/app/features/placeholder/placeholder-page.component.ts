import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-placeholder-page',
  template: `
    <section class="placeholder-page" aria-labelledby="placeholder-title">
      <p class="eyebrow">{{ route.snapshot.data['eyebrow'] }}</p>
      <h1 id="placeholder-title">{{ route.snapshot.data['title'] }}</h1>
      <p>This route is ready for its feature implementation in a later phase.</p>
    </section>
  `,
  styles: `
    .placeholder-page { width: min(100%, var(--container-max)); margin: 0 auto; padding: clamp(3rem, 8vw, 7rem) var(--page-gutter); }
    .eyebrow { color: var(--color-primary); font: 600 .75rem/1 var(--font-body); letter-spacing: .14em; text-transform: uppercase; }
    h1 { margin: .75rem 0 1rem; font: 700 clamp(2.25rem, 6vw, 4rem)/1.05 var(--font-heading); letter-spacing: -.04em; }
    p:last-child { max-width: 38rem; color: var(--color-text-muted); font-size: 1.05rem; }
  `,
})
export class PlaceholderPageComponent {
  readonly route = inject(ActivatedRoute);
}
