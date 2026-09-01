import { Component, computed, input } from '@angular/core';

const SOCIAL_ICON_PATHS: Readonly<Record<string, string>> = {
  github: 'M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.093.682-.217.682-.483 0-.237-.009-.866-.014-1.7-2.782.605-3.369-1.345-3.369-1.345-.455-1.158-1.11-1.466-1.11-1.466-.908-.621.069-.608.069-.608 1.004.071 1.532 1.034 1.532 1.034.892 1.53 2.341 1.088 2.91.832.091-.647.349-1.088.635-1.338-2.221-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.987 1.029-2.688-.103-.254-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0 1 12 6.844a9.55 9.55 0 0 1 2.504.337c1.909-1.296 2.747-1.026 2.747-1.026.546 1.378.203 2.396.1 2.65.64.701 1.028 1.595 1.028 2.688 0 3.848-2.338 4.695-4.566 4.943.359.31.678.921.678 1.856 0 1.34-.012 2.421-.012 2.751 0 .268.18.58.688.482A10.02 10.02 0 0 0 22 12.017C22 6.484 17.522 2 12 2Z',
  linkedin: 'M6.5 8.5H3.25V21H6.5V8.5ZM4.875 3A1.875 1.875 0 1 0 4.875 6.75 1.875 1.875 0 0 0 4.875 3ZM21 13.84c0-3.77-2.01-5.52-4.7-5.52-2.16 0-3.13 1.19-3.67 2.03V8.5H9.38V21h3.25v-6.19c0-1.63.31-3.21 2.33-3.21 1.99 0 2.02 1.86 2.02 3.31V21H21v-7.16Z',
  email: 'M3 5h18v14H3V5Zm9 7.1L19.1 7H4.9l7.1 5.1Zm0 2.45L5 9.52V17h14V9.52l-7 5.03Z',
  website: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm6.92 6h-3.16a15.7 15.7 0 0 0-1.38-3.56A8.05 8.05 0 0 1 18.92 8ZM12 4c.83 1.2 1.43 2.54 1.76 4h-3.52A13.7 13.7 0 0 1 12 4ZM4.26 14a7.82 7.82 0 0 1 0-4h3.42a16.4 16.4 0 0 0 0 4H4.26Zm.82 2h3.16a15.7 15.7 0 0 0 1.38 3.56A8.05 8.05 0 0 1 5.08 16ZM8.24 8H5.08a8.05 8.05 0 0 1 4.54-3.56A15.7 15.7 0 0 0 8.24 8ZM12 20a13.7 13.7 0 0 1-1.76-4h3.52A13.7 13.7 0 0 1 12 20Zm2.08-6H9.92a14.17 14.17 0 0 1 0-4h4.16a14.17 14.17 0 0 1 0 4Zm.3 5.56A15.7 15.7 0 0 0 15.76 16h3.16a8.05 8.05 0 0 1-4.54 3.56ZM16.32 14a16.4 16.4 0 0 0 0-4h3.42a7.82 7.82 0 0 1 0 4h-3.42Z',
};

@Component({
  selector: 'app-social-icon',
  template: `
    @if (path(); as iconPath) {
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false" [attr.data-icon-key]="normalizedKey()">
        <path [attr.d]="iconPath" />
      </svg>
    } @else {
      <span class="fallback" aria-hidden="true" data-icon-fallback="true">{{ fallback() }}</span>
    }
  `,
  styles: `:host{display:grid;place-items:center;width:1.1rem;height:1.1rem}:host svg{width:100%;height:100%;fill:currentColor}.fallback{font:700 .72rem var(--font-heading)}`,
})
export class SocialIconComponent {
  readonly iconKey = input<string | null>();
  readonly label = input.required<string>();
  readonly normalizedKey = computed(() => this.iconKey()?.trim().toLowerCase() ?? '');
  readonly path = computed(() => SOCIAL_ICON_PATHS[this.normalizedKey()] ?? null);
  readonly fallback = computed(() => this.label().trim().slice(0, 1).toUpperCase() || '?');
}
