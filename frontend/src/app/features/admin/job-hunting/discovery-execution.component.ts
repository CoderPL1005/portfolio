import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, timer } from 'rxjs';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryExecution } from './discovery-execution.models';

@Component({
  selector: 'app-discovery-execution', standalone: true, imports: [RouterLink, DatePipe],
  template: `
    <section aria-label="Discovery execution" class="execution">
      <div class="admin-actions"><button class="admin-button primary" type="button" (click)="start()" [disabled]="blocked()">Run Job Discovery</button>
        <button class="admin-button" type="button" (click)="refresh()" [disabled]="starting()">Refresh run status</button></div>
      @if(error()){<p role="alert">{{error()}}</p>}
      <details><summary>Discovery · {{current()?.state ?? 'Trạng thái thu thập'}}</summary>
      <p class="muted">One provider request per execution. No automatic retry or application submission.</p>
      @if(current(); as run){
        <h2>Discovery: {{run.state}}</h2>
        <a routerLink="/admin/job-hunting/discovery" [queryParams]="{runId:run.id}">Xem bài đăng của phiên này</a>
        <p role="status">Requests: {{run.actualRequests ?? 'UNKNOWN'}} · Deducted credits: {{run.creditsDeducted ?? 'UNKNOWN'}}</p>
        @if(run.counts; as c){<p>Bài đăng đã lưu: {{c.fetched ?? 0}}</p>}
        @if(run.errorCode){<p>{{run.errorCode}}</p>}
      }
      @if(history().length){<details><summary>Recent executions</summary>@for(run of history();track run.id){<button class="admin-button" type="button" (click)="select(run.id)">{{run.createdAt | date:'short'}} · {{run.state}}</button>}</details>}
      </details>
    </section>`,
  styles: `:host{display:block}.execution{padding:1rem 1.25rem;background:var(--color-surface);border-radius:var(--radius-lg)}summary{cursor:pointer;font-weight:600}.admin-actions{margin-top:.75rem}button{margin:.25rem}p{overflow-wrap:anywhere}`
})
export class DiscoveryExecutionComponent {
  private api = inject(JobHuntingService);
  private destroyRef = inject(DestroyRef);
  private poll?: Subscription;
  private requestKey?: string;
  private observedVersion='';
  observationsChanged=output<void>();
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
      const fingerprint=JSON.stringify([r.id,(r as DiscoveryExecution&{version?:number}).version,r.state,r.counts?.fetched,r.counts?.validRecruitment]);
      if(fingerprint!==this.observedVersion){this.observedVersion=fingerprint;this.observationsChanged.emit();}
      if(this.terminal(r)){this.poll?.unsubscribe();this.requestKey=undefined;this.uncertain.set(false);this.history.update(rows=>[r,...rows.filter(x=>x.id!==r.id)]);}
    },error:()=>{this.uncertain.set(true);this.error.set('UNKNOWN run status. No automatic provider retry.');this.poll?.unsubscribe();}});
    // Timer subscription precedes synchronous test transports so terminal reads can cancel it.
    this.poll=timer(5000,5000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(read);read();
  }
}
