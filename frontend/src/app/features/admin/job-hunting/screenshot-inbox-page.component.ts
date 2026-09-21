import { Component, OnDestroy, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize, take } from 'rxjs';
import { backendFieldError, safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { RawJobPostingSummary, ScreenshotSubmissionResult } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

interface ScreenshotPreview {
  file: File;
  url: string;
}

@Component({
  selector: 'app-screenshot-inbox-page',
  imports: [RouterLink],
  template: `
    <div class="admin-page screenshot-inbox">
      <a routerLink="/admin/job-hunting/jobs">← Jobs</a>
      <header class="inbox-header"><p class="eyebrow">JOB HUNTING</p><h1>Screenshot inbox</h1><p>Select every screenshot belonging to one job post, then submit them together.</p></header>
      <label class="picker admin-panel">
        <span class="picker-icon" aria-hidden="true">+</span>
        <strong>Select screenshots</strong>
        <span>JPEG or PNG · up to 10 files · 10 MiB each · 50 MiB total</span>
        <input type="file" accept="image/jpeg,image/png" multiple (change)="selectFiles($event)">
      </label>
      @if (validationError()) { <p class="admin-error" role="alert">{{ validationError() }}</p> }
      @if (error()) { <p class="admin-error" role="alert">{{ error() }}</p> }
      @if (success(); as result) {
        <div class="state-panel success" role="status">
          <span>{{ result.attachmentCount }} screenshot{{ result.attachmentCount === 1 ? '' : 's' }} received. This job is awaiting processing.</span>
          <button type="button" class="admin-button primary" (click)="analyze(result.rawJobPostingId)" [disabled]="analyzingId() !== null">
            {{ analyzingId() === result.rawJobPostingId ? 'Analyzing...' : 'Analyze' }}
          </button>
        </div>
      }
      @if (previews().length) {
        <section aria-label="Selected screenshots">
          <h2>{{ previews().length }} selected · {{ totalSizeLabel() }}</h2>
          <div class="preview-grid">
            @for (preview of previews(); track preview.url; let index = $index) {
              <article>
                <span class="order">{{ index + 1 }}</span>
                <img [src]="preview.url" [alt]="'Selected screenshot ' + (index + 1)">
                <div><span>{{ preview.file.name }}</span><button type="button" class="admin-button danger" (click)="remove(index)" [disabled]="submitting()">Remove</button></div>
              </article>
            }
          </div>
          <button type="button" class="admin-button primary submit" (click)="submit()" [disabled]="submitting() || !!validationError()">
            {{ submitting() ? 'Submitting…' : 'Submit screenshots' }}
          </button>
        </section>
      }
      <section class="awaiting" aria-labelledby="awaiting-title">
        <h2 id="awaiting-title">Awaiting processing</h2>
        @if (loadingAwaiting()) { <p>Loading submissions...</p> }
        @else if (!awaiting().length) { <p>No screenshot submissions are awaiting processing.</p> }
        @else {
          <div class="awaiting-list">
            @for (item of awaiting(); track item.id) {
              <article>
                <div><strong>{{ item.attachmentCount }} screenshot{{ item.attachmentCount === 1 ? '' : 's' }}</strong><span>Submitted {{ formatDate(item.createdAt) }}</span></div>
                <button type="button" class="admin-button primary" (click)="analyze(item.id)" [disabled]="analyzingId() !== null">
                  {{ analyzingId() === item.id ? 'Analyzing...' : 'Analyze' }}
                </button>
              </article>
            }
          </div>
        }
      </section>
    </div>
  `,
  styles: [`
    .screenshot-inbox{max-width:72rem}.inbox-header{margin:1.25rem 0 1.5rem}.inbox-header h1{margin:.35rem 0;font-size:clamp(2rem,4vw,3.5rem)}.inbox-header>p:last-child{max-width:42rem;color:var(--color-text-muted)}.picker{justify-items:center;gap:.5rem;padding:2rem;margin:1rem 0;border-style:dashed;text-align:center;cursor:pointer}.picker:hover{border-color:var(--color-primary)}.picker-icon{display:grid;width:2.5rem;height:2.5rem;place-items:center;border-radius:999px;color:var(--color-on-primary);background:var(--color-primary);font-size:1.5rem}.picker>span:not(.picker-icon){color:var(--color-text-muted);font-size:.84rem}.picker input{width:min(100%,24rem);margin-top:.5rem;font:inherit}.preview-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(13rem,1fr));gap:1rem}.preview-grid article{position:relative;padding:.75rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);background:var(--color-surface)}.preview-grid img{display:block;width:100%;height:14rem;object-fit:contain;background:#0f172a;border-radius:.5rem}.preview-grid article>div,.success,.awaiting-list article{display:flex;align-items:center;justify-content:space-between;gap:.75rem}.preview-grid article>div{margin-top:.6rem}.preview-grid article>div span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.order{position:absolute;z-index:1;top:1rem;left:1rem;min-width:2rem;padding:.25rem;border-radius:999px;background:#0f172a;color:#fff;text-align:center}.submit{margin-top:1rem;width:100%;min-height:3rem}.success{min-height:auto;border:1px solid #22c55e;border-radius:var(--radius-lg);padding:1rem}.awaiting{margin-top:2.5rem}.awaiting h2{margin-bottom:1rem}.awaiting-list{display:grid;gap:.75rem}.awaiting-list article{min-height:5rem;padding:1rem 1.25rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);background:var(--color-surface)}.awaiting-list article div{display:grid;gap:.3rem}.awaiting-list article span{font-size:.85rem;color:var(--color-text-muted)}@media(max-width:40rem){.screenshot-inbox{padding-inline:1rem}.picker{padding:1.5rem 1rem}.preview-grid{grid-template-columns:1fr}.preview-grid img{height:18rem}.success,.awaiting-list article{align-items:stretch;flex-direction:column}.awaiting-list .admin-button{min-height:2.75rem}}
  `],
})
export class ScreenshotInboxPageComponent implements DirtyAware, OnDestroy {
  private static readonly maxFiles = 10;
  private static readonly maxFileBytes = 10 * 1024 * 1024;
  private static readonly maxTotalBytes = 50 * 1024 * 1024;
  private readonly api = inject(JobHuntingService);
  private readonly router = inject(Router);
  readonly previews = signal<ScreenshotPreview[]>([]);
  readonly validationError = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly success = signal<ScreenshotSubmissionResult | null>(null);
  readonly submitting = signal(false);
  readonly awaiting = signal<RawJobPostingSummary[]>([]);
  readonly loadingAwaiting = signal(true);
  readonly analyzingId = signal<string | null>(null);
  private submissionId = crypto.randomUUID();

  constructor() { this.loadAwaiting(); }

  hasUnsavedChanges(): boolean { return this.previews().length > 0; }

  selectFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.clearPreviews();
    this.success.set(null);
    this.error.set(null);
    this.validationError.set(this.validate(files));
    if (!this.validationError()) {
      this.previews.set(files.map(file => ({ file, url: URL.createObjectURL(file) })));
      this.submissionId = crypto.randomUUID();
    }
  }

  remove(index: number): void {
    const items = [...this.previews()];
    const [removed] = items.splice(index, 1);
    if (removed) URL.revokeObjectURL(removed.url);
    this.previews.set(items);
    this.validationError.set(this.validate(items.map(item => item.file)));
    this.error.set(null);
  }

  submit(): void {
    if (this.submitting() || this.validationError() || !this.previews().length) return;
    this.submitting.set(true);
    this.error.set(null);
    const files = this.previews().map(item => item.file);
    this.api.submitScreenshots(this.submissionId, files).pipe(
      take(1),
      finalize(() => this.submitting.set(false)),
    ).subscribe({
      next: result => {
        this.clearPreviews();
        this.success.set(result);
        this.submissionId = crypto.randomUUID();
        this.loadAwaiting();
      },
      error: value => this.error.set(safeAdminError(value)),
    });
  }

  analyze(rawJobPostingId: string): void {
    if (this.analyzingId()) return;
    this.analyzingId.set(rawJobPostingId);
    this.error.set(null);
    this.api.analyzeRawJobPosting(rawJobPostingId).pipe(
      take(1),
      finalize(() => this.analyzingId.set(null)),
    ).subscribe({
      next: job => void this.router.navigate(['/admin/job-hunting/jobs', job.id]),
      error: value => this.error.set(backendFieldError(value, 'screenshots') ?? safeAdminError(value, 'The screenshots could not be analyzed. You can retry this submission.')),
    });
  }

  totalSizeLabel(): string {
    const bytes = this.previews().reduce((total, item) => total + item.file.size, 0);
    return `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
  }

  formatDate(value: string): string {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  }

  ngOnDestroy(): void { this.clearPreviews(); }

  private validate(files: readonly File[]): string | null {
    if (!files.length) return 'Select at least one screenshot.';
    if (files.length > ScreenshotInboxPageComponent.maxFiles) return 'Select no more than 10 screenshots.';
    if (files.some(file => !['image/jpeg', 'image/png'].includes(file.type))) return 'Only JPEG and PNG screenshots are supported.';
    if (files.some(file => file.size <= 0 || file.size > ScreenshotInboxPageComponent.maxFileBytes)) return 'Each screenshot must be between 1 byte and 10 MiB.';
    if (files.reduce((total, file) => total + file.size, 0) > ScreenshotInboxPageComponent.maxTotalBytes) return 'The selected screenshots cannot exceed 50 MiB in total.';
    return null;
  }

  private clearPreviews(): void {
    for (const preview of this.previews()) URL.revokeObjectURL(preview.url);
    this.previews.set([]);
  }

  private loadAwaiting(): void {
    this.loadingAwaiting.set(true);
    this.api.rawJobPostings({ page: 1, pageSize: 100, ingestionStatus: 'RECEIVED' }).pipe(
      take(1),
      finalize(() => this.loadingAwaiting.set(false)),
    ).subscribe({
      next: result => this.awaiting.set(result.items),
      error: value => this.error.set(safeAdminError(value, 'Awaiting submissions could not be loaded.')),
    });
  }
}
