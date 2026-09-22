import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { DirtyAware } from '../shared/dirty.guard';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-job-preferences-page', imports: [ReactiveFormsModule, DatePipe],
  template: `
    <div class="admin-page preferences-page">
      <header class="page-header"><div><p class="eyebrow">JOB HUNTING</p><h1>Candidate preferences</h1><p>Private preferences used by deterministic fit analysis. Portfolio facts remain the source of qualifications.</p></div></header>
      @if (loading()) { <div class="state-panel">Loading preferences...</div> }
      @if (error()) { <p class="admin-error" role="alert">{{ error() }}</p> }
      <section class="admin-panel canonical-cv" aria-labelledby="canonical-cv-title">
        <div><p class="eyebrow">PRIVATE DOCUMENT</p><h2 id="canonical-cv-title">Canonical CV</h2><p>This single private CV will be used for future application packages.</p></div>
        @if (cvLoading()) { <div class="state-panel">Loading canonical CV...</div> }
        @else {
          @if (cvError()) { <p class="admin-error" role="alert">{{ cvError() }}</p> }
          @if (canonicalCv()?.isConfigured) {
            <div class="cv-metadata"><strong>{{ canonicalCv()?.fileName }}</strong><span>PDF · {{ formatSize(canonicalCv()?.fileSizeBytes) }}</span><span>Updated {{ canonicalCv()?.updatedAt | date:'medium' }}</span></div>
            <p class="replacement-note">Replacing the canonical CV affects future application packages only. Existing finalized application snapshots will remain unchanged.</p>
          } @else {
            <div class="empty-cv"><strong>No canonical CV uploaded.</strong><span>This CV will be used for future application packages.</span></div>
          }
          <div class="cv-actions">
            @if (canonicalCv()?.isConfigured) { <button type="button" class="admin-button" [disabled]="previewing() || cvUploading()" (click)="previewCv()">{{ previewing() ? 'Opening...' : 'Preview / Download' }}</button> }
            <label class="file-button admin-button" [class.disabled]="cvUploading()"><span>{{ canonicalCv()?.isConfigured ? 'Replace CV' : 'Upload CV' }}</span><input type="file" accept="application/pdf,.pdf" [disabled]="cvUploading()" (change)="chooseCv($event)"></label>
          </div>
          <small class="file-hint">PDF only · maximum 10 MiB</small>
          @if (cvFile()) {
            <div class="selected-file"><span>{{ cvFile()?.name }} · {{ formatSize(cvFile()?.size) }}</span><button type="button" class="admin-button primary" [disabled]="cvUploading()" (click)="uploadCv()">{{ cvUploading() ? 'Uploading...' : canonicalCv()?.isConfigured ? 'Confirm replacement' : 'Upload canonical CV' }}</button><button type="button" class="admin-button" [disabled]="cvUploading()" (click)="clearCvFile()">Cancel</button></div>
          }
        }
      </section>
      @if (!loading()) {
        <form class="admin-panel admin-form" [formGroup]="form" (ngSubmit)="save()">
          <p class="hint">Enter one value per line. Duplicate values are removed when saved.</p>
          <div class="grid">
            <label>Target roles<textarea formControlName="targetRoles"></textarea></label>
            <label>Preferred technologies<textarea formControlName="preferredTechnologies"></textarea></label>
            <label>Acceptable locations<textarea formControlName="acceptableLocations"></textarea></label>
            <label>Workplace types<textarea formControlName="workplaceTypes" placeholder="Remote&#10;Hybrid"></textarea></label>
            <label>Employment types<textarea formControlName="employmentTypes" placeholder="Full time&#10;Internship"></textarea></label>
          </div>
          <fieldset><legend>Minimum salary</legend><div class="salary"><label>Amount<input type="number" min="0" step="0.01" formControlName="minimumSalary"></label><label>Currency<input maxlength="3" formControlName="salaryCurrency" placeholder="VND"></label><label>Period<input maxlength="30" formControlName="salaryPeriod" placeholder="MONTH"></label></div></fieldset>
          <div class="actions"><span>{{ form.dirty ? 'Unsaved changes' : 'All changes saved' }}</span><button class="admin-button primary" type="submit" [disabled]="saving() || !form.dirty">{{ saving() ? 'Saving...' : 'Save preferences' }}</button></div>
        </form>
      }
    </div>`,
  styles: [`
    .preferences-page{display:grid;gap:1.25rem;max-width:70rem}.page-header h1,.canonical-cv h2{margin:.25rem 0}.page-header p,.canonical-cv p{color:var(--color-text-muted)}.canonical-cv{display:grid;gap:1rem}.cv-metadata,.empty-cv{display:grid;gap:.3rem;padding:1rem;border:1px solid var(--color-border);border-radius:var(--radius-md);background:var(--color-surface-lowest)}.cv-metadata span,.empty-cv span,.file-hint,.selected-file span{color:var(--color-text-muted)}.replacement-note{margin:0}.cv-actions,.selected-file{display:flex;align-items:center;gap:.7rem;flex-wrap:wrap}.file-button{position:relative;cursor:pointer}.file-button.disabled{cursor:not-allowed;opacity:.6}.file-button input{position:absolute;width:1px;height:1px;opacity:0;pointer-events:none}.selected-file{padding-top:.5rem}.admin-form{gap:1.25rem}.hint{margin:0;color:var(--color-text-muted)}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.grid textarea{min-height:8rem}fieldset{border:1px solid var(--color-border);border-radius:var(--radius-md);padding:1rem}legend{padding:0 .4rem;font-weight:700}.salary{display:grid;grid-template-columns:2fr 1fr 1fr;gap:1rem}.actions{display:flex;align-items:center;justify-content:space-between;gap:1rem}.actions span{color:var(--color-text-muted);font-size:.8rem}@media(max-width:640px){.grid,.salary{grid-template-columns:1fr}.preferences-page{padding-inline:1rem}.actions,.cv-actions,.selected-file{align-items:stretch;flex-direction:column}.actions button,.cv-actions .admin-button,.selected-file .admin-button{width:100%;text-align:center}}
  `]
})
export class JobPreferencesPageComponent implements DirtyAware, OnDestroy {
  private fb=inject(FormBuilder); private api=inject(JobHuntingService);
  readonly loading=signal(true); readonly saving=signal(false); readonly error=signal<string|null>(null); private version=0;
  readonly cvLoading=signal(true);readonly cvUploading=signal(false);readonly previewing=signal(false);readonly cvError=signal<string|null>(null);readonly canonicalCv=signal<import('./job-hunting.models').CanonicalCv|null>(null);readonly cvFile=signal<File|null>(null);private readonly objectUrls=new Set<string>();
  readonly form=this.fb.group({
    targetRoles:['',[preferenceValuesValidator]],preferredTechnologies:['',[preferenceValuesValidator]],acceptableLocations:['',[preferenceValuesValidator]],workplaceTypes:['',[preferenceValuesValidator]],employmentTypes:['',[preferenceValuesValidator]],
    minimumSalary:[null as number|null,[Validators.min(0),Validators.max(9999999999999999.99),numeric18Scale2Validator]],
    salaryCurrency:['',[Validators.pattern(/^[A-Za-z]{3}$/)]],salaryPeriod:['',[Validators.maxLength(30)]]
  });
  constructor(){this.load();this.loadCanonicalCv()}
  hasUnsavedChanges(){return this.form.dirty||this.cvFile()!==null}
  load(){this.api.preferences().pipe(take(1)).subscribe({next:value=>{this.bind(value);this.loading.set(false)},error:value=>{this.error.set(safeAdminError(value));this.loading.set(false)}})}
  save(){if(this.saving()||!this.form.dirty)return;const value=this.form.getRawValue();if(this.form.invalid){this.error.set('Preferences must contain at most 100 values of 100 characters, and salary must use numeric(18,2) with a three-letter currency.');return}if(value.minimumSalary!==null&&(!value.salaryCurrency?.trim()||!value.salaryPeriod?.trim())){this.error.set('Currency and period are required when minimum salary is set.');return}if(value.minimumSalary===null&&(value.salaryCurrency?.trim()||value.salaryPeriod?.trim())){this.error.set('Currency and period require a minimum salary.');return}this.saving.set(true);this.error.set(null);this.api.updatePreferences({expectedVersion:this.version,targetRoles:this.values(value.targetRoles),preferredTechnologies:this.values(value.preferredTechnologies),acceptableLocations:this.values(value.acceptableLocations),workplaceTypes:this.values(value.workplaceTypes),employmentTypes:this.values(value.employmentTypes),minimumSalary:value.minimumSalary,salaryCurrency:value.salaryCurrency||null,salaryPeriod:value.salaryPeriod||null}).pipe(take(1)).subscribe({next:saved=>{this.bind(saved);this.saving.set(false)},error:error=>{this.error.set(safeAdminError(error));this.saving.set(false);if((error as {status?:number})?.status===409)this.load()}})}
  loadCanonicalCv(preserveError=false){this.cvLoading.set(true);this.api.canonicalCv().pipe(take(1)).subscribe({next:value=>{this.canonicalCv.set(value);this.cvLoading.set(false);if(!preserveError)this.cvError.set(null)},error:error=>{this.cvError.set(safeAdminError(error));this.cvLoading.set(false)}})}
  chooseCv(event:Event){const file=(event.target as HTMLInputElement).files?.[0]??null;this.cvError.set(null);if(!file){this.cvFile.set(null);return}if(file.type!=='application/pdf'&&!file.name.toLowerCase().endsWith('.pdf')){this.cvFile.set(null);this.cvError.set('Only PDF files are allowed.');return}if(file.size<=0||file.size>10*1024*1024){this.cvFile.set(null);this.cvError.set('The canonical CV must be a non-empty PDF no larger than 10 MiB.');return}this.cvFile.set(file)}
  clearCvFile(){if(!this.cvUploading())this.cvFile.set(null)}
  uploadCv(){const file=this.cvFile();const current=this.canonicalCv();if(!file||this.cvUploading())return;this.cvUploading.set(true);this.cvError.set(null);this.api.uploadCanonicalCv(file,current?.version??0).pipe(take(1)).subscribe({next:()=>{this.cvFile.set(null);this.cvUploading.set(false);this.loadCanonicalCv()},error:error=>{this.cvError.set(safeAdminError(error));this.cvUploading.set(false);if((error as {status?:number})?.status===409)this.loadCanonicalCv(true)}})}
  previewCv(){if(!this.canonicalCv()?.isConfigured||this.previewing())return;this.previewing.set(true);this.cvError.set(null);this.api.canonicalCvContent().pipe(take(1)).subscribe({next:blob=>{const url=URL.createObjectURL(blob);this.objectUrls.add(url);window.open(url,'_blank','noopener,noreferrer');setTimeout(()=>this.revoke(url),60_000);this.previewing.set(false)},error:error=>{this.cvError.set(safeAdminError(error));this.previewing.set(false)}})}
  formatSize(bytes:number|null|undefined){if(!bytes)return '0 B';return bytes>=1024*1024?`${(bytes/(1024*1024)).toFixed(1)} MiB`:`${Math.ceil(bytes/1024)} KiB`}
  ngOnDestroy(){for(const url of this.objectUrls)URL.revokeObjectURL(url);this.objectUrls.clear()}
  private revoke(url:string){if(this.objectUrls.delete(url))URL.revokeObjectURL(url)}
  private bind(value:import('./job-hunting.models').CandidateJobPreferences){this.version=value.version;this.form.reset({targetRoles:this.lines(value.targetRoles),preferredTechnologies:this.lines(value.preferredTechnologies),acceptableLocations:this.lines(value.acceptableLocations),workplaceTypes:this.lines(value.workplaceTypes),employmentTypes:this.lines(value.employmentTypes),minimumSalary:value.minimumSalary,salaryCurrency:value.salaryCurrency||'',salaryPeriod:value.salaryPeriod||''})}
  private values(value:string|null|undefined){return (value||'').split(/\r?\n/).map(x=>x.trim()).filter(Boolean)} private lines(values:string[]){return values.join('\n')}
}

const preferenceValuesValidator:ValidatorFn=(control:AbstractControl):ValidationErrors|null=>{
  const values=String(control.value??'').split(/\r?\n/).map(value=>value.trim()).filter(Boolean);
  return values.length>100||values.some(value=>value.length>100)?{preferenceValues:true}:null;
};
const numeric18Scale2Validator:ValidatorFn=(control:AbstractControl):ValidationErrors|null=>{
  if(control.value===null||control.value==='')return null;
  const value=String(control.value);
  return /^\d{1,16}(?:\.\d{1,2})?$/.test(value)?null:{numeric18Scale2:true};
};
