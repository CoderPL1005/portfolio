import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { JobSummary } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-job-list',
  imports: [FormsModule, RouterLink],
  template: `<div class="admin-page">
    <header class="list-header"><div><p class="eyebrow">JOB HUNTING</p><h1>Jobs</h1><p>Review extracted roles and manage your application pipeline.</p></div><div class="admin-actions"><a class="admin-button primary" routerLink="new/screenshots">New from screenshots</a><a class="admin-button" routerLink="new">New manual job</a></div></header>
    <div class="admin-actions filters">
      <input class="admin-input" aria-label="Search" placeholder="Search company or position" [(ngModel)]="search">
      <select class="admin-input" aria-label="Source" [(ngModel)]="source"><option value="">All sources</option>@for(x of sources;track x){<option>{{x}}</option>}</select>
      <select class="admin-input" aria-label="Verification" [(ngModel)]="verification"><option value="">All verification states</option>@for(x of verifications;track x){<option [value]="x">{{statusLabel(x)}}</option>}</select>
      <select class="admin-input" aria-label="Selection" [(ngModel)]="selection"><option value="">All selection states</option>@for(x of selections;track x){<option [value]="x">{{statusLabel(x)}}</option>}</select>
      <select class="admin-input" aria-label="Archive state" [(ngModel)]="archived"><option value="active">Active</option><option value="archived">Archived</option><option value="all">All</option></select>
      <button class="admin-button" (click)="apply()">Apply</button><button class="admin-button" (click)="reset()">Reset</button>
    </div>
    @if(loading()){<div class="state-panel">Loading jobs…</div>}
    @else if(error()){<p class="admin-error">{{error()}}</p>}
    @else if(!items().length){<div class="state-panel">No jobs match these filters.</div>}
    @else{<div class="table-wrap"><table class="admin-table"><thead><tr><th>Job</th><th>Source</th><th>Verification</th><th>Selection</th><th>Applications</th><th></th></tr></thead><tbody>
      @for(x of items();track x.id){<tr><td class="job-cell"><strong>{{x.positionTitle}}</strong><span>{{x.companyName}}</span><small>{{x.location}}</small></td><td>{{x.source ? statusLabel(x.source) : '—'}}</td><td><span class="status-badge">{{statusLabel(x.verificationStatus)}}</span></td><td><span class="status-badge">{{statusLabel(x.selectionStatus)}}</span></td><td>{{x.applicationCount}}</td><td><a class="admin-button" [routerLink]="[x.id]" [attr.aria-label]="'Open ' + x.positionTitle + ' at ' + x.companyName">Open</a></td></tr>}
    </tbody></table></div>
    <nav class="pagination" aria-label="Job pages"><button class="admin-button" [disabled]="page()<=1" (click)="go(page()-1)">Previous</button><span>Page {{page()}} / {{totalPages()}}</span><button class="admin-button" [disabled]="page()>=totalPages()" (click)="go(page()+1)">Next</button></nav>}
  </div>`,
  styles:[`.list-header{display:flex;align-items:flex-end;justify-content:space-between;gap:2rem;margin-bottom:1.5rem}.list-header h1{margin:.3rem 0;font-size:clamp(2.25rem,4vw,3.5rem)}.list-header>div>p:last-child{margin:0;color:var(--color-text-muted)}.filters{margin-bottom:1rem}.filters .admin-input{flex:1 1 11rem}.job-cell{display:grid;gap:.18rem}.job-cell strong{font-size:.95rem}.job-cell span{color:var(--color-text-muted)}.job-cell small{color:var(--color-text-dim)}@media(max-width:700px){.list-header{align-items:flex-start;flex-direction:column}.list-header .admin-actions,.filters .admin-button{width:100%}.list-header .admin-button,.filters .admin-button{flex:1}.admin-page{padding-inline:1rem}}`],
})
export class JobListPageComponent {
  private api=inject(JobHuntingService);
  readonly items=signal<JobSummary[]>([]); readonly loading=signal(true); readonly error=signal<string|null>(null); readonly page=signal(1); readonly totalPages=signal(1);
  search=''; source=''; verification=''; selection=''; archived:'active'|'archived'|'all'='active'; readonly pageSize=20;
  readonly sources=['MANUAL','TOPCV','VIETNAMWORKS','COMPANY_SITE','FACEBOOK','INSTAGRAM','OTHER'];
  readonly verifications=['PENDING','VERIFIED','UNVERIFIED','LIKELY_EXPIRED']; readonly selections=['PENDING_ANALYSIS','RECOMMENDED','APPROVED','SKIPPED'];
  constructor(){this.load()}
  statusLabel(value:string){const known:Record<string,string>={TOPCV:'TopCV',VIETNAMWORKS:'VietnamWorks',COMPANY_SITE:'Company site'};if(known[value])return known[value];const label=value.replaceAll('_',' ').toLowerCase();return label.charAt(0).toUpperCase()+label.slice(1)}
  apply(){this.page.set(1);this.load()}
  reset(){this.search=this.source=this.verification=this.selection='';this.archived='active';this.page.set(1);this.load()}
  go(target:number){if(target<1||target>this.totalPages()||target===this.page())return;this.page.set(target);this.load()}
  load(){
    this.loading.set(true);this.error.set(null);
    this.api.jobs({page:this.page(),pageSize:this.pageSize,...(this.search&&{search:this.search}),...(this.source&&{source:this.source}),...(this.verification&&{verificationStatus:this.verification}),...(this.selection&&{selectionStatus:this.selection}),...(this.archived!=='all'&&{archived:this.archived==='archived'})}).pipe(take(1)).subscribe({
      next:x=>{this.items.set(x.items);this.totalPages.set(Math.max(1,x.totalPages));this.loading.set(false)},
      error:e=>{this.error.set(safeAdminError(e));this.loading.set(false)},
    });
  }
}
