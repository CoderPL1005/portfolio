import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { nullable, safeAdminError } from '../shared/admin-api';
import { JourneyItem, JourneyRequest, JourneySourceType, JourneyTimelineItem } from '../shared/admin.models';
import { AdminPageHeaderComponent } from '../shared/admin-page-header.component';
import { DirtyAware } from '../shared/dirty.guard';
import { JourneyAdminService } from './journey-admin.service';

@Component({
  selector: 'app-journey-admin-page',
  imports: [ReactiveFormsModule, RouterLink, AdminPageHeaderComponent],
  providers: [JourneyAdminService],
  template: `
    <div class="admin-page">
      <app-admin-page-header title="Journey" eyebrow="Portfolio timeline">
        <button class="admin-button primary" (click)="edit(null)">New milestone</button>
      </app-admin-page-header>
      <p class="manual-help">This is the same published timeline shown publicly. Generated entries are grouped and ordered by their canonical source dates; edit those dates in the source module. Add manual milestones only for events not represented elsewhere.</p>
      <div class="management">
        <ol class="admin-list">
          @for (item of items(); track item.id) {
            <li class="admin-list-item" [attr.data-source-type]="item.sourceType" [attr.data-kind]="item.timelineKind">
              <div>
                <span class="source-badge">{{ sourceLabel(item.sourceType) }}</span>
                <strong>{{ item.title }}</strong>
                <p class="timeline-range">{{ range(item) }}</p>
                @if (item.subtitle) { <p>{{ item.subtitle }}</p> }
              </div>
              <div class="admin-actions">
                @if (item.isManual) {
                  <button class="admin-button" data-action="edit" (click)="edit(item)">Edit</button>
                  @if (confirming() === item.sourceId) {
                    <button class="admin-button danger" data-action="confirm-delete" (click)="remove(item.sourceId)">Confirm</button>
                    <button class="admin-button" (click)="confirming.set(null)">Cancel</button>
                  } @else {
                    <button class="admin-button danger" data-action="delete" (click)="confirming.set(item.sourceId)">Delete</button>
                  }
                } @else {
                  <a class="admin-button" data-action="edit-source" [routerLink]="sourceRoute(item)">Edit source</a>
                }
              </div>
            </li>
          }
        </ol>
        <form class="admin-form admin-panel" [formGroup]="form" (ngSubmit)="save()">
          <h2>{{ editingId() ? 'Edit' : 'Add' }} manual milestone</h2>
          <label>Title<input formControlName="title" maxlength="255"/></label>
          <label>Subtitle<input formControlName="subtitle" maxlength="255"/></label>
          <label>Date<input type="date" formControlName="occurredAt"/></label>
          <label>Icon key<input formControlName="iconKey" maxlength="100"/></label>
          <label>Description<textarea rows="6" formControlName="description"></textarea></label>
          <label>Display order<input type="number" min="0" formControlName="displayOrder"/></label>
          <label><span><input type="checkbox" formControlName="isPublished"/> Published</span></label>
          @if (error()) { <p class="admin-error">{{ error() }}</p> }
          <div class="admin-actions">
            <button class="admin-button primary">Save</button>
            <button type="button" class="admin-button" (click)="edit(null)">Clear</button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: `
    .manual-help{max-width:55rem;margin:-1rem 0 1.5rem;color:var(--color-text-muted);line-height:1.6}
    .management{display:grid;grid-template-columns:minmax(0,1fr) minmax(20rem,.8fr);gap:1.5rem;align-items:start}
    .admin-list-item>div:first-child{display:grid;justify-items:start;gap:.25rem}
    .admin-list p{margin:0;color:var(--color-text-muted)}
    .admin-list .timeline-range{font-weight:650;color:var(--color-text)}
    .source-badge{padding:.18rem .45rem;border:1px solid var(--color-border);border-radius:999px;color:var(--color-primary);font-size:.65rem;font-weight:700;text-transform:uppercase;letter-spacing:.06em}
    @media(max-width:900px){.management{grid-template-columns:1fr}}
  `,
})
export class JourneyAdminPageComponent implements DirtyAware {
  private readonly api = inject(JourneyAdminService);
  readonly items = signal<JourneyTimelineItem[]>([]);
  readonly error = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly confirming = signal<string | null>(null);
  readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(255)] }),
    subtitle: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(255)] }),
    description: new FormControl('', { nonNullable: true }),
    occurredAt: new FormControl('', { nonNullable: true }),
    iconKey: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    displayOrder: new FormControl(0, { nonNullable: true, validators: [Validators.min(0)] }),
    isPublished: new FormControl(false, { nonNullable: true }),
  });

  constructor() { this.load(); }

  load(): void {
    this.api.timeline().pipe(take(1)).subscribe({
      next: items => this.items.set(items),
      error: error => this.error.set(safeAdminError(error)),
    });
  }

  edit(item: JourneyTimelineItem | null): void {
    if (!item) {
      this.editingId.set(null);
      this.resetForm(null);
      return;
    }
    if (!item.isManual) return;
    this.api.get(item.sourceId).pipe(take(1)).subscribe({
      next: manual => {
        this.editingId.set(manual.id);
        this.resetForm(manual);
      },
      error: error => this.error.set(safeAdminError(error)),
    });
  }

  save(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const value = this.form.getRawValue();
    const body: JourneyRequest = {
      ...value,
      subtitle: nullable(value.subtitle),
      description: nullable(value.description),
      occurredAt: nullable(value.occurredAt),
      iconKey: nullable(value.iconKey),
    };
    const id = this.editingId();
    (id ? this.api.update(id, body) : this.api.create(body)).pipe(take(1)).subscribe({
      next: () => { this.edit(null); this.load(); },
      error: error => this.error.set(safeAdminError(error)),
    });
  }

  remove(id: string): void {
    this.api.delete(id).pipe(take(1)).subscribe({
      next: () => { this.confirming.set(null); this.load(); },
      error: error => this.error.set(safeAdminError(error)),
    });
  }

  sourceRoute(item: JourneyTimelineItem): string[] {
    switch (item.sourceType) {
      case 'EXPERIENCE': return ['/admin/experience', item.sourceId];
      case 'PROJECT': return ['/admin/projects', item.sourceId];
      case 'EDUCATION': return ['/admin/education'];
      case 'TRAINING': return ['/admin/trainings'];
      case 'CERTIFICATE': return ['/admin/certificates'];
      default: return ['/admin/journey'];
    }
  }

  sourceLabel(sourceType: JourneySourceType): string {
    return sourceType.charAt(0) + sourceType.slice(1).toLowerCase();
  }

  range(item: JourneyTimelineItem): string {
    if (!item.startAt) return 'No date';
    if (item.timelineKind === 'POINT') return item.startAt;
    if (item.isOngoing) return `${item.startAt} → Present`;
    return `${item.startAt} → ${item.endAt ?? 'End date not set'}`;
  }

  hasUnsavedChanges(): boolean { return this.form.dirty; }

  private resetForm(item: JourneyItem | null): void {
    this.form.reset(item ? {
      ...item,
      subtitle: item.subtitle ?? '',
      description: item.description ?? '',
      occurredAt: item.occurredAt ?? '',
      iconKey: item.iconKey ?? '',
    } : {
      title: '', subtitle: '', description: '', occurredAt: '', iconKey: '',
      displayOrder: this.items().filter(candidate => candidate.isManual).length,
      isPublished: false,
    });
  }
}
