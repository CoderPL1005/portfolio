import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { applicationTransitions, ApplicationDetail, ApplicationStatus } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-application-detail', imports: [ReactiveFormsModule, RouterLink],
  template: `<div class="admin-page"><a routerLink="/admin/job-hunting/applications">← Applications</a>
  @if(loading()){<div class="state-panel">Loading application…</div>}@else if(error()){<p class="admin-error">{{error()}}</p>}
  @if(item();as x){<h1>{{x.job.companyName}} — {{x.job.positionTitle}}</h1><p><span class="status-badge">{{x.status}}</span> · {{x.job.location}}</p>
  <section><h2>Application metadata</h2><form [formGroup]="form" (ngSubmit)="save()"><label>Channel<select formControlName="channel"><option value="">None</option>@for(c of channels;track c){<option>{{c}}</option>}</select></label><label>Email<input formControlName="applicationEmail"></label><label>Application URL<input formControlName="applicationUrl"></label><label>External application ID<input formControlName="externalApplicationId"></label><label>Notes<textarea formControlName="notes"></textarea></label><button class="admin-button primary" [disabled]="form.invalid||saving()">Save metadata</button></form></section>
  <section><h2>Status actions</h2><label>Optional transition note<input [formControl]="transitionNote"></label>@for(s of transitions[x.status];track s){<button class="admin-button" (click)="transition(s)">{{label(s)}}</button>}@if(!transitions[x.status].length){<p>This status is terminal; no transition actions are available.</p>}</section>
  <section><h2>Documents</h2><p>Metadata/reference only. No file is uploaded.</p><form [formGroup]="documentForm" (ngSubmit)="attach()"><input placeholder="Document type" formControlName="documentType"><input placeholder="Version label" formControlName="versionLabel"><input placeholder="File name (optional)" formControlName="fileName"><input placeholder="Storage key (optional)" formControlName="storageKey"><input placeholder="SHA-256 hash (optional)" formControlName="contentHash"><button class="admin-button" [disabled]="documentForm.invalid">Attach metadata</button></form>@for(d of x.documents;track d.id){<article [class.muted]="d.removedAt"><strong>{{d.documentType}} {{d.versionLabel}}</strong><p>{{d.fileName||d.storageKey||'Metadata reference'}}</p>@if(!d.removedAt){<button class="admin-button danger" (click)="remove(d.id)">Remove</button>}@else{<span>Removed {{d.removedAt}}</span>}</article>}</section>
  <section><h2>Immutable event history</h2>@for(e of x.events;track e.id){<article><strong>{{e.eventType}}</strong><p>@if(e.fromStatus||e.toStatus){ {{e.fromStatus||'—'}} → {{e.toStatus||'—'}} · }{{e.actorType}} · {{e.occurredAt}}</p>@if(e.note){<p>{{e.note}}</p>}</article>}</section>}</div>`,
})
export class ApplicationDetailPageComponent implements DirtyAware {
  private api=inject(JobHuntingService); private fb=inject(FormBuilder); id=inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  item=signal<ApplicationDetail|null>(null); loading=signal(true); saving=signal(false); error=signal<string|null>(null); transitions=applicationTransitions; channels=['EMAIL','PLATFORM','MANUAL','OTHER']; transitionNote=this.fb.control('');
  form=this.fb.group({channel:[''],applicationEmail:[''],applicationUrl:[''],externalApplicationId:[''],notes:['']});
  documentForm=this.fb.group({documentType:['',Validators.required],versionLabel:['',Validators.required],fileName:[''],storageKey:[''],contentHash:['',Validators.pattern(/^[0-9a-fA-F]{64}$/)]});
  constructor(){this.load()} hasUnsavedChanges(){return this.form.dirty||this.documentForm.dirty}
  load(preserveError=false){this.loading.set(true);this.api.application(this.id).subscribe({next:x=>{this.item.set(x);this.form.patchValue({channel:x.channel||'',applicationEmail:x.applicationEmail||'',applicationUrl:x.applicationUrl||'',externalApplicationId:x.externalApplicationId||'',notes:x.notes||''});this.form.markAsPristine();this.loading.set(false);if(!preserveError)this.error.set(null)},error:e=>{this.error.set(safeAdminError(e));this.loading.set(false)}})}
  save(){if(this.form.invalid)return;this.saving.set(true);this.api.updateApplication(this.id,{expectedVersion:this.item()!.version,...this.form.getRawValue()}).subscribe({next:x=>{this.item.set(x);this.form.markAsPristine();this.saving.set(false)},error:e=>this.failed(e)})}
  transition(status:ApplicationStatus){if(confirm(`Change status to ${this.label(status)}?`))this.api.transition(this.id,{status,expectedVersion:this.item()!.version,note:this.transitionNote.value||null}).subscribe({next:x=>{this.item.set(x);this.transitionNote.reset()},error:e=>this.failed(e)})}
  attach(){if(this.documentForm.invalid)return;this.api.attach(this.id,{...this.documentForm.getRawValue(),metadata:null}).subscribe({next:()=>{this.documentForm.reset();this.load()},error:e=>this.failed(e)})}
  remove(id:string){if(confirm('Soft-remove this document metadata?'))this.api.remove(this.id,id).subscribe({next:()=>this.load(),error:e=>this.failed(e)})}
  label(s:ApplicationStatus){return s==='APPLIED'?'Mark Applied':s==='INTERVIEW'?'Interview':s==='REJECTED'?'Reject':s==='OFFER'?'Offer':'Withdraw'}
  private failed(e:unknown){this.error.set(safeAdminError(e));this.saving.set(false);if((e as {status?:number})?.status===409)this.load(true)}
}
