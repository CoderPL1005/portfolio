import { Component, input } from '@angular/core';

@Component({
  selector: 'app-inline-alert',
  template: `<span aria-hidden="true">!</span><span>{{ message() }}</span>`,
  host: { role: 'alert' },
  styles: `
    :host { display: flex; gap: .75rem; align-items: flex-start; padding: .875rem 1rem; border: 1px solid color-mix(in srgb, var(--color-error) 45%, transparent); border-radius: var(--radius-md); color: var(--color-error); background: color-mix(in srgb, var(--color-error) 8%, transparent); font-size: .875rem; }
    :host > span:first-child { display: grid; flex: 0 0 1.25rem; height: 1.25rem; place-items: center; border: 1px solid currentColor; border-radius: 50%; font-weight: 700; }
  `,
})
export class InlineAlertComponent {
  readonly message = input.required<string>();
}
