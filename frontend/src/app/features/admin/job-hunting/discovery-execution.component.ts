import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, JsonPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, timer } from 'rxjs';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryExecution } from './discovery-execution.models';

@Component({
  selector: 'app-discovery-execution', standalone: true, imports: [RouterLink, DatePipe, JsonPipe],
  template: `
    <section aria-label="Discovery execution" class="execution">
      <div class="admin-actions"><button class="admin-button primary" type="button" (click)="start()" [disabled]="blocked()">Run Job Discovery</button>
        <button class="admin-button" type="button" (click)="refresh()" [disabled]="starting()">Refresh run status</button></div>
      <p class="muted">One provider request per execution. No automatic retry or application submission.</p>
      @if(error()){<p role="alert">{{error()}}</p>}
      @if(current(); as run){
        <h2>Discovery: {{run.state}}</h2>
        <a routerLink="/admin/job-hunting/discovery" [queryParams]="{runId:run.id}">Review observations and Human Review</a>
        <p role="status">Requests: {{run.actualRequests ?? 'UNKNOWN'}} · Deducted credits: {{run.creditsDeducted ?? 'UNKNOWN'}}</p>
        @if(run.counts; as c){<p>Posts: {{c.fetched ?? 0}} · Recruitment: {{c.validRecruitment ?? 0}} · Normalized JDs: {{c.normalized ?? 0}} · Duplicates: {{c.duplicates ?? 0}} · Needs review: {{c.extractionFailures ?? 0}}</p>}
        @if(run.errorCode){<p>{{run.errorCode}}</p>}
        @for(result of run.results ?? []; track $index){
          <article><h3>Post {{(result.itemIndex ?? $index) + 1}} · {{result.status}}</h3>
            <p>Gemini: {{result.semanticState ?? 'NOT_REQUESTED'}}</p>
            @if(result.processing; as processing){
              @if(processing.review){
              <p>Recruitment: {{processing.review.contentValidity.status}} · {{processing.review.contentValidity.explanation}}</p>
              <p>Profile matching: {{processing.review.targetMatching.status}} · {{processing.review.targetMatching.explanation}}</p>
              @if(processing.review.sourceWarnings.length){<p>Source warnings: {{processing.review.sourceWarnings.join(', ')}}</p>}
              @if(processing.review.missingFields.length){<p>Missing / uncertain: {{processing.review.missingFields.join(', ')}}</p>}
              } @else {<p>Classification unavailable. Original post is preserved for review.</p>}
              @if(processing.extractionError){<p role="status">Processing: {{processing.extractionError}}. Original post remains available.</p>}
              @for(v of processing.vacancies; track $index){<details><summary>Extracted fields and evidence — vacancy {{$index + 1}}</summary><pre>{{v.fields | json}}</pre><p>Missing / uncertain: {{v.missingFields?.join(', ')}}</p></details>}
            }@else{<p>Classification has not been persisted yet. This does not mean the post is invalid.</p>}
            @if(result.jobPostingId){<a [routerLink]="['/admin/job-hunting/jobs',result.jobPostingId]">Open Job</a>}
            @if(result.missingFields?.length){<p>Missing / uncertain: {{result.missingFields?.join(', ')}}</p>}
            @for(v of result.vacancies ?? []; track $index){
              <p>{{v.status}} @if(v.jobPostingId){<a [routerLink]="['/admin/job-hunting/jobs',v.jobPostingId]">Open Job</a>}</p>
              @if(v.missingFields?.length){<p>Missing / uncertain: {{v.missingFields?.join(', ')}}</p>}
              <details><summary>Field evidence and status</summary><pre>{{v.fields | json}}</pre></details>
            }
            @if(result.observation; as o){<section aria-label="Original Facebook post"><p>{{o.outcome}}</p><pre>{{o.rawContent ?? 'No source text supplied'}}</pre><p>Canonical source: {{o.sourceUrl ?? 'Unverified / unavailable'}}</p><details><summary>Source provenance</summary><p>Provider post ID: {{o.providerPostId}}</p><p>Observed provider URL (not verified): {{o.providerSourceUrl}}</p></details></section>}
          </article>
        }
      }
      @if(history().length){<details><summary>Recent executions</summary>@for(run of history();track run.id){<button class="admin-button" type="button" (click)="select(run.id)">{{run.createdAt | date:'short'}} · {{run.state}}</button>}</details>}
    </section>`,
  styles: `:host{display:block}.execution{margin-block:1.5rem;padding:1.25rem;background:var(--admin-surface,#161b26);border-radius:var(--admin-radius,12px)}article{padding-block:1rem;border-bottom:1px solid var(--admin-border,#343b49)}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:24rem;overflow:auto}button{margin:.25rem}p{overflow-wrap:anywhere}`
})
export class DiscoveryExecutionComponent {
  private api = inject(JobHuntingService);
  private destroyRef = inject(DestroyRef);
  private poll?: Subscription;
  private requestKey?: string;
  current = signal<DiscoveryExecution | null>(null);
  history = signal<DiscoveryExecution[]>([]);
  starting = signal(false);
  uncertain = signal(false);
  historyKnown = signal(false);
  error = signal('');
  private terminal(run: DiscoveryExecution){return run.active === false || ['SUCCEEDED','PARTIAL','FAILED'].includes(run.state);}
  blocked(){return this.starting() || this.uncertain() || !this.historyKnown() || this.history().some(r=>!this.terminal(r)) || !!(this.current() && !this.terminal(this.current()!));}
  constructor(){this.refresh();this.destroyRef.onDestroy(()=>this.poll?.unsubscribe());}
  refresh(){
    // Older embedding consumers may not expose execution capabilities yet.
    if(typeof this.api.discoveryExecutions !== 'function')return;
    this.api.discoveryExecutions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:h=>{
      this.history.set(h.items);this.historyKnown.set(true);
      const selected=this.current()?.id;
      const active=h.items.find(r=>!this.terminal(r));
      const id=selected??active?.id??h.items[0]?.id;if(id)this.select(id);
    },error:()=>{this.historyKnown.set(false);this.error.set('Run history unavailable. Discovery cannot be started safely.');}});
  }
  start(){
    if(this.blocked())return;
    this.starting.set(true);this.error.set('');this.requestKey ??= crypto.randomUUID();
    this.api.startDiscoveryExecution({clientRequestId:this.requestKey}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:r=>{
      this.starting.set(false);this.current.set(r);this.select(r.id);
    },error:()=>{this.starting.set(false);this.uncertain.set(true);this.error.set('UNKNOWN start outcome. Do not retry; refresh authoritative run history.');}});
  }
  select(id:string){
    this.poll?.unsubscribe();
    const read=()=>this.api.discoveryExecution(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:r=>{
      this.current.set(r);
      if(this.terminal(r)){this.poll?.unsubscribe();this.requestKey=undefined;this.uncertain.set(false);this.history.update(rows=>[r,...rows.filter(x=>x.id!==r.id)]);}
    },error:()=>{this.uncertain.set(true);this.error.set('UNKNOWN run status. No automatic provider retry.');this.poll?.unsubscribe();}});
    // Timer subscription precedes synchronous test transports so terminal reads can cancel it.
    this.poll=timer(5000,5000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(read);read();
  }
}
