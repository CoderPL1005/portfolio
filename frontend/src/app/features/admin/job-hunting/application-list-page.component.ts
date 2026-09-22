import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { take } from 'rxjs';
import { safeAdminError } from '../shared/admin-api';
import { ApplicationChannel, ApplicationItem } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

@Component({
  selector: 'app-application-list',
  imports: [DatePipe, FormsModule, RouterLink],
  template: `
    <div class="admin-page application-list-page">
      <header class="list-header"><div><p class="section-kicker">JOB HUNTING</p><h1>Applications</h1><p>Track submissions, interviews, outcomes, and application activity.</p></div>@if(!loading()&&!error()){<span class="count-badge">{{total()}} {{total()===1?'application':'applications'}}</span>}</header>
      <section class="admin-panel filter-panel" aria-label="Application filters">
        <div class="filters"><label>Search<input class="admin-input" aria-label="Search" [(ngModel)]="search" placeholder="Company or position"></label><label>Status<select class="admin-input" aria-label="Status" [(ngModel)]="status"><option value="">All statuses</option>@for(value of statuses;track value){<option [value]="value">{{statusLabel(value)}}</option>}</select></label><label>Channel<select class="admin-input" aria-label="Channel" [(ngModel)]="channel"><option value="">All channels</option>@for(value of channels;track value){<option [value]="value">{{statusLabel(value)}}</option>}</select></label></div>
        <div class="filter-actions"><button class="admin-button primary" type="button" [disabled]="loading()" (click)="apply()">Apply filters</button><button class="admin-button" type="button" [disabled]="loading()" (click)="reset()">Reset</button></div>
      </section>
      @if(loading()){<div class="state-panel" aria-live="polite">Loading applications…</div>}
      @else if(error()){<div class="state-panel error-state" role="alert"><strong>Applications could not be loaded.</strong><p>{{error()}}</p><button class="admin-button" type="button" (click)="load()">Try again</button></div>}
      @else if(!items().length){<div class="state-panel empty-state"><strong>No applications match these filters.</strong><p>Adjust the filters or create an application from an approved job.</p></div>}
      @else{
        <div class="application-grid">
          @for(application of items();track application.id){
            <a class="application-card" [routerLink]="[application.id]" [attr.aria-label]="'Open application for '+application.positionTitle+' at '+application.companyName">
              <div class="card-heading"><div><span class="company">{{application.companyName}}</span><h2>{{application.positionTitle}}</h2></div><span class="status-badge status-{{application.status.toLowerCase()}}">{{statusLabel(application.status)}}</span></div>
              <div class="card-meta"><span>{{application.channel?statusLabel(application.channel):'No channel specified'}}</span>@if(activityDate(application);as activityDate){<span>{{activityLabel(application)}} {{activityDate|date:'mediumDate'}}</span>}@else{<span>No activity recorded</span>}</div>
              <span class="open-label">Open workspace <span aria-hidden="true">&rarr;</span></span>
            </a>
          }
        </div>
        <nav class="pagination" aria-label="Application pages"><button class="admin-button" [disabled]="page()<=1||loading()" (click)="go(page()-1)">Previous</button><span>Page {{page()}} of {{totalPages()}}</span><button class="admin-button" [disabled]="page()>=totalPages()||loading()" (click)="go(page()+1)">Next</button></nav>
      }
    </div>
  `,
  styles: `
    .application-list-page{display:grid;gap:1.25rem;max-width:76rem}.list-header{display:flex;justify-content:space-between;align-items:flex-end;gap:1.5rem}.list-header h1{margin:.15rem 0;font-size:clamp(1.8rem,4vw,2.6rem)}.list-header p:last-child{margin:.4rem 0 0;color:var(--color-text-muted)}.section-kicker{margin:0;color:var(--color-primary);font-size:.7rem;font-weight:750;letter-spacing:.14em}.count-badge{padding:.45rem .7rem;border:1px solid var(--color-border);border-radius:999px;color:var(--color-text-muted);font-size:.75rem;white-space:nowrap}
    .filter-panel{display:flex;justify-content:space-between;align-items:flex-end;gap:1rem}.filters{display:grid;grid-template-columns:minmax(14rem,1.5fr) repeat(2,minmax(10rem,1fr));gap:.8rem;flex:1}.filters label{display:grid;gap:.4rem;color:var(--color-text-muted);font-size:.72rem;font-weight:650}.filter-actions{display:flex;gap:.6rem}.application-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.application-card{display:grid;gap:1.2rem;min-width:0;padding:1.25rem;border:1px solid var(--color-border);border-radius:var(--radius-lg);color:var(--color-text);text-decoration:none;background:var(--color-surface);transition:border-color .15s ease,transform .15s ease}.application-card:hover,.application-card:focus-visible{border-color:var(--color-primary);transform:translateY(-1px)}.card-heading{display:flex;justify-content:space-between;align-items:flex-start;gap:1rem}.company{color:var(--color-primary);font-size:.72rem;font-weight:750;letter-spacing:.05em;text-transform:uppercase}.card-heading h2{margin:.35rem 0 0;font-size:1.1rem}.card-meta{display:flex;gap:.55rem 1rem;flex-wrap:wrap;color:var(--color-text-muted);font-size:.78rem}.card-meta span+span::before{content:'·';margin-right:1rem;color:var(--color-text-dim)}.open-label{display:flex;justify-content:space-between;align-items:center;padding-top:.8rem;border-top:1px solid var(--color-border);color:var(--color-text);font-size:.8rem;font-weight:650}.pagination{display:flex;justify-content:center;align-items:center;gap:1rem;margin-top:.5rem}.pagination span{color:var(--color-text-muted);font-size:.8rem}.error-state,.empty-state{display:grid;justify-items:start;gap:.4rem}.error-state p,.empty-state p{margin:0;color:var(--color-text-muted)}
    @media(max-width:900px){.filter-panel{align-items:stretch;flex-direction:column}.filter-actions{justify-content:flex-end}.filters{grid-template-columns:1fr 1fr}.filters label:first-child{grid-column:1/-1}}
    @media(max-width:680px){.list-header{align-items:flex-start;flex-direction:column}.application-grid,.filters{grid-template-columns:1fr}.filters label:first-child{grid-column:auto}.filter-actions,.filter-actions .admin-button{width:100%}.application-card{padding:1rem}.card-heading{gap:.75rem}.card-meta{display:grid}.card-meta span+span::before{content:none}.pagination{justify-content:space-between}.pagination .admin-button{min-height:2.75rem}}
  `,
})
export class ApplicationListPageComponent {
  private readonly api=inject(JobHuntingService);
  readonly items=signal<ApplicationItem[]>([]);readonly loading=signal(true);readonly error=signal<string|null>(null);readonly page=signal(1);readonly totalPages=signal(1);readonly total=signal(0);readonly pageSize=20;
  search='';status='';channel='';readonly statuses=['DRAFT','APPLIED','INTERVIEW','REJECTED','OFFER','WITHDRAWN'];readonly channels:ApplicationChannel[]=['EMAIL','PLATFORM','MANUAL','OTHER'];
  constructor(){this.load()}
  apply(){this.page.set(1);this.load()}
  reset(){this.search=this.status=this.channel='';this.page.set(1);this.load()}
  go(target:number){if(target<1||target>this.totalPages()||target===this.page())return;this.page.set(target);this.load()}
  load(){this.loading.set(true);this.error.set(null);this.api.applications({page:this.page(),pageSize:this.pageSize,...(this.search&&{search:this.search}),...(this.status&&{status:this.status}),...(this.channel&&{channel:this.channel})}).pipe(take(1)).subscribe({next:result=>{this.items.set(result.items);this.total.set(result.total);this.totalPages.set(Math.max(1,result.totalPages));this.loading.set(false)},error:error=>{this.error.set(safeAdminError(error));this.loading.set(false)}})}
  statusLabel(value:string){return value.toLowerCase().replaceAll('_',' ').replace(/\b\w/g,letter=>letter.toUpperCase())}
  activityDate(application:ApplicationItem){return application.lastActivityAt||application.appliedAt}
  activityLabel(application:ApplicationItem){return application.lastActivityAt?'Active':'Applied'}
}
