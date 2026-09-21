import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { JobCreate, JobDetail, JobFitAnalysis, JobSource, JobWrite } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-job-edit',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="admin-page job-editor">
      <a class="back-link" routerLink="/admin/job-hunting/jobs">&larr; Jobs</a>
      @if (loading()) {
        <div class="state-panel">Loading job...</div>
      } @else {
        <header class="page-header">
          <div>
            <p class="eyebrow">{{ isNew ? 'MANUAL JOB' : 'JOB DETAIL' }}</p>
            <h1>{{ isNew ? 'New manual job' : (job()?.positionTitle || 'Job') }}</h1>
            @if (job(); as current) {
              <p class="company">{{ current.companyName }}</p>
              <p class="metadata">{{ current.location }}@if (current.employmentType) { <span aria-hidden="true"> &middot; </span>{{ current.employmentType }} }@if (current.workplaceType) { <span aria-hidden="true"> &middot; </span>{{ current.workplaceType }} }</p>
            }
          </div>
          @if (job(); as current) {
            <div class="header-statuses" aria-label="Job states">
              <span class="status-badge"><span>Verification</span>{{ statusLabel(current.verificationStatus) }}</span>
              <span class="status-badge"><span>Selection</span>{{ statusLabel(current.selectionStatus) }}</span>
              @if (current.archivedAt) { <span class="status-badge archived">Archived</span> }
            </div>
          }
        </header>
        @if (error()) { <p class="admin-error page-error" role="alert">{{ error() }}</p> }

        <form class="admin-form" [formGroup]="form" (ngSubmit)="save()">
          @if (isNew) {
            <section class="admin-panel form-card" [formGroup]="sourceForm" aria-labelledby="manual-source-title">
              <div class="section-heading"><div><p class="section-kicker">INPUT</p><h2 id="manual-source-title">Manual source</h2></div><p>Record the original source before creating this job.</p></div>
              <div class="form-grid">
                <label>Source<select formControlName="source">@for (source of sources; track source) { <option [value]="source">{{ statusLabel(source) }}</option> }</select></label>
                <label>Source URL<input type="url" formControlName="sourceUrl"></label>
                <label>External ID<input formControlName="sourceExternalId"></label>
                <label class="wide">Raw content<textarea class="raw-textarea" formControlName="rawContent"></textarea>
                  @if (sourceForm.controls.rawContent.touched && sourceForm.controls.rawContent.invalid) { <span class="field-error">Raw content is required.</span> }
                </label>
              </div>
            </section>
          }

          <section class="admin-panel form-card" aria-labelledby="basics-title">
            <div class="section-heading"><div><p class="section-kicker">OVERVIEW</p><h2 id="basics-title">Job basics</h2></div></div>
            <div class="form-grid">
              <label>Company<input formControlName="companyName">@if (form.controls.companyName.touched && form.controls.companyName.invalid) { <span class="field-error">Company is required.</span> }</label>
              <label>Position<input formControlName="positionTitle">@if (form.controls.positionTitle.touched && form.controls.positionTitle.invalid) { <span class="field-error">Position is required.</span> }</label>
              <label>Location<input formControlName="location">@if (form.controls.location.touched && form.controls.location.invalid) { <span class="field-error">Location is required.</span> }</label>
              <label>Employment type<input formControlName="employmentType"></label>
              <label>Workplace type<input formControlName="workplaceType"></label>
            </div>
          </section>

          <section class="admin-panel form-card" aria-labelledby="compensation-title">
            <div class="section-heading"><div><p class="section-kicker">PACKAGE</p><h2 id="compensation-title">Compensation &amp; requirements</h2></div></div>
            <div class="salary-grid">
              <label>Minimum salary<input type="number" formControlName="salaryMinimum"></label>
              <label>Maximum salary<input type="number" formControlName="salaryMaximum"></label>
              <label>Currency<input formControlName="salaryCurrency" placeholder="e.g. VND"></label>
              <label>Period<input formControlName="salaryPeriod" placeholder="e.g. MONTHLY"></label>
            </div>
            <label>Experience requirements<textarea class="medium-textarea" formControlName="experienceRequirements"></textarea></label>
          </section>

          <section class="admin-panel form-card" aria-labelledby="description-title">
            <div class="section-heading"><div><p class="section-kicker">EXTRACTED JD</p><h2 id="description-title">Job description</h2></div><p>Review the extracted content before saving changes.</p></div>
            <label>Description<textarea class="description-textarea" formControlName="description"></textarea>@if (form.controls.description.touched && form.controls.description.invalid) { <span class="field-error">Description is required.</span> }</label>
          </section>

          <section class="admin-panel form-card" aria-labelledby="technology-title">
            <div class="section-heading"><div><p class="section-kicker">SKILLS</p><h2 id="technology-title">Technology stack</h2></div><p>Separate technologies with commas or new lines.</p></div>
            <label>Technologies<textarea class="technology-textarea" formControlName="technologyStack" placeholder="Java, Spring Boot, RESTful API"></textarea></label>
            @if (technologyPreview().length) { <ul class="chip-list technology-preview" aria-label="Technology preview">@for (technology of technologyPreview(); track technology) { <li>{{ technology }}</li> }</ul> }
          </section>

          <section class="admin-panel form-card" aria-labelledby="application-title">
            <div class="section-heading"><div><p class="section-kicker">CONTACT</p><h2 id="application-title">Application</h2></div></div>
            <div class="form-grid">
              <label>Application email<input type="email" formControlName="applicationEmail"></label>
              <label>Application URL<input type="url" formControlName="applicationUrl"></label>
              <label>Expires at<input type="date" formControlName="expiresAt"></label>
              <label class="wide">Notes<textarea class="medium-textarea" formControlName="notes"></textarea></label>
            </div>
          </section>

          <div class="save-bar">
            <span>{{ form.dirty || (isNew && sourceForm.dirty) ? 'Unsaved changes' : 'All changes saved' }}</span>
            <button type="submit" class="admin-button primary save-button" [disabled]="form.invalid || (isNew && sourceForm.invalid) || saving()">{{ saving() ? 'Saving...' : 'Save job' }}</button>
          </div>
        </form>

        @if (job(); as current) {
          <section class="admin-panel detail-card fit-card" aria-labelledby="fit-title">
            <div class="section-heading"><div><p class="section-kicker">DECISION SUPPORT</p><h2 id="fit-title">Candidate fit</h2></div><button type="button" class="admin-button primary" (click)="analyzeFit()" [disabled]="fitLoading()">{{ fitLoading() ? 'Analyzing...' : 'Analyze fit' }}</button></div>
            <p class="fit-disclaimer">A deterministic comparison of published portfolio evidence and your private preferences—not a probability of being hired.</p>
            @if (fitAnalysis(); as analysis) {
              <div class="fit-summary"><div><span>Fit score</span><strong>{{ analysis.overallScore === null ? 'Unknown' : analysis.overallScore + ' / 100' }}</strong></div><div><span>Evidence coverage</span><strong>{{ analysis.coveragePercent }}%</strong></div><div><span>Recommendation</span><strong class="recommendation" [attr.data-recommendation]="analysis.recommendation">{{ statusLabel(analysis.recommendation) }}</strong></div></div>
              <div class="recommendation-detail">
                @if (analysis.reasons.length) { <section><h3>Why this recommendation</h3><ul>@for (reason of analysis.reasons; track reason) { <li><span aria-hidden="true">&#10003;</span>{{ reason }}</li> }</ul></section> }
                @if (analysis.concerns.length) { <section><h3>Needs review</h3><ul>@for (concern of analysis.concerns; track concern) { <li><span aria-hidden="true">?</span>{{ concern }}</li> }</ul></section> }
              </div>
              <div class="fit-components">@for (component of analysis.components; track component.key) { <article><header><strong>{{ component.label }}</strong><span class="fit-status" [attr.data-status]="component.status">{{ statusLabel(component.status) }}</span></header><p>{{ component.score === null ? 'Not scored' : component.score + ' / 100' }} · weight {{ component.configuredWeight }}</p><p>{{ component.explanation }}</p>@if(component.evidence.length){<ul>@for(item of component.evidence;track item){<li>{{item}}</li>}</ul>}</article> }</div>
              <div class="fit-lists">@if(analysis.matchedTechnologies.length){<p><strong>Matched:</strong> {{analysis.matchedTechnologies.join(', ')}}</p>}@if(analysis.developingTechnologies.length){<p><strong>Developing:</strong> {{analysis.developingTechnologies.join(', ')}}</p>}@if(analysis.missingTechnologies.length){<p><strong>Missing:</strong> {{analysis.missingTechnologies.join(', ')}}</p>}@if(analysis.unknownFactors.length){<p><strong>Unknown factors:</strong> {{analysis.unknownFactors.join(', ')}}</p>}</div>
              @if (canDecide(current.selectionStatus)) {
                <div class="decision-actions"><p>Approval means this job may continue into the application workflow; it does not submit an application.</p><div><button type="button" class="admin-button primary" (click)="select('APPROVED')" [disabled]="selectionAction() !== null">{{ selectionAction() === 'APPROVED' ? 'Approving...' : 'Approve' }}</button><button type="button" class="admin-button danger" (click)="select('SKIPPED')" [disabled]="selectionAction() !== null">{{ selectionAction() === 'SKIPPED' ? 'Skipping...' : 'Skip' }}</button></div></div>
              } @else { <p class="decision-complete">Human decision: <strong>{{ statusLabel(current.selectionStatus) }}</strong></p> }
            } @else { <p class="empty-copy">Fit analysis runs only when you choose Analyze fit.</p> }
          </section>

          <section class="admin-panel detail-card" aria-labelledby="state-title">
            <div class="section-heading"><div><p class="section-kicker">WORKFLOW</p><h2 id="state-title">State</h2></div><p>Stored values remain unchanged; labels are formatted for readability.</p></div>
            <div class="state-grid">
              <label>Verification status<select class="admin-input" [value]="current.verificationStatus" (change)="verify(selectValue($event))">@for (status of verifications; track status) { <option [value]="status">{{ statusLabel(status) }}</option> }</select></label>
               <div class="archive-state"><span>Selection status</span><strong>{{ statusLabel(current.selectionStatus) }}</strong></div>
              <div class="archive-state"><span>Archive state</span><strong>{{ current.archivedAt ? 'Archived' : 'Active' }}</strong></div>
            </div>
            @if (!current.archivedAt) { <button type="button" class="admin-button danger archive-button" (click)="archive()">Archive job</button> }
          </section>

          <section class="admin-panel detail-card" aria-labelledby="sources-title">
            <div class="section-heading"><div><p class="section-kicker">READ ONLY</p><h2 id="sources-title">Sources</h2></div><p>Original source information used to create this job.</p></div>
            <div class="source-list">
              @for (source of current.sources; track source.id) {
                <article class="source-card"><div><span>Source</span><strong>{{ statusLabel(source.source) }}</strong></div>@if (source.sourceUrl) { <a class="text-link" [href]="source.sourceUrl" target="_blank" rel="noopener noreferrer">Open source</a> }<div class="raw-content"><span>Raw content</span><pre>{{ source.rawContent }}</pre></div></article>
              } @empty { <p class="empty-copy">No raw sources.</p> }
            </div>
          </section>

          <section class="admin-panel detail-card" aria-labelledby="applications-title">
            <div class="section-heading"><div><p class="section-kicker">TRACKING</p><h2 id="applications-title">Applications</h2></div>
              @if (current.applications.length) { <a class="admin-button primary" [routerLink]="['/admin/job-hunting/applications', current.applications[0].id]">Open application</a> }
              @else if (current.selectionStatus === 'APPROVED' && !current.archivedAt) { <button type="button" class="admin-button primary" (click)="createApplication()" [disabled]="applicationCreating()">{{ applicationCreating() ? 'Creating...' : 'Create application' }}</button> }
            </div>
            <div class="application-list">
              @for (application of current.applications; track application.id) { <a class="application-row" [routerLink]="['/admin/job-hunting/applications', application.id]"><span><strong>{{ statusLabel(application.status) }}</strong><small>{{ application.channel ? statusLabel(application.channel) : 'No channel' }}</small></span><span aria-hidden="true">&rarr;</span></a> }
              @empty { <div class="empty-copy"><p>No applications yet.</p><p>Create one when you are ready to track an application for this role.</p></div> }
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: [`
    .job-editor{display:grid;gap:1.25rem;max-width:90rem}.back-link{width:max-content;color:var(--color-text-muted);font-weight:700;text-decoration:none}.back-link:hover{color:var(--color-primary)}
    .page-header{display:flex;align-items:flex-end;justify-content:space-between;gap:2rem;padding:1.25rem 0 .5rem}.page-header h1{max-width:54rem;margin:.35rem 0 0;font-size:clamp(2rem,4vw,3.6rem);line-height:1.05;overflow-wrap:anywhere}.company{margin:.65rem 0 .2rem;color:var(--color-text);font:700 1.15rem var(--font-heading)}.metadata{margin:0;color:var(--color-text-muted)}
    .header-statuses{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:.55rem}.header-statuses .status-badge{display:grid;gap:.18rem;padding:.5rem .75rem;font-size:.78rem}.header-statuses .status-badge span{color:var(--color-text-dim);font-size:.6rem;letter-spacing:.08em;text-transform:uppercase}.status-badge.archived{color:var(--color-error)}
    .page-error{margin:0;padding:1rem;border:1px solid color-mix(in srgb,var(--color-error) 45%,var(--color-border));border-radius:var(--radius-md);background:color-mix(in srgb,var(--color-error) 8%,transparent)}.admin-form{gap:1.25rem}.form-card,.detail-card{display:grid;gap:1.25rem}.section-heading{align-items:flex-start;margin:0}.section-heading h2{margin:.15rem 0 0;font-size:1.35rem}.section-heading>p{max-width:32rem;margin:0;color:var(--color-text-muted);font-size:.84rem}.section-kicker{margin:0;color:var(--color-primary);font-size:.62rem;font-weight:800;letter-spacing:.14em}
    .form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.form-grid .wide{grid-column:1/-1}.salary-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:1rem}.description-textarea{min-height:22rem;line-height:1.65}.medium-textarea{min-height:8rem;line-height:1.55}.technology-textarea{min-height:6rem}.raw-textarea{min-height:12rem}.technology-preview{margin:.1rem 0 0}.technology-preview li{color:var(--color-text);background:var(--color-surface-lowest)}
    .save-bar{position:sticky;bottom:1rem;z-index:5;display:flex;align-items:center;justify-content:space-between;gap:1rem;padding:.8rem 1rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);background:color-mix(in srgb,var(--color-surface-lowest) 92%,transparent);box-shadow:0 .8rem 2.5rem #0005;backdrop-filter:blur(12px)}.save-bar span{color:var(--color-text-muted);font-size:.8rem}.save-button{min-width:8.5rem;min-height:2.75rem}
    .state-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem}.state-grid label,.archive-state{display:grid;gap:.45rem;color:var(--color-text-muted);font-size:.82rem;font-weight:650}.archive-state strong{display:flex;align-items:center;min-height:3rem;padding:.85rem 1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);color:var(--color-text);background:var(--color-surface-lowest)}.archive-button{width:max-content}
    .fit-disclaimer{margin:0;color:var(--color-text-muted)}.fit-summary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem}.fit-summary div{display:grid;gap:.25rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md)}.fit-summary span{color:var(--color-text-muted);font-size:.75rem}.fit-summary strong{font-size:1.5rem}.recommendation[data-recommendation="RECOMMENDED"]{color:var(--color-success)}.recommendation[data-recommendation="NEEDS_REVIEW"]{color:var(--color-warning)}.recommendation[data-recommendation="NOT_RECOMMENDED"]{color:var(--color-error)}.recommendation-detail{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.recommendation-detail section{padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);background:var(--color-surface-lowest)}.recommendation-detail ul{display:grid;gap:.5rem;margin:0;padding:0;list-style:none}.recommendation-detail li{display:flex;gap:.55rem;color:var(--color-text-muted);font-size:.85rem}.recommendation-detail li span{color:var(--color-primary);font-weight:800}.fit-components{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.75rem}.fit-components article{padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);background:var(--color-surface-lowest)}.fit-components header{display:flex;justify-content:space-between;gap:1rem}.fit-components p,.fit-components li,.fit-lists{color:var(--color-text-muted);font-size:.82rem}.fit-status{font-weight:800}.fit-status[data-status="MATCH"]{color:var(--color-success)}.fit-status[data-status="PARTIAL"]{color:var(--color-warning)}.fit-status[data-status="MISMATCH"]{color:var(--color-error)}.fit-status[data-status="UNKNOWN"]{color:var(--color-text-dim)}.fit-lists{display:grid;gap:.35rem}.fit-lists p{margin:0}.decision-actions{display:flex;align-items:center;justify-content:space-between;gap:1rem;padding-top:1rem;border-top:1px solid var(--color-border)}.decision-actions p{margin:0;color:var(--color-text-muted)}.decision-actions>div{display:flex;gap:.75rem}
    .source-list,.application-list{display:grid;gap:.75rem}.source-card{display:grid;grid-template-columns:1fr auto;gap:1rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);background:var(--color-surface-lowest)}.source-card>div{display:grid;gap:.25rem}.source-card span,.application-row small{color:var(--color-text-dim);font-size:.72rem}.raw-content{grid-column:1/-1}.raw-content pre{max-height:22rem;margin:.25rem 0 0;padding:1rem;overflow:auto;border-radius:var(--radius-md);white-space:pre-wrap;overflow-wrap:anywhere;color:var(--color-text-muted);background:var(--color-background);font:inherit;font-size:.82rem;line-height:1.55}.application-row{display:flex;align-items:center;justify-content:space-between;gap:1rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);color:var(--color-text);text-decoration:none;background:var(--color-surface-lowest)}.application-row:hover{border-color:var(--color-primary)}.application-row>span:first-child{display:grid;gap:.25rem}.empty-copy{margin:0;color:var(--color-text-muted)}.empty-copy p{margin:.2rem 0}
    @media(max-width:900px){.salary-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.state-grid{grid-template-columns:1fr 1fr}.archive-state{grid-column:1/-1}}
    @media(max-width:640px){.job-editor{padding-inline:1rem}.page-header{align-items:flex-start;flex-direction:column}.header-statuses{justify-content:flex-start}.form-grid,.salary-grid,.state-grid,.fit-summary,.fit-components,.recommendation-detail{grid-template-columns:1fr}.form-grid .wide,.archive-state{grid-column:auto}.section-heading{gap:.5rem}.save-bar{bottom:.5rem}.save-bar span{display:none}.save-button{width:100%}.source-card{grid-template-columns:1fr}.raw-content{grid-column:auto}.description-textarea{min-height:18rem}.admin-panel{padding:1rem}.application-row{min-height:3.25rem}.decision-actions{align-items:stretch;flex-direction:column}.decision-actions>div,.decision-actions button{width:100%}}
  `],
})
export class JobEditPageComponent implements DirtyAware {
  private fb = inject(FormBuilder);
  private api = inject(JobHuntingService);
  private router = inject(Router);
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly isNew = !this.id;
  readonly job = signal<JobDetail | null>(null);
  readonly loading = signal(!this.isNew);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  readonly fitLoading = signal(false);
  readonly selectionAction = signal<string | null>(null);
  readonly applicationCreating = signal(false);
  readonly fitAnalysis = signal<JobFitAnalysis | null>(null);
  readonly sources: JobSource[] = ['MANUAL', 'TOPCV', 'VIETNAMWORKS', 'COMPANY_SITE', 'FACEBOOK', 'INSTAGRAM', 'OTHER'];
  readonly verifications = ['PENDING', 'VERIFIED', 'UNVERIFIED', 'LIKELY_EXPIRED'];
  readonly sourceForm = this.fb.group({ source: this.fb.nonNullable.control<JobSource>('MANUAL'), sourceUrl: [''], sourceExternalId: [''], rawContent: ['', Validators.required] });
  readonly form = this.fb.group({ companyName: ['', Validators.required], positionTitle: ['', Validators.required], location: ['', Validators.required], employmentType: [''], workplaceType: [''], salaryMinimum: [null as number | null], salaryMaximum: [null as number | null], salaryCurrency: [''], salaryPeriod: [''], experienceRequirements: [''], description: ['', Validators.required], technologyStack: [''], applicationEmail: [''], applicationUrl: [''], expiresAt: [''], notes: [''] });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.fitAnalysis.set(null));
    if (this.id) this.load();
  }
  hasUnsavedChanges(): boolean { return this.form.dirty || (this.isNew && this.sourceForm.dirty); }
  statusLabel(value: string): string {
    const known: Record<string, string> = { TOPCV: 'TopCV', VIETNAMWORKS: 'VietnamWorks', COMPANY_SITE: 'Company site' };
    if (known[value]) return known[value];
    const label = value.replaceAll('_', ' ').toLowerCase();
    return label.charAt(0).toUpperCase() + label.slice(1);
  }
  selectValue(event: Event): string { return (event.target as HTMLSelectElement).value; }
  technologyPreview(): string[] { return (this.form.controls.technologyStack.value || '').split(/[\n,]/).map(value => value.trim()).filter(Boolean); }

  load(preserveError = false): void {
    this.loading.set(true);
    this.api.job(this.id!).pipe(take(1)).subscribe({
      next: current => {
        const analyzedVersion = this.fitAnalysis()?.jobVersion;
        this.job.set(current);
        this.form.reset({ ...current, technologyStack: current.technologyStack.join(', '), employmentType: current.employmentType || '', workplaceType: current.workplaceType || '', salaryCurrency: current.salaryCurrency || '', salaryPeriod: current.salaryPeriod || '', experienceRequirements: current.experienceRequirements || '', applicationEmail: current.applicationEmail || '', applicationUrl: current.applicationUrl || '', expiresAt: current.expiresAt?.slice(0, 10) || '', notes: current.notes || '' }, { emitEvent: false });
        if (analyzedVersion !== undefined && analyzedVersion !== current.version) this.fitAnalysis.set(null);
        this.loading.set(false);
        if (!preserveError) this.error.set(null);
      },
      error: value => { this.error.set(safeAdminError(value)); this.loading.set(false); },
    });
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid || (this.isNew && this.sourceForm.invalid)) { this.form.markAllAsTouched(); this.sourceForm.markAllAsTouched(); return; }
    this.saving.set(true);
    this.error.set(null);
    const write = this.jobWrite();
    const operation = this.isNew ? this.api.createJob({ ...write, ...this.createSource() }) : this.api.updateJob(this.id!, { ...write, expectedVersion: this.job()!.version });
    operation.pipe(take(1)).subscribe({
      next: current => { this.form.markAsPristine(); this.sourceForm.markAsPristine(); this.saving.set(false); if (this.isNew) void this.router.navigate(['/admin/job-hunting/jobs', current.id]); else { this.job.set(current); if (this.fitAnalysis()?.jobVersion !== current.version) this.fitAnalysis.set(null); } },
      error: value => { this.error.set(safeAdminError(value)); this.saving.set(false); if ((value as { status?: number })?.status === 409) this.load(true); },
    });
  }

  verify(status: string): void { if (status === this.job()!.verificationStatus) return; this.api.verification(this.id!, status, this.job()!.version).pipe(take(1)).subscribe({ next: value => this.replace(value), error: value => this.failed(value) }); }
  canDecide(status: string): boolean { return status === 'PENDING_ANALYSIS' || status === 'RECOMMENDED'; }
  select(status: 'APPROVED' | 'SKIPPED'): void { const current=this.job();if(!current||this.selectionAction()!==null||!this.canDecide(current.selectionStatus))return;this.selectionAction.set(status);this.error.set(null);this.api.selection(this.id!,status,current.version).pipe(take(1)).subscribe({next:value=>{this.selectionAction.set(null);this.replace(value)},error:value=>{this.selectionAction.set(null);this.failed(value)}}); }
  archive(): void { if (confirm('Archive this job?')) this.api.archive(this.id!, this.job()!.version).pipe(take(1)).subscribe({ next: value => this.replace(value), error: value => this.failed(value) }); }
  createApplication(): void { const current=this.job();if(!current||this.applicationCreating()||current.selectionStatus!=='APPROVED'||current.archivedAt!==null||current.applications.length)return;this.applicationCreating.set(true);this.error.set(null);this.api.createApplication({jobPostingId:current.id,expectedJobVersion:current.version}).pipe(take(1)).subscribe({next:value=>{this.applicationCreating.set(false);void this.router.navigate(['/admin/job-hunting/applications',value.id])},error:value=>{this.applicationCreating.set(false);this.error.set(safeAdminError(value))}}); }
  analyzeFit(): void { if(this.fitLoading()||!this.id)return;this.fitLoading.set(true);this.error.set(null);this.api.analyzeFit(this.id).pipe(take(1)).subscribe({next:value=>{this.fitAnalysis.set(value);this.fitLoading.set(false)},error:value=>{this.error.set(safeAdminError(value));this.fitLoading.set(false)}}); }

  private jobWrite(): JobWrite {
    const value = this.form.getRawValue();
    return { companyName: value.companyName ?? '', positionTitle: value.positionTitle ?? '', location: value.location ?? '', employmentType: value.employmentType || null, workplaceType: value.workplaceType || null, salaryMinimum: value.salaryMinimum, salaryMaximum: value.salaryMaximum, salaryCurrency: value.salaryCurrency || null, salaryPeriod: value.salaryPeriod || null, experienceRequirements: value.experienceRequirements || null, description: value.description ?? '', technologyStack: (value.technologyStack || '').split(/[\n,]/).map(item => item.trim()).filter(Boolean), applicationEmail: value.applicationEmail || null, applicationUrl: value.applicationUrl || null, expiresAt: value.expiresAt || null, notes: value.notes || null };
  }
  private createSource(): Pick<JobCreate, 'source' | 'sourceExternalId' | 'sourceUrl' | 'rawContent'> { const value = this.sourceForm.getRawValue(); return { source: value.source, sourceExternalId: value.sourceExternalId || null, sourceUrl: value.sourceUrl || null, rawContent: value.rawContent ?? '' }; }
  private replace(value: JobDetail): void { this.job.set(value); this.error.set(null); }
  private failed(value: unknown): void { this.error.set(safeAdminError(value)); if ((value as { status?: number })?.status === 409) this.load(true); }
}
