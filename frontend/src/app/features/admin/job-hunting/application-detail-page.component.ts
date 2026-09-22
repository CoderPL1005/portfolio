import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { applicationTransitions, ApplicationDetail, ApplicationStatus, EventItem } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-application-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  template: `
    <div class="admin-page application-workspace">
      <a class="back-link" routerLink="/admin/job-hunting/applications">&larr; Applications</a>
      @if (loading() && !item()) { <div class="state-panel" aria-live="polite">Loading application…</div> }
      @if (error()) { <p class="admin-error workspace-error" role="alert">{{ error() }}</p> }

      @if (item(); as application) {
        <header class="workspace-header">
          <div><p class="section-kicker">APPLICATION WORKSPACE</p><h1>{{ application.job.companyName }}</h1><p class="role-title">{{ application.job.positionTitle }}</p><p class="job-context">{{ application.job.location }}</p></div>
          <div class="header-actions"><span class="status-badge status-{{ application.status.toLowerCase() }}">{{ statusLabel(application.status) }}</span><a class="admin-button" [routerLink]="['/admin/job-hunting/jobs', application.job.id]">View original job</a></div>
        </header>

        <section class="admin-panel progress-card" aria-labelledby="progress-title">
          <div class="section-heading"><div><p class="section-kicker">LIFECYCLE</p><h2 id="progress-title">Application progress</h2></div></div>
          <ol class="progress-track" [class.has-terminal-outcome]="isTerminal(application.status)">
            @for (stage of forwardStages; track stage; let index = $index) {
              <li [class.reached]="index <= progressIndex(application)" [class.current]="stage === application.status"><span class="progress-dot" aria-hidden="true"></span><span>{{ statusLabel(stage) }}</span></li>
            }
          </ol>
          @if (isTerminal(application.status)) { <div class="terminal-outcome status-{{ application.status.toLowerCase() }}" role="status"><strong>{{ statusLabel(application.status) }}</strong><span>{{ stageDescription(application.status) }}</span></div> }
        </section>

        <section class="admin-panel stage-card" aria-labelledby="stage-title">
          <div class="stage-copy"><p class="section-kicker">CURRENT STAGE</p><h2 id="stage-title">{{ statusLabel(application.status) }}</h2><p>{{ stageDescription(application.status) }}</p></div>
          @if (transitions[application.status].length) {
            <div class="transition-workspace">
              <label>Optional transition note<input [formControl]="transitionNote" [readonly]="transitionPending() !== null" maxlength="2000" placeholder="Add context to the immutable activity history"></label>
              <div class="transition-actions">
                @for (status of transitions[application.status]; track status) {
                  <button type="button" class="admin-button" [class.primary]="!isTerminal(status)" [class.danger]="isTerminal(status)" [disabled]="transitionPending() !== null || saving() || documentPending() || removingDocumentId() !== null" (click)="transition(status)">{{ transitionPending() === status ? 'Updating…' : transitionLabel(status) }}</button>
                }
              </div>
            </div>
          } @else { <p class="terminal-copy">This is a terminal outcome. No further status transitions are available.</p> }
        </section>

        <section class="admin-panel" aria-labelledby="details-title">
          <div class="section-heading"><div><p class="section-kicker">METADATA</p><h2 id="details-title">Application details</h2></div>@if (!editingDetails()) { <button type="button" class="admin-button" [disabled]="addingDocument() || documentPending()" (click)="beginEdit()">Edit details</button> }</div>
          @if (!editingDetails()) {
            <dl class="details-grid">
              <div><dt>Channel</dt><dd>{{ application.channel ? statusLabel(application.channel) : 'Not specified' }}</dd></div>
              <div><dt>Application email</dt><dd>{{ application.applicationEmail || 'Not specified' }}</dd></div>
              <div><dt>Application URL</dt><dd>@if (application.applicationUrl) { <a [href]="application.applicationUrl" target="_blank" rel="noopener">{{ application.applicationUrl }}</a> } @else { Not specified }</dd></div>
              <div><dt>External application ID</dt><dd>{{ application.externalApplicationId || 'Not specified' }}</dd></div>
              <div><dt>Applied</dt><dd>{{ application.appliedAt ? (application.appliedAt | date:'medium') : 'Not submitted yet' }}</dd></div>
              <div><dt>Last activity</dt><dd>{{ application.lastActivityAt ? (application.lastActivityAt | date:'medium') : 'Not available' }}</dd></div>
              <div class="wide"><dt>Notes</dt><dd class="preserve-text">{{ application.notes || 'Not specified' }}</dd></div>
            </dl>
          } @else {
            <form class="admin-form details-form" [formGroup]="form" (ngSubmit)="save()"><div class="form-grid">
              <label>Channel<select formControlName="channel"><option value="">Not specified</option>@for (channel of channels; track channel) { <option [value]="channel">{{ statusLabel(channel) }}</option> }</select></label>
              <label>Application email<input type="email" formControlName="applicationEmail"></label><label>Application URL<input type="url" formControlName="applicationUrl"></label><label>External application ID<input formControlName="externalApplicationId"></label><label class="wide">Notes<textarea rows="5" formControlName="notes"></textarea></label>
            </div><div class="form-actions"><button class="admin-button primary" type="submit" [disabled]="form.invalid || saving() || transitionPending() !== null">{{ saving() ? 'Saving…' : 'Save details' }}</button><button class="admin-button" type="button" [disabled]="saving()" (click)="cancelEdit()">Cancel</button></div></form>
          }
        </section>

        <section class="admin-panel" aria-labelledby="documents-title">
          <div class="section-heading"><div><p class="section-kicker">REFERENCES</p><h2 id="documents-title">Documents</h2><p>Metadata references only. No file is uploaded.</p></div>@if (!addingDocument()) { <button type="button" class="admin-button" [disabled]="editingDetails() || saving()" (click)="beginDocument()">Add document reference</button> }</div>
          @if (!application.documents.length) { <div class="empty-section"><strong>No documents attached yet.</strong><span>Documents and application-package artifacts will appear here.</span></div> }
          @else { <div class="document-list">@for (document of application.documents; track document.id) {
            <article class="document-card" [class.removed]="document.removedAt"><div><span class="document-type">{{ statusLabel(document.documentType) }}</span><strong>{{ document.versionLabel }}</strong><p>{{ document.fileName || document.storageKey || 'Metadata reference' }}</p><small>Added {{ document.createdAt | date:'medium' }}</small></div>
            @if (!document.removedAt) { <button type="button" class="admin-button danger" [disabled]="editingDetails() || saving() || documentPending() || removingDocumentId() !== null" (click)="remove(document.id)">{{ removingDocumentId() === document.id ? 'Removing…' : 'Remove' }}</button> } @else { <span class="removed-label">Removed {{ document.removedAt | date:'medium' }}</span> }</article>
          }</div> }
          @if (addingDocument()) { <form class="admin-form document-form" [formGroup]="documentForm" (ngSubmit)="attach()"><div class="form-grid">
            <label>Document type<input formControlName="documentType" placeholder="CV"></label><label>Version label<input formControlName="versionLabel" placeholder="v1"></label><label>File name (optional)<input formControlName="fileName"></label><label>Storage key (optional)<input formControlName="storageKey"></label><label class="wide">SHA-256 hash (optional)<input formControlName="contentHash">@if (documentForm.controls.contentHash.invalid && documentForm.controls.contentHash.dirty) { <span class="field-error">Enter a 64-character hexadecimal hash.</span> }</label>
          </div><div class="form-actions"><button class="admin-button primary" [disabled]="documentForm.invalid || documentPending()">{{ documentPending() ? 'Adding…' : 'Add reference' }}</button><button type="button" class="admin-button" [disabled]="documentPending()" (click)="cancelDocument()">Cancel</button></div></form> }
        </section>

        <section class="admin-panel" aria-labelledby="activity-title">
          <div class="section-heading"><div><p class="section-kicker">IMMUTABLE HISTORY</p><h2 id="activity-title">Activity</h2></div></div>
          <ol class="activity-timeline">@for (event of application.events; track event.id) {
            <li><span class="timeline-dot" aria-hidden="true"></span><article><strong>{{ eventLabel(event) }}</strong>@if (event.fromStatus || event.toStatus) { <p>{{ event.fromStatus ? statusLabel(event.fromStatus) : 'None' }} &rarr; {{ event.toStatus ? statusLabel(event.toStatus) : 'None' }}</p> }<small>{{ event.occurredAt | date:'medium' }} · {{ statusLabel(event.actorType) }}</small>@if (event.note) { <p class="event-note">{{ event.note }}</p> }</article></li>
          } @empty { <li class="empty-section">No activity has been recorded.</li> }</ol>
        </section>
      }
    </div>
  `,
  styles: `
    .application-workspace{display:grid;gap:1.25rem;max-width:76rem}.back-link{width:max-content;color:var(--color-text-muted);text-decoration:none}.back-link:hover{color:var(--color-primary)}.workspace-error{margin:0}
    .workspace-header{display:flex;justify-content:space-between;gap:2rem;align-items:flex-start;padding:.5rem 0 1rem}.workspace-header h1{margin:.15rem 0;font-size:clamp(1.75rem,4vw,2.6rem)}.role-title{margin:.25rem 0;color:var(--color-text);font-size:1.15rem;font-weight:650}.job-context{margin:.35rem 0;color:var(--color-text-muted)}.header-actions{display:flex;align-items:center;justify-content:flex-end;gap:.75rem;flex-wrap:wrap}
    .section-kicker{margin:0 0 .35rem;color:var(--color-primary);font-size:.7rem;font-weight:750;letter-spacing:.14em}.section-heading{display:flex;justify-content:space-between;gap:1rem;align-items:flex-start;margin-bottom:1.25rem}.section-heading h2,.stage-copy h2{margin:0}.section-heading p{margin:.35rem 0 0;color:var(--color-text-muted)}
    .progress-track{display:grid;grid-template-columns:repeat(4,1fr);padding:0;margin:1.5rem 0 .25rem;list-style:none}.progress-track li{position:relative;display:grid;justify-items:center;gap:.65rem;color:var(--color-text-dim);font-size:.8rem;text-align:center}.progress-track li:not(:last-child)::after{content:'';position:absolute;z-index:0;top:.42rem;left:50%;width:100%;height:2px;background:var(--color-border)}.progress-track li.reached:not(:last-child)::after{background:color-mix(in srgb,var(--color-primary) 55%,var(--color-border))}.progress-dot{position:relative;z-index:1;width:.9rem;height:.9rem;border:2px solid var(--color-border);border-radius:50%;background:var(--color-surface)}.reached .progress-dot{border-color:var(--color-primary);background:var(--color-primary)}.progress-track li.current{color:var(--color-text);font-weight:700}.has-terminal-outcome li.current{color:var(--color-text-dim);font-weight:400}.terminal-outcome{display:flex;gap:.6rem;align-items:center;margin-top:1.25rem;padding:.85rem 1rem;border:1px solid color-mix(in srgb,#ef4444 45%,var(--color-border));border-radius:var(--radius-md);background:color-mix(in srgb,#ef4444 8%,var(--color-surface-lowest))}.terminal-outcome span{color:var(--color-text-muted)}
    .stage-card{display:grid;grid-template-columns:minmax(0,.75fr) minmax(20rem,1.25fr);gap:2rem;align-items:start}.stage-copy p:last-child,.terminal-copy{color:var(--color-text-muted);line-height:1.6}.transition-workspace{display:grid;gap:1rem}.transition-workspace label{display:grid;gap:.45rem;color:var(--color-text-muted);font-size:.78rem}.transition-actions,.form-actions{display:flex;gap:.65rem;flex-wrap:wrap}.transition-actions .danger{margin-left:auto}
    .details-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem;margin:0}.details-grid>div{min-width:0;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);background:var(--color-surface-lowest)}.details-grid .wide{grid-column:1/-1}.details-grid dt{color:var(--color-text-dim);font-size:.7rem;font-weight:700;letter-spacing:.06em;text-transform:uppercase}.details-grid dd{margin:.4rem 0 0;overflow-wrap:anywhere;color:var(--color-text)}.details-grid a{color:var(--color-primary)}.preserve-text{white-space:pre-wrap}.details-form,.document-form{display:grid;gap:1rem}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.form-grid .wide{grid-column:1/-1}
    .empty-section{display:grid;gap:.35rem;padding:1.25rem;border:1px dashed var(--color-border);border-radius:var(--radius-lg);color:var(--color-text-muted);background:var(--color-surface-lowest)}.empty-section strong{color:var(--color-text)}.document-list{display:grid;gap:.75rem}.document-card{display:flex;justify-content:space-between;align-items:center;gap:1rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);background:var(--color-surface-lowest)}.document-card>div{display:grid;gap:.25rem;min-width:0}.document-card p,.document-card small{margin:0;color:var(--color-text-muted);overflow-wrap:anywhere}.document-card.removed{opacity:.62}.document-type{color:var(--color-primary);font-size:.7rem;font-weight:700;letter-spacing:.06em}.removed-label{color:var(--color-text-dim);font-size:.75rem}.document-form{margin-top:1rem;padding-top:1rem;border-top:1px solid var(--color-border)}
    .activity-timeline{display:grid;gap:0;margin:0;padding:0;list-style:none}.activity-timeline li{position:relative;display:grid;grid-template-columns:1rem 1fr;gap:.8rem;padding-bottom:1.25rem}.activity-timeline li:not(:last-child)::before{content:'';position:absolute;top:.8rem;bottom:0;left:.34rem;width:1px;background:var(--color-border)}.timeline-dot{position:relative;z-index:1;width:.7rem;height:.7rem;margin-top:.25rem;border-radius:50%;background:var(--color-primary)}.activity-timeline article{display:grid;gap:.3rem}.activity-timeline p,.activity-timeline small{margin:0;color:var(--color-text-muted)}.activity-timeline .event-note{margin-top:.35rem;padding:.65rem;border-left:2px solid var(--color-border);white-space:pre-wrap;color:var(--color-text)}
    @media(max-width:760px){.workspace-header,.section-heading{align-items:stretch;flex-direction:column}.header-actions{justify-content:flex-start}.stage-card,.details-grid,.form-grid{grid-template-columns:1fr}.details-grid .wide,.form-grid .wide{grid-column:auto}.transition-actions{display:grid}.transition-actions .danger{margin-left:0}.document-card{align-items:flex-start;flex-direction:column}.document-card .admin-button{width:100%}.progress-track li{font-size:.7rem}.admin-panel{padding:1rem}}
    @media(max-width:430px){.header-actions,.header-actions .admin-button,.transition-actions .admin-button,.form-actions,.form-actions .admin-button{width:100%}.progress-track{margin-inline:-.35rem}.terminal-outcome{align-items:flex-start;flex-direction:column}}
  `,
})
export class ApplicationDetailPageComponent implements DirtyAware {
  private readonly api = inject(JobHuntingService); private readonly fb = inject(FormBuilder);
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly item = signal<ApplicationDetail | null>(null); readonly loading = signal(true); readonly saving = signal(false); readonly error = signal<string | null>(null);
  readonly editingDetails = signal(false); readonly addingDocument = signal(false); readonly transitionPending = signal<ApplicationStatus | null>(null); readonly documentPending = signal(false); readonly removingDocumentId = signal<string | null>(null);
  readonly transitions = applicationTransitions; readonly forwardStages: ApplicationStatus[] = ['DRAFT','APPLIED','INTERVIEW','OFFER']; readonly channels = ['EMAIL','PLATFORM','MANUAL','OTHER']; readonly transitionNote = this.fb.control('');
  readonly form = this.fb.group({channel:[''],applicationEmail:[''],applicationUrl:[''],externalApplicationId:[''],notes:['']});
  readonly documentForm = this.fb.group({documentType:['',Validators.required],versionLabel:['',Validators.required],fileName:[''],storageKey:[''],contentHash:['',Validators.pattern(/^[0-9a-fA-F]{64}$/)]});
  constructor() { this.load(); }
  hasUnsavedChanges() { return this.form.dirty || this.documentForm.dirty; }
  load(preserveError=false) { this.loading.set(true);this.api.application(this.id).pipe(take(1)).subscribe({next:application=>{this.item.set(application);this.syncForm(application);this.loading.set(false);if(!preserveError)this.error.set(null)},error:error=>{this.error.set(safeAdminError(error));this.loading.set(false)}}); }
  beginEdit() { if(!this.saving()&&!this.addingDocument()&&!this.documentPending())this.editingDetails.set(true); }
  cancelEdit() { const current=this.item();if(current)this.syncForm(current);this.editingDetails.set(false); }
  save() { const current=this.item();if(!current||!this.editingDetails()||this.form.invalid||this.saving()||this.transitionPending())return;this.saving.set(true);this.error.set(null);this.api.updateApplication(this.id,{expectedVersion:current.version,...this.form.getRawValue()}).pipe(take(1)).subscribe({next:application=>{this.item.set(application);this.syncForm(application);this.editingDetails.set(false);this.saving.set(false)},error:error=>this.failed(error)}); }
  transition(status:ApplicationStatus) { const current=this.item();if(!current||this.saving()||this.documentPending()||this.removingDocumentId()||this.transitionPending()||!this.transitions[current.status].includes(status)||!confirm(`Change status to ${this.statusLabel(status)}?`))return;this.transitionPending.set(status);this.error.set(null);this.api.transition(this.id,{status,expectedVersion:current.version,note:this.transitionNote.value||null}).pipe(take(1)).subscribe({next:application=>{const preserveDraft=this.editingDetails()&&this.form.dirty;this.item.set(application);if(!preserveDraft)this.syncForm(application);this.transitionNote.reset();this.transitionPending.set(null)},error:error=>this.failed(error)}); }
  beginDocument() { if(!this.documentPending()&&!this.editingDetails()&&!this.saving())this.addingDocument.set(true); }
  cancelDocument() { this.documentForm.reset();this.documentForm.markAsPristine();this.addingDocument.set(false); }
  attach() { if(!this.addingDocument()||this.documentForm.invalid||this.documentPending())return;this.documentPending.set(true);this.error.set(null);this.api.attach(this.id,{...this.documentForm.getRawValue(),metadata:null}).pipe(take(1)).subscribe({next:()=>{this.documentPending.set(false);this.cancelDocument();this.load()},error:error=>this.failed(error)}); }
  remove(id:string) { if(this.editingDetails()||this.saving()||this.removingDocumentId()||this.documentPending()||!confirm('Soft-remove this document metadata?'))return;this.removingDocumentId.set(id);this.error.set(null);this.api.remove(this.id,id).pipe(take(1)).subscribe({next:()=>{this.removingDocumentId.set(null);this.load()},error:error=>this.failed(error)}); }
  isTerminal(status:ApplicationStatus) { return status==='REJECTED'||status==='WITHDRAWN'; }
  progressIndex(application:ApplicationDetail) { if(!this.isTerminal(application.status))return this.forwardStages.indexOf(application.status);const source=application.events.find(event=>event.toStatus===application.status)?.fromStatus as ApplicationStatus|null|undefined;return Math.max(0,source?this.forwardStages.indexOf(source):-1); }
  statusLabel(value:string) { return value.toLowerCase().replaceAll('_',' ').replace(/\b\w/g,letter=>letter.toUpperCase()); }
  transitionLabel(status:ApplicationStatus) { return ({APPLIED:'Mark as applied',INTERVIEW:'Move to interview',REJECTED:'Reject',OFFER:'Mark offer received',WITHDRAWN:'Withdraw'} as Partial<Record<ApplicationStatus,string>>)[status]??this.statusLabel(status); }
  stageDescription(status:ApplicationStatus) { return ({DRAFT:'This application has not been submitted yet.',APPLIED:'The application has been submitted and is awaiting a response.',INTERVIEW:'The application is progressing through interviews.',OFFER:'An offer has been received for this application.',REJECTED:'The employer ended this application process.',WITHDRAWN:'This application was withdrawn and is no longer active.'} as Record<ApplicationStatus,string>)[status]; }
  eventLabel(event:EventItem) { return event.eventType==='CREATED'?'Application created':event.eventType==='STATUS_CHANGED'?'Status changed':this.statusLabel(event.eventType); }
  private syncForm(application:ApplicationDetail) { this.form.reset({channel:application.channel||'',applicationEmail:application.applicationEmail||'',applicationUrl:application.applicationUrl||'',externalApplicationId:application.externalApplicationId||'',notes:application.notes||''},{emitEvent:false});this.form.markAsPristine(); }
  private failed(error:unknown) { this.error.set(safeAdminError(error));this.saving.set(false);this.transitionPending.set(null);this.documentPending.set(false);this.removingDocumentId.set(null);if((error as {status?:number})?.status===409)this.load(true); }
}
