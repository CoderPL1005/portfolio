import { Component, computed, input, signal } from '@angular/core';
import { safeHttpUrl } from '../../../features/public/shared/public-utils';

@Component({
  selector: 'app-public-image',
  template: `
    @if (safeUrl() && !failed()) {
      <img [src]="safeUrl()!" [alt]="alt()" loading="lazy" (error)="failed.set(true)" />
    } @else {
      <span class="fallback" role="img" [attr.aria-label]="alt()">{{ fallbackLabel() }}</span>
    }
  `,
  styles: `
    :host { display: block; width: 100%; height: 100%; overflow: hidden; background: linear-gradient(135deg, var(--color-surface-high), var(--color-surface-lowest)); }
    img { display: block; width: 100%; height: 100%; object-fit: cover; }
    .fallback { width: 100%; height: 100%; min-height: 8rem; display: grid; place-items: center; color: var(--color-primary); font: 700 clamp(1.5rem, 5vw, 3rem) var(--font-heading); }
  `,
})
export class PublicImageComponent {
  readonly src = input<string | null>();
  readonly alt = input.required<string>();
  readonly fallbackLabel = input('Portfolio');
  readonly failed = signal(false);
  readonly safeUrl = computed(() => safeHttpUrl(this.src()));
}
