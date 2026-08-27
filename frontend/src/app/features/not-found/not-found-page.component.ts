import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `
    <main class="not-found">
      <p>404</p>
      <h1>Page not found</h1>
      <a routerLink="/">Return home</a>
    </main>
  `,
  styles: `
    .not-found { min-height: 100vh; display: grid; place-content: center; gap: 1rem; padding: 2rem; text-align: center; background: var(--color-background); }
    p { margin: 0; color: var(--color-primary); font: 600 .875rem var(--font-body); letter-spacing: .18em; }
    h1 { margin: 0; font: 700 clamp(2rem, 7vw, 4rem) var(--font-heading); }
    a { color: var(--color-primary); }
  `,
})
export class NotFoundPageComponent {}
