import { Component, OnDestroy, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize, take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { ScreenshotSubmissionResult } from './job-hunting.models';
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
      <header><p>JOB HUNTING</p><h1>Screenshot inbox</h1><p>Select every screenshot belonging to one job post, then submit them together.</p></header>
      <label class="picker">
        <strong>Select screenshots</strong>
        <span>JPEG or PNG · up to 10 files · 10 MiB each · 50 MiB total</span>
        <input type="file" accept="image/jpeg,image/png" multiple (change)="selectFiles($event)">
      </label>
      @if (validationError()) { <p class="admin-error" role="alert">{{ validationError() }}</p> }
      @if (error()) { <p class="admin-error" role="alert">{{ error() }}</p> }
      @if (success(); as result) {
        <div class="state-panel success" role="status">{{ result.attachmentCount }} screenshot{{ result.attachmentCount === 1 ? '' : 's' }} received. This job is awaiting processing.</div>
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
    </div>
  `,
  styles: [`
    .screenshot-inbox{max-width:64rem}.picker{display:grid;gap:.4rem;padding:1rem;margin:1rem 0;border:1px dashed var(--border-color,#64748b);border-radius:.75rem}.picker input{font:inherit}.preview-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(13rem,1fr));gap:1rem}.preview-grid article{position:relative;padding:.75rem;border:1px solid var(--border-color,#334155);border-radius:.75rem}.preview-grid img{display:block;width:100%;height:14rem;object-fit:contain;background:#0f172a;border-radius:.5rem}.preview-grid article>div{display:flex;align-items:center;justify-content:space-between;gap:.5rem;margin-top:.6rem}.preview-grid article>div span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.order{position:absolute;z-index:1;top:1rem;left:1rem;min-width:2rem;padding:.25rem;border-radius:999px;background:#0f172a;color:#fff;text-align:center}.submit{margin-top:1rem;width:100%;min-height:3rem}.success{border-color:#22c55e}@media(max-width:40rem){.preview-grid{grid-template-columns:1fr}.preview-grid img{height:18rem}}
  `],
})
export class ScreenshotInboxPageComponent implements DirtyAware, OnDestroy {
  private static readonly maxFiles = 10;
  private static readonly maxFileBytes = 10 * 1024 * 1024;
  private static readonly maxTotalBytes = 50 * 1024 * 1024;
  private readonly api = inject(JobHuntingService);
  readonly previews = signal<ScreenshotPreview[]>([]);
  readonly validationError = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly success = signal<ScreenshotSubmissionResult | null>(null);
  readonly submitting = signal(false);
  private submissionId = crypto.randomUUID();

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
      },
      error: value => this.error.set(safeAdminError(value)),
    });
  }

  totalSizeLabel(): string {
    const bytes = this.previews().reduce((total, item) => total + item.file.size, 0);
    return `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
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
}
