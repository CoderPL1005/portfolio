import {Component,DestroyRef,OnInit,inject,output,signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {JobHuntingService} from './job-hunting.service';
import {JobDataResetPlan} from './discovery-cleanup.models';
import {safeAdminError} from '../shared/admin-api';

@Component({selector:'app-discovery-cleanup',standalone:true,imports:[FormsModule],template:`
  <section class="rounded border border-red-300 p-4 space-y-3" aria-label="Reset All Job Data">
    <h2>Reset All Job Data</h2>
    <p>Xóa vĩnh viễn TẤT CẢ runs, Discovery reviews/history, Jobs, Raw Jobs, attachment metadata, Applications, application documents/events và SubmissionAttempts/events.</p>
    <p>Không xóa Admin, Portfolio, Projects, Skills, preferences, canonical CV, file R2, SocialAPIs ledger/credits, GitHub history hay checkpoints. Không hủy các ứng tuyển đã gửi bên ngoài.</p>
    @if(error()){<p role="alert">{{error()}}</p>}@if(message()){<p role="status">{{message()}}</p>}
    <button type="button" (click)="refresh()" [disabled]="busy()">Xem lại số lượng sẽ xóa</button>
    @if(plan();as p){
      <ul>@for(row of rows();track row[0]){<li>{{row[0]}}: {{row[1]}}</li>}</ul>
      <p>Tổng số bản ghi: {{p.totalRecords}}</p>
      @if(p.totalRecords===0){<p>Discovery và Jobs hiện có 0 bản ghi. Không cần reset.</p>}
      @else{
        <label class="block">Nhập RESET ALL JOB DATA để xác nhận <input [(ngModel)]="confirmation" [disabled]="busy()" autocomplete="off" /></label>
        <button type="button" (click)="execute()" [disabled]="busy() || confirmation !== 'RESET ALL JOB DATA'">Xóa toàn bộ Job Data</button>
      }
    }@else{<p>Database counts: UNKNOWN cho đến khi API tải thành công.</p>}
  </section>`})
export class DiscoveryCleanupComponent implements OnInit {
  private api=inject(JobHuntingService);private destroy=inject(DestroyRef);
  changed=output<void>();plan=signal<JobDataResetPlan|null>(null);busy=signal(false);error=signal('');message=signal('');confirmation='';
  rows(){return Object.entries(this.plan()?.counts??{});}
  ngOnInit(){this.refresh();}
  refresh(){if(this.busy())return;this.plan.set(null);this.confirmation='';this.error.set('');this.busy.set(true);
    this.api.jobDataResetPreview().pipe(takeUntilDestroyed(this.destroy)).subscribe({next:p=>{this.plan.set(p);this.busy.set(false);},error:e=>{this.error.set(safeAdminError(e));this.busy.set(false);}});}
  execute(){const p=this.plan();if(this.busy()||!p?.totalRecords||this.confirmation!=='RESET ALL JOB DATA')return;
    this.busy.set(true);this.error.set('');this.message.set('');
    this.api.resetAllJobData({confirmation:this.confirmation,expectedFingerprint:p.fingerprint}).pipe(takeUntilDestroyed(this.destroy))
      .subscribe({next:r=>{this.plan.set(r.remaining);this.confirmation='';this.busy.set(false);this.message.set(`Đã xóa ${r.deleted.totalRecords} bản ghi. Còn lại ${r.remaining.totalRecords}.`);this.changed.emit();},
        error:e=>{this.plan.set(null);this.confirmation='';this.busy.set(false);this.error.set(safeAdminError(e)+' Xem lại counts rồi xác nhận lại; không tự retry.');}});}
}
