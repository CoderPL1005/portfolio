import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize, take } from 'rxjs';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { PublicPortfolioService } from '../shared/public-portfolio.service';

@Component({ selector: 'app-contact-page', imports: [ReactiveFormsModule], template: `
  <div class="public-page contact-layout"><header class="page-heading"><span class="eyebrow">Start a conversation</span><h1>Contact</h1><p>Send a message through the portfolio contact form.</p></header>
  <section aria-labelledby="contact-form-title"><h2 id="contact-form-title" class="sr-only">Contact form</h2>
    @if (success()) { <div class="success" role="status"><h2>Message sent</h2><p>Thank you. Your message was received.</p><button class="secondary-button" type="button" (click)="success.set(false)">Send another</button></div> }
    @else { <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      @if (generalError()) { <div class="form-alert" role="alert">{{ generalError() }}</div> }
      <label>Name<input type="text" formControlName="name" maxlength="255" autocomplete="name" [attr.aria-invalid]="invalid('name')" />@if (invalid('name')) { <span>Name is required and must be 255 characters or fewer.</span> }@if (fieldError('name'); as error) { <span>{{ error }}</span> }</label>
      <label>Email<input type="email" formControlName="email" maxlength="255" autocomplete="email" [attr.aria-invalid]="invalid('email')" />@if (invalid('email')) { <span>Enter a valid email address of 255 characters or fewer.</span> }@if (fieldError('email'); as error) { <span>{{ error }}</span> }</label>
      <label>Subject <small>(optional)</small><input type="text" formControlName="subject" maxlength="255" [attr.aria-invalid]="invalid('subject')" />@if (invalid('subject')) { <span>Subject must be 255 characters or fewer.</span> }@if (fieldError('subject'); as error) { <span>{{ error }}</span> }</label>
      <label>Message<textarea formControlName="message" maxlength="5000" rows="8" [attr.aria-invalid]="invalid('message')"></textarea>@if (invalid('message')) { <span>Message is required and must be 5,000 characters or fewer.</span> }@if (fieldError('message'); as error) { <span>{{ error }}</span> }</label>
      <button class="primary-button" type="submit" [disabled]="submitting()">{{ submitting() ? 'Sending…' : 'Send message' }}</button>
    </form> }
  </section></div>`, styles: `.contact-layout { display: grid; grid-template-columns: minmax(0, .8fr) minmax(0, 1.2fr); gap: clamp(2rem, 8vw, 7rem); } form { display: grid; gap: 1.25rem; padding: clamp(1.25rem, 4vw, 2.5rem); border: 1px solid var(--color-border); border-radius: var(--radius-lg); background: var(--color-surface); } label { display: grid; gap: .45rem; font-weight: 700; } input, textarea { width: 100%; border: 1px solid var(--color-border); border-radius: var(--radius-md); padding: .8rem; color: var(--color-text); background: var(--color-background); font: inherit; resize: vertical; } label span, .form-alert { color: var(--color-danger, #e36b6b); font-size: .8rem; } small { color: var(--color-text-muted); font-weight: 400; } button[disabled] { cursor: wait; opacity: .65; } .success { padding: 2rem; border: 1px solid var(--color-primary); border-radius: var(--radius-lg); } @media (max-width: 760px) { .contact-layout { grid-template-columns: 1fr; } }` })
export class ContactPageComponent {
  private readonly api = inject(PublicPortfolioService);
  readonly submitting = signal(false); readonly success = signal(false); readonly generalError = signal<string | null>(null); readonly backendErrors = signal<Record<string, string[]>>({});
  readonly form = new FormGroup({ name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(255)] }), email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(255)] }), subject: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(255)] }), message: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(5000)] }) });
  invalid(field: keyof typeof this.form.controls): boolean { const control = this.form.controls[field]; return control.invalid && (control.touched || control.dirty); }
  fieldError(field: string): string | null { const errors = this.backendErrors(); return errors[field]?.[0] ?? errors[capitalize(field)]?.[0] ?? null; }
  submit(): void { if (this.submitting()) return; this.generalError.set(null); this.backendErrors.set({}); if (this.form.invalid) { this.form.markAllAsTouched(); return; } this.submitting.set(true); const value = this.form.getRawValue(); this.api.submitContact({ ...value, subject: value.subject.trim() || null }).pipe(take(1), finalize(() => this.submitting.set(false))).subscribe({ next: () => { this.form.reset({ name: '', email: '', subject: '', message: '' }); this.success.set(true); }, error: (e: unknown) => this.handleError(e) }); }
  private handleError(error: unknown): void { if (error instanceof ApiHttpError) { if (error.status === 429) { this.generalError.set('Too many messages were sent recently. Please wait and try again.'); return; } if (error.apiError.code === 'VALIDATION_ERROR' && error.apiError.details) { this.backendErrors.set(error.apiError.details); this.generalError.set('Please review the highlighted fields.'); return; } } this.generalError.set('Your message could not be sent. Please try again.'); }
}
function capitalize(value: string): string { return value.charAt(0).toUpperCase() + value.slice(1); }
