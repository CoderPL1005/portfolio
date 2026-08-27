import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  template: `<section><span aria-hidden="true">—</span><h2>{{ title() }}</h2><p>{{ message() }}</p></section>`,
  styles: `
    section { padding: clamp(2rem, 7vw, 4rem); border: 1px dashed var(--color-border); border-radius: var(--radius-lg); text-align: center; background: color-mix(in srgb, var(--color-surface) 60%, transparent); }
    span { color: var(--color-primary); font-size: 2rem; } h2 { margin: .5rem 0; font-size: 1.35rem; } p { margin: 0; color: var(--color-text-dim); }
  `,
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly message = input.required<string>();
}
