import { Component, inject, signal } from '@angular/core';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';
import { PortfolioStore } from '../shared/portfolio.store';
import { isExternalUrl, safeSocialUrl } from '../shared/public-utils';

@Component({
  selector: 'app-contact-page',
  imports: [LoadingIndicatorComponent],
  template: `
    <div class="public-page contact-layout">
      <header class="page-heading">
        <span class="eyebrow">Connect</span>
        <h1>Contact</h1>
        <p>Reach out through email or one of the published social profiles below.</p>
      </header>
      @if (store.status() === 'loading' || store.status() === 'idle') {
        <div class="state-panel"><app-loading-indicator label="Loading contact information" /></div>
      } @else if (store.status() === 'error') {
        <div class="state-panel" role="alert"><p>{{ store.error() }}</p><button class="retry-button" type="button" (click)="store.retry()">Try again</button></div>
      } @else if (store.data(); as portfolio) {
        <section class="contact-links" aria-labelledby="contact-links-title">
          <h2 id="contact-links-title">Contact information</h2>
          @if (portfolio.profile.email; as email) {
            <button class="email-copy" type="button" (click)="copyEmail(email)" [attr.aria-label]="'Copy email address ' + email" [attr.title]="'Copy ' + email">
              <span>Email</span><strong>{{ email }}</strong>
            </button>
            @if (copyStatus() === 'COPIED') { <p class="copy-feedback" role="status">Email copied</p> }
            @if (copyStatus() === 'FAILED') { <p class="copy-feedback copy-error" role="alert">Unable to copy email. Please copy it manually.</p> }
          }
          @for (social of portfolio.socialLinks; track social.id) {
            @if (socialUrl(social.url); as url) {
              <a [href]="url" [attr.target]="external(url) ? '_blank' : null" [attr.rel]="external(url) ? 'noopener noreferrer' : null"><span>{{ social.platform }}</span><strong>{{ social.label || social.platform }}</strong></a>
            }
          }
          @if (!portfolio.profile.email && !portfolio.socialLinks.length) { <p class="empty-state">No contact links are currently published.</p> }
        </section>
      }
    </div>
  `,
  styles: `
    .contact-layout{display:grid;grid-template-columns:minmax(0,.8fr) minmax(0,1.2fr);gap:clamp(2rem,8vw,7rem)}
    .contact-links{display:grid;align-content:start;gap:.75rem;padding:clamp(1.25rem,4vw,2.5rem);border:1px solid var(--color-border);border-radius:var(--radius-lg);background:var(--color-surface)}
    .contact-links h2{margin:0 0 .5rem}.contact-links a,.email-copy{display:grid;gap:.25rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);color:var(--color-text);background:var(--color-background);text-align:left;font:inherit}
    .contact-links a:hover,.email-copy:hover{border-color:var(--color-primary)}.email-copy{width:100%;cursor:pointer}.email-copy:focus-visible{outline:2px solid var(--color-primary);outline-offset:2px}.contact-links span{color:var(--color-text-dim);font-size:.72rem;font-weight:700;letter-spacing:.1em;text-transform:uppercase}.contact-links strong{overflow-wrap:anywhere}.copy-feedback{margin:-.25rem 0 .25rem;padding:0 .25rem;color:var(--color-primary);font-size:.8rem}.copy-error{color:var(--color-error)}.empty-state{color:var(--color-text-muted)}
    @media(max-width:760px){.contact-layout{grid-template-columns:1fr}}
  `,
})
export class ContactPageComponent {
  readonly store = inject(PortfolioStore);
  readonly socialUrl = safeSocialUrl;
  readonly external = isExternalUrl;
  readonly copyStatus = signal<'COPIED' | 'FAILED' | null>(null);
  constructor() { this.store.load(); }

  async copyEmail(email: string): Promise<void> {
    try {
      if (typeof navigator === 'undefined' || !navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable.');
      await navigator.clipboard.writeText(email);
      this.copyStatus.set('COPIED');
    } catch {
      this.copyStatus.set('FAILED');
    }
  }
}
