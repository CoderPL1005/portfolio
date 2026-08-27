import { Component, input } from '@angular/core';

@Component({
  selector: 'app-loading-indicator',
  template: `<span class="spinner" aria-hidden="true"></span><span>{{ label() }}</span>`,
  host: { role: 'status', 'aria-live': 'polite' },
  styles: `
    :host { display: inline-flex; align-items: center; justify-content: center; gap: .625rem; }
    .spinner { width: 1rem; height: 1rem; border: 2px solid currentColor; border-right-color: transparent; border-radius: 50%; animation: spin .7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `,
})
export class LoadingIndicatorComponent {
  readonly label = input('Loading');
}
