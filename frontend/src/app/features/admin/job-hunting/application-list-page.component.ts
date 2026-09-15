import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { ApplicationChannel, ApplicationItem } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector:'app-application-list', imports:[FormsModule,RouterLink],
  template:`<div class="admin-page"><h1>Applications</h1><div class="admin-actions">
    <input class="admin-input" aria-label="Search" [(ngModel)]="search" placeholder="Search company or position">
    <select class="admin-input" aria-label="Status" [(ngModel)]="status"><option value="">All statuses</option>@for(x of statuses;track x){<option>{{x}}</option>}</select>
    <select class="admin-input" aria-label="Channel" [(ngModel)]="channel"><option value="">All channels</option>@for(x of channels;track x){<option>{{x}}</option>}</select>
    <button class="admin-button" (click)="apply()">Apply</button><button class="admin-button" (click)="reset()">Reset</button>
  </div>
  @if(loading()){<div class="state-panel">Loading applications…</div>}@else if(error()){<p class="admin-error">{{error()}}</p>}@else if(!items().length){<div class="state-panel">No applications match these filters.</div>}@else{
    @for(x of items();track x.id){<article><a [routerLink]="[x.id]"><strong>{{x.companyName}} — {{x.positionTitle}}</strong></a><span class="status-badge">{{x.status}}</span><small>{{x.channel||'No channel'}}</small></article>}
    <nav class="pagination" aria-label="Application pages"><button class="admin-button" [disabled]="page()<=1" (click)="go(page()-1)">Previous</button><span>Page {{page()}} / {{totalPages()}}</span><button class="admin-button" [disabled]="page()>=totalPages()" (click)="go(page()+1)">Next</button></nav>
  }</div>`,
})
export class ApplicationListPageComponent {
  private api=inject(JobHuntingService);
  readonly items=signal<ApplicationItem[]>([]); readonly loading=signal(true); readonly error=signal<string|null>(null); readonly page=signal(1); readonly totalPages=signal(1); readonly pageSize=20;
  search='';status='';channel='';readonly statuses=['DRAFT','APPLIED','INTERVIEW','REJECTED','OFFER','WITHDRAWN'];readonly channels:ApplicationChannel[]=['EMAIL','PLATFORM','MANUAL','OTHER'];
  constructor(){this.load()}
  apply(){this.page.set(1);this.load()}
  reset(){this.search=this.status=this.channel='';this.page.set(1);this.load()}
  go(target:number){if(target<1||target>this.totalPages()||target===this.page())return;this.page.set(target);this.load()}
  load(){this.loading.set(true);this.error.set(null);this.api.applications({page:this.page(),pageSize:this.pageSize,...(this.search&&{search:this.search}),...(this.status&&{status:this.status}),...(this.channel&&{channel:this.channel})}).pipe(take(1)).subscribe({next:x=>{this.items.set(x.items);this.totalPages.set(Math.max(1,x.totalPages));this.loading.set(false)},error:e=>{this.error.set(safeAdminError(e));this.loading.set(false)}})}
}
