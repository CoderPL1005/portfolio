import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { JobCreate, JobDetail, JobSource, JobWrite } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector:'app-job-edit', imports:[ReactiveFormsModule,RouterLink],
  template:`<div class="admin-page"><a routerLink="/admin/job-hunting/jobs">← Jobs</a><h1>{{isNew?'New manual job':job()?.positionTitle}}</h1>
  @if(loading()){<div class="state-panel">Loading job…</div>}@if(error()){<p class="admin-error">{{error()}}</p>}
  @if(!loading()){<form [formGroup]="form" (ngSubmit)="save()">
    @if(isNew){<fieldset [formGroup]="sourceForm"><legend>Manual source</legend><label>Source<select formControlName="source">@for(x of sources;track x){<option>{{x}}</option>}</select></label><label>Source URL<input formControlName="sourceUrl"></label><label>External ID<input formControlName="sourceExternalId"></label><label>Raw content<textarea formControlName="rawContent"></textarea></label>@if(sourceForm.controls.rawContent.touched&&sourceForm.controls.rawContent.invalid){<p class="admin-error">Raw content is required.</p>}</fieldset>}
    <fieldset><legend>Job basics</legend><label>Company<input formControlName="companyName"></label><label>Position<input formControlName="positionTitle"></label><label>Location<input formControlName="location"></label><label>Employment type<input formControlName="employmentType"></label><label>Workplace type<input formControlName="workplaceType"></label></fieldset>
    <fieldset><legend>Compensation & requirements</legend><label>Minimum salary<input type="number" formControlName="salaryMinimum"></label><label>Maximum salary<input type="number" formControlName="salaryMaximum"></label><label>Currency<input formControlName="salaryCurrency"></label><label>Period<input formControlName="salaryPeriod"></label><label>Experience requirements<textarea formControlName="experienceRequirements"></textarea></label><label>Description<textarea formControlName="description"></textarea></label><label>Technology stack<textarea formControlName="technologyStack" placeholder="Comma or newline separated"></textarea></label></fieldset>
    <fieldset><legend>Application & notes</legend><label>Application email<input formControlName="applicationEmail"></label><label>Application URL<input formControlName="applicationUrl"></label><label>Expires at<input type="date" formControlName="expiresAt"></label><label>Notes<textarea formControlName="notes"></textarea></label></fieldset>
    <button class="admin-button primary" [disabled]="form.invalid||(isNew&&sourceForm.invalid)||saving()">Save</button>
  </form>}
  @if(job();as j){<section><h2>State</h2><p>Verification: {{j.verificationStatus}}</p>@for(x of verifications;track x){<button (click)="verify(x)">{{x}}</button>}<p>Selection: {{j.selectionStatus==='APPROVED'?'Approved for application':j.selectionStatus}}</p>@for(x of selections;track x){<button (click)="select(x)">{{x}}</button>}<button class="danger" (click)="archive()">Archive</button></section>
  <section><h2>Raw sources (read-only)</h2>@for(x of j.sources;track x.id){<article><strong>{{x.source}}</strong>@if(x.sourceUrl){<a [href]="x.sourceUrl" target="_blank" rel="noopener noreferrer">Source link</a>}<pre>{{x.rawContent}}</pre></article>}@empty{<p>No raw sources.</p>}</section>
  <section><h2>Applications</h2><button (click)="createApplication()">Create application</button>@for(x of j.applications;track x.id){<a [routerLink]="['/admin/job-hunting/applications',x.id]">{{x.status}}</a>}</section>}
  </div>`,
})
export class JobEditPageComponent implements DirtyAware {
  private fb=inject(FormBuilder); private api=inject(JobHuntingService); private router=inject(Router);
  readonly id=inject(ActivatedRoute).snapshot.paramMap.get('id'); readonly isNew=!this.id;
  readonly job=signal<JobDetail|null>(null); readonly loading=signal(!this.isNew); readonly error=signal<string|null>(null); readonly saving=signal(false);
  readonly sources:JobSource[]=['MANUAL','TOPCV','VIETNAMWORKS','COMPANY_SITE','FACEBOOK','INSTAGRAM','OTHER'];
  readonly verifications=['PENDING','VERIFIED','UNVERIFIED','LIKELY_EXPIRED']; readonly selections=['PENDING_ANALYSIS','RECOMMENDED','APPROVED','SKIPPED'];
  readonly sourceForm=this.fb.group({source:this.fb.nonNullable.control<JobSource>('MANUAL'),sourceUrl:[''],sourceExternalId:[''],rawContent:['',Validators.required]});
  readonly form=this.fb.group({companyName:['',Validators.required],positionTitle:['',Validators.required],location:['',Validators.required],employmentType:[''],workplaceType:[''],salaryMinimum:[null as number|null],salaryMaximum:[null as number|null],salaryCurrency:[''],salaryPeriod:[''],experienceRequirements:[''],description:['',Validators.required],technologyStack:[''],applicationEmail:[''],applicationUrl:[''],expiresAt:[''],notes:['']});
  constructor(){if(this.id)this.load()}
  hasUnsavedChanges(){return this.form.dirty||(this.isNew&&this.sourceForm.dirty)}
  load(preserveError=false){this.loading.set(true);this.api.job(this.id!).pipe(take(1)).subscribe({next:j=>{this.job.set(j);this.form.reset({...j,technologyStack:j.technologyStack.join(', '),employmentType:j.employmentType||'',workplaceType:j.workplaceType||'',salaryCurrency:j.salaryCurrency||'',salaryPeriod:j.salaryPeriod||'',experienceRequirements:j.experienceRequirements||'',applicationEmail:j.applicationEmail||'',applicationUrl:j.applicationUrl||'',expiresAt:j.expiresAt?.slice(0,10)||'',notes:j.notes||''});this.loading.set(false);if(!preserveError)this.error.set(null)},error:e=>{this.error.set(safeAdminError(e));this.loading.set(false)}})}
  save(){if(this.form.invalid||(this.isNew&&this.sourceForm.invalid)){this.form.markAllAsTouched();this.sourceForm.markAllAsTouched();return}this.saving.set(true);this.error.set(null);const write=this.jobWrite();const operation=this.isNew?this.api.createJob({...write,...this.createSource()}):this.api.updateJob(this.id!,{...write,expectedVersion:this.job()!.version});operation.pipe(take(1)).subscribe({next:j=>{this.form.markAsPristine();this.sourceForm.markAsPristine();this.saving.set(false);if(this.isNew)void this.router.navigate(['/admin/job-hunting/jobs',j.id]);else this.job.set(j)},error:e=>{this.error.set(safeAdminError(e));this.saving.set(false);if((e as {status?:number})?.status===409)this.load(true)}})}
  verify(status:string){this.api.verification(this.id!,status,this.job()!.version).pipe(take(1)).subscribe({next:x=>this.replace(x),error:e=>this.failed(e)})}
  select(status:string){this.api.selection(this.id!,status,this.job()!.version).pipe(take(1)).subscribe({next:x=>this.replace(x),error:e=>this.failed(e)})}
  archive(){if(confirm('Archive this job?'))this.api.archive(this.id!,this.job()!.version).pipe(take(1)).subscribe({next:x=>this.replace(x),error:e=>this.failed(e)})}
  createApplication(){this.api.createApplication({jobPostingId:this.id,channel:'MANUAL'}).pipe(take(1)).subscribe({next:x=>void this.router.navigate(['/admin/job-hunting/applications',x.id]),error:e=>this.error.set(safeAdminError(e))})}
  private jobWrite():JobWrite{const v=this.form.getRawValue();return{companyName:v.companyName??'',positionTitle:v.positionTitle??'',location:v.location??'',employmentType:v.employmentType||null,workplaceType:v.workplaceType||null,salaryMinimum:v.salaryMinimum,salaryMaximum:v.salaryMaximum,salaryCurrency:v.salaryCurrency||null,salaryPeriod:v.salaryPeriod||null,experienceRequirements:v.experienceRequirements||null,description:v.description??'',technologyStack:(v.technologyStack||'').split(/[\n,]/).map(x=>x.trim()).filter(Boolean),applicationEmail:v.applicationEmail||null,applicationUrl:v.applicationUrl||null,expiresAt:v.expiresAt||null,notes:v.notes||null}}
  private createSource():Pick<JobCreate,'source'|'sourceExternalId'|'sourceUrl'|'rawContent'>{const v=this.sourceForm.getRawValue();return{source:v.source,sourceExternalId:v.sourceExternalId||null,sourceUrl:v.sourceUrl||null,rawContent:v.rawContent??''}}
  private replace(value:JobDetail){this.job.set(value);this.error.set(null)}
  private failed(e:unknown){this.error.set(safeAdminError(e));if((e as {status?:number})?.status===409)this.load(true)}
}
