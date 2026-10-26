import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, JsonPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryReviewDetail, DiscoveryReviewList, DiscoveryQualityReport, ReviewHistory, HumanReviewWrite } from './discovery-review.models';
import { DiscoveryQualityComponent } from './discovery-quality.component';
import { DiscoveryJobInformationComponent, discoveryTitle, evidenceText } from './discovery-job-information.component';
import { safeAdminError } from '../shared/admin-api';

@Component({
  selector: 'app-discovery-review-page', standalone: true, imports: [FormsModule, RouterLink, DatePipe, JsonPipe, DiscoveryQualityComponent, DiscoveryJobInformationComponent],
  styleUrl: './discovery-review-page.component.css',
  template: `
    <main class="admin-page discovery-page space-y-5">
      <header class="page-header"><div><p class="eyebrow">JOB HUNTING / DISCOVERY</p><h1>Discovery Review</h1>
        <p>Kiểm tra cơ hội và đối chiếu nhận định classifier với quyết định của bạn.</p></div>
        <button class="admin-button" type="button" (click)="load()" [disabled]="loading() || savingReview()">Làm mới</button></header>
      <p class="notice">Facebook chỉ là nguồn phát hiện. Phân loại không xác minh công ty và không cho phép tự động ứng tuyển.</p>
      @if(error()){<p role="alert" class="rounded border border-red-400 p-3">{{error()}}</p>}
      @if(message()){<p role="status">{{message()}}</p>}
      @if(loading()){<p role="status">Đang tải…</p>}
      @if(detail(); as d){
        <a routerLink="/admin/job-hunting/discovery" class="underline">← Danh sách</a>
        <section class="detail-layout space-y-3 rounded border p-4">
          <header class="job-heading"><h2>{{title(d.item.extracted)}}</h2><p>{{text(d.item.extracted['company']) || 'Chưa xác định công ty'}} · {{text(d.item.extracted['location']) || 'Chưa xác định địa điểm'}}</p>
            <p>Thu thập {{d.item.evaluatedAt | date:'medium'}} · Ngày đăng: {{d.item.publishedAt ? (d.item.publishedAt | date:'medium') : 'UNKNOWN'}}</p></header>
          @if(safeLink(d.item.sourceUrl); as link){<a [href]="link" target="_blank" rel="noopener noreferrer" class="underline">URL nguồn (chưa xác minh)</a>}
          @else{<p>Không có URL Facebook an toàn để mở.</p>}
          <section class="job-facts"><h3>Thông tin công việc</h3><app-discovery-job-information [data]="d.item.extracted" /></section>
          <details open class="original-post"><summary>Nội dung gốc</summary><pre class="whitespace-pre-wrap break-words" data-testid="raw-content">{{d.item.rawContent ?? 'Không lưu nội dung không an toàn/không có text.'}}</pre></details>
          <h3 class="section-heading">Đánh giá AI / deterministic classifier</h3>
          <h3 class="font-semibold">Content validity: {{d.item.contentValidity.status}}</h3>
          <p>{{d.item.contentValidity.explanation}}</p><p>{{d.item.contentValidity.reasonCodes.join(', ')}}</p>
          @for(s of d.item.contentValidity.evidenceSnippets; track $index){<blockquote class="border-l-2 pl-3">{{s}}</blockquote>}
          <h3 class="font-semibold">Target matching: {{d.item.targetMatching.status}}</h3>
          <p>{{d.item.targetMatching.explanation}}</p><p>{{d.item.targetMatching.reasonCodes.join(', ')}}</p>
          @for(s of d.item.targetMatching.evidenceSnippets; track $index){<blockquote class="border-l-2 pl-3">{{s}}</blockquote>}
          <p>Source warnings: {{d.item.sourceWarnings.join(', ') || 'Không'}} · Cùng nội dung: {{d.duplicateCount}}</p>
          <p>Thiếu/chưa rõ: {{d.item.missingFields.join(', ') || 'Không ghi nhận'}}</p>
          <details class="technical"><summary>Technical Details</summary><p>Run {{d.runId}} · trang {{d.pageNumber}} · item {{d.item.itemIndex}}</p><p>{{d.query}} · đồng bộ {{d.ingestedAt | date:'medium'}}</p>
            <p>Provider ID: {{d.item.providerPostId ?? 'Thiếu'}} · Company verification: {{d.item.verificationStatus}}</p>
            <pre class="overflow-auto whitespace-pre-wrap">{{d.item.extracted | json}}</pre>
            <p>{{d.item.classifierVersion}} · Profile {{d.item.profileFingerprint ?? 'UNKNOWN'}} · Preferences {{d.item.preferencesFingerprint ?? 'UNKNOWN'}}</p></details>
          <p>Chỉ xem xét; không tạo JobPosting, không ứng tuyển, không xác minh công ty.</p>
          <section class="space-y-3 border-t pt-4" aria-label="Human review">
            <h3 class="text-lg font-semibold">Human Review</h3><p>Classifier prediction vs Human decision</p>
            <p>Classifier: {{d.item.contentValidity.status}} / {{d.item.targetMatching.status}}</p>
            <p>Admin: {{d.humanReview?.status ?? 'UNREVIEWED'}} · version {{d.humanReview?.version ?? 0}} · {{d.humanReview?.contentStatus ?? 'Chưa có nhãn'}} / {{d.humanReview?.targetStatus ?? 'Chưa có nhãn'}}</p>
            <p>{{d.humanReview?.notes}} · {{d.humanReview?.reviewedAt | date:'medium'}} · Admin {{d.humanReview?.reviewedByAdminId}}</p>
            <p>Đánh giá nội dung không phải xác minh công ty hoặc cho phép ứng tuyển.</p>
            <label class="block">Content decision <select [(ngModel)]="draft(d).contentStatus" [disabled]="savingReview()" class="rounded border p-2">@for(s of contentStatuses;track s){<option>{{s}}</option>}</select></label>
            <label class="block">Target decision <select [(ngModel)]="draft(d).targetStatus" [disabled]="savingReview()" class="rounded border p-2">@for(s of targetStatuses;track s){<option>{{s}}</option>}</select></label>
            <label class="block" for="human-notes">Lý do (bắt buộc)</label><textarea id="human-notes" [(ngModel)]="draft(d).notes" [disabled]="savingReview()" maxlength="2000" rows="3" class="w-full rounded border p-2"></textarea>
            <div class="flex flex-wrap gap-3">
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('CONFIRMED')" class="rounded border p-3">Xác nhận prediction gốc</button>
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('CORRECTED')" class="rounded border p-3">Lưu sửa phân loại</button>
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('UNCERTAIN')" class="rounded border p-3">Chưa thể kết luận</button>
            </div>
            @if(reviewConflict()){<p role="alert">Review đã thay đổi. Draft được giữ lại; tải lại quyết định trước khi tự lưu lần nữa.</p><button type="button" (click)="load()" [disabled]="loading()">Tải lại quyết định</button>}
            <button type="button" (click)="loadHistory(1)" [disabled]="historyLoading()">Xem lịch sử review</button>
            @if(history();as h){<ol>@for(e of h.items;track e.version){<li class="border-t py-2">v{{e.version}} · {{e.status}} · {{e.contentStatus ?? '—'}} / {{e.targetStatus ?? '—'}}<p>{{e.notes}}</p><p>{{e.reviewedAt | date:'medium'}} · Admin {{e.reviewedByAdminId}}</p></li>}</ol>
              <p>{{h.total}} lần đánh giá</p><button (click)="loadHistory(h.page-1)" [disabled]="h.page<=1 || historyLoading()">Trước</button><button (click)="loadHistory(h.page+1)" [disabled]="h.page>=h.totalPages || historyLoading()">Sau</button>}
          </section>
        </section>
      }@else if(!detailMode()){
        <details class="import-fallback rounded border p-4"><summary>Import thủ công dự phòng</summary>
          <label for="review-import" class="block font-semibold">Review export JSON từ local (không cần cho automatic sync)</label>
          <input id="review-import" type="file" accept=".json,application/json" [disabled]="importing()" (change)="chooseFile($event)" />
          <button type="button" class="ml-3 rounded border px-3 py-2" [disabled]="!selected() || importing()" (click)="importFile()">{{importing() ? 'Đang nhập…' : 'Import để review'}}</button>
          <p>Chỉ export đã kiểm soát, tối đa 8 MiB. Không upload credentials hoặc raw provider response.</p>
        </details>
        <section class="flex flex-wrap gap-3" aria-label="Bộ lọc">
          <label>Human review <select [(ngModel)]="reviewState" (ngModelChange)="filter()"><option value="">Tất cả</option>@for(s of reviewStates;track s){<option>{{s}}</option>}</select></label>
          <label>Content <select [(ngModel)]="contentStatus" (ngModelChange)="filter()" class="rounded border p-2"><option value="">Tất cả</option><option>VALID_RECRUITMENT</option><option>INVALID_RECRUITMENT</option><option>NEEDS_REVIEW</option></select></label>
          <label>Target <select [(ngModel)]="targetStatus" (ngModelChange)="filter()" class="rounded border p-2"><option value="">Tất cả</option>@for(s of targetStatuses; track s){<option>{{s}}</option>}</select></label>
          <label>Run <input [(ngModel)]="runId" class="rounded border p-2" maxlength="100" /></label><button type="button" (click)="filter()" class="rounded border px-3">Lọc</button>
          <label>Ngày đồng bộ (giờ local) <input type="date" [(ngModel)]="collectedDate" (ngModelChange)="filter()" class="rounded border p-2" /></label>
          <label><input type="checkbox" [(ngModel)]="identityOnly" (ngModelChange)="filter()" /> Identity warnings</label>
          <label><input type="checkbox" [(ngModel)]="duplicatesOnly" (ngModelChange)="filter()" /> Cùng nội dung</label>
        </section>
        @if(list(); as data){
          <p role="status">Tập đang lọc — Tổng {{data.counts.total}} · Valid {{data.counts.valid}} · Invalid {{data.counts.invalid}} · Needs review {{data.counts.needsReview}} · Target {{data.counts.targetMatch}} · Identity warnings {{data.counts.identityWarnings}} · Duplicate items {{data.counts.duplicateItems}}</p>
          <p class="muted">Ưu tiên TARGET_MATCH chưa review. Giữ cả INVALID, NOT_MATCHED, malformed và duplicate.</p>
          <div class="candidate-list"><table class="w-full text-left"><thead><tr><th>Cơ hội / nguồn</th><th>Thời gian</th><th>Content / Target / Human</th><th>Reasons / warnings</th></tr></thead><tbody>
            @for(d of data.results.items; track d.id){<tr><td><small>{{text(d.item.extracted['company']) || 'Chưa xác định công ty'}}</small><a [routerLink]="['/admin/job-hunting/discovery',d.id]">{{title(d.item.extracted)}}</a><p>{{text(d.item.extracted['location']) || 'Chưa rõ địa điểm'}} · {{seniority(d)}}</p><p>{{text(d.item.extracted['technologies']) || 'Chưa rõ công nghệ'}}</p><small>Run {{d.runId}} / {{d.pageNumber}} / {{d.item.itemIndex}}</small></td><td><small>Ngày đăng</small><p>{{d.item.publishedAt ? (d.item.publishedAt | date:'short') : 'UNKNOWN'}}</p><small>Thu thập</small><p>{{d.item.evaluatedAt | date:'short'}}</p></td><td><span [class.target]="d.item.targetMatching.status==='TARGET_MATCH'">{{d.item.targetMatching.status}}</span><span>{{d.item.contentValidity.status}}</span><strong>{{d.humanReview?.status ?? 'UNREVIEWED'}}</strong></td><td>{{d.item.contentValidity.reasonCodes.join(', ')}}<br />{{d.item.sourceWarnings.join(', ')}} @if(d.duplicateCount > 1){<span> · {{d.duplicateCount}} cùng nội dung</span>}</td></tr>}
          </tbody></table></div>
          @if(!data.results.total){<p>Chưa có observations trong tập đang lọc.</p>}
          <nav aria-label="Phân trang" class="flex items-center gap-4"><button type="button" [disabled]="page <= 1 || loading()" (click)="changePage(-1)">Trước</button><span>Trang {{page}} / {{data.results.totalPages}}</span><button type="button" [disabled]="page >= data.results.totalPages || loading()" (click)="changePage(1)">Sau</button></nav>
        }
        <button type="button" (click)="loadMetrics()" [disabled]="metricsLoading()" class="rounded border p-3">Xem quality metrics (run đang nhập, không theo review filters)</button>
        <app-discovery-quality [report]="metrics()" />
      }
    </main>`
})
export class DiscoveryReviewPageComponent {
  private api = inject(JobHuntingService); private route = inject(ActivatedRoute); private destroyRef = inject(DestroyRef);
  list = signal<DiscoveryReviewList | null>(null); detail = signal<DiscoveryReviewDetail | null>(null);
  detailMode = signal(false);
  error = signal(''); message = signal(''); loading = signal(false); importing = signal(false); selected = signal<File | null>(null);
  contentStatus = ''; targetStatus = ''; runId = ''; collectedDate = ''; identityOnly = false; duplicatesOnly = false; page = 1;
  title=discoveryTitle; text=evidenceText;
  seniority(d:DiscoveryReviewDetail){return this.title(d.item.extracted).match(/\b(?:intern(?:ship)?|fresher|junior|middle|senior|lead)\b/gi)?.join(' / ') || 'Chưa rõ cấp độ';}
  targetStatuses = ['TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'];
  contentStatuses = ['VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW'];
  reviewStates = ['REVIEWED','UNREVIEWED','CONFIRMED','CORRECTED','UNCERTAIN']; reviewState='';
  savingReview=signal(false); reviewConflict=signal(false); historyLoading=signal(false); metricsLoading=signal(false);
  history=signal<ReviewHistory|null>(null); metrics=signal<DiscoveryQualityReport|null>(null);
  private drafts:Record<string,{notes:string;contentStatus:string;targetStatus:string}>={};
  draft(d:DiscoveryReviewDetail){return this.drafts[d.id] ??= {notes:'',contentStatus:d.humanReview?.contentStatus ?? d.item.contentValidity.status,targetStatus:d.humanReview?.targetStatus ?? d.item.targetMatching.status};}
  saveReview(action:HumanReviewWrite['action']) {
    const d=this.detail();if(!d || this.savingReview() || this.reviewConflict())return;
    const draft=this.draft(d);if(!draft.notes.trim())return;
    const body:HumanReviewWrite={expectedVersion:d.humanReview?.version ?? 0,action,notes:draft.notes};
    if(action==='CORRECTED'){body.contentStatus=draft.contentStatus;body.targetStatus=draft.targetStatus;}
    this.savingReview.set(true);this.error.set('');
    this.api.saveDiscoveryReview(d.id,body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:review=>{
      this.savingReview.set(false);delete this.drafts[d.id];if(this.detail()?.id===d.id){this.detail.set({...d,humanReview:review});this.history.set(null);this.message.set('Đã lưu đánh giá.');}
    },error:e=>{this.savingReview.set(false);if(this.detail()?.id===d.id){this.error.set(safeAdminError(e));if(e?.status===409)this.reviewConflict.set(true);}}});
  }
  loadHistory(page:number){const d=this.detail();if(!d || this.historyLoading())return;this.historyLoading.set(true);
    const request=this.requestNumber;
    this.api.discoveryHistory(d.id,page).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:h=>{this.historyLoading.set(false);if(request===this.requestNumber)this.history.set(h);},error:e=>{this.historyLoading.set(false);if(request===this.requestNumber)this.error.set(safeAdminError(e));}});}
  loadMetrics(){if(this.metricsLoading())return;this.metricsLoading.set(true);this.metrics.set(null);
    this.api.discoveryMetrics(this.runId.trim()||undefined).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:m=>{this.metrics.set(m);this.metricsLoading.set(false);},error:e=>{this.error.set(safeAdminError(e));this.metricsLoading.set(false);}});}
  private requestNumber = 0;
  constructor() { this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load()); }
  filter() { this.page = 1; this.load(); }
  changePage(delta: number) { this.page += delta; this.load(); }
  load() {
    const request = ++this.requestNumber; this.loading.set(true); this.error.set('');
    const id = this.route.snapshot.paramMap.get('id');
    this.detailMode.set(!!id); this.detail.set(null);
    this.history.set(null);
    const params: Record<string,string|number|boolean> = { page: this.page, pageSize: 20 };
    if (this.contentStatus) params['contentStatus'] = this.contentStatus;
    if (this.targetStatus) params['targetStatus'] = this.targetStatus;
    if (this.runId.trim()) params['runId'] = this.runId.trim();
    if (this.identityOnly) params['identityWarning'] = true;
    if (this.duplicatesOnly) params['duplicatesOnly'] = true;
    if (this.reviewState) params['reviewState'] = this.reviewState;
    if(this.collectedDate){const from=new Date(`${this.collectedDate}T00:00:00`),before=new Date(from);before.setDate(before.getDate()+1);params['collectedFrom']=from.toISOString();params['collectedBefore']=before.toISOString();}
    const failed = (e: unknown) => { if(request === this.requestNumber){this.error.set(safeAdminError(e)); this.loading.set(false);} };
    if (id) this.api.discoveryItem(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:d=>{if(request===this.requestNumber){this.detail.set(d);this.reviewConflict.set(false);this.loading.set(false);}},error:failed});
    else {this.detail.set(null);this.api.discovery(params).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:d=>{if(request===this.requestNumber){this.list.set(d);this.loading.set(false);}},error:failed});}
  }
  chooseFile(event: Event) { this.error.set(''); this.selected.set((event.target as HTMLInputElement).files?.[0] ?? null); }
  async importFile() {
    const file = this.selected(); if(!file || this.importing()) return;
    if(file.size > 8*1024*1024){this.error.set('File vượt quá 8 MiB.');return;}
    this.importing.set(true);this.error.set('');this.message.set('');
    try {
      const payload: unknown = JSON.parse(await file.text());
      this.api.importDiscovery(payload).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next:r=>{this.importing.set(false);this.selected.set(null);this.message.set(`Đã thêm ${r.inserted}; giữ nguyên ${r.unchanged}.`);this.filter();},
        error:e=>{this.importing.set(false);this.error.set(safeAdminError(e));}
      });
    } catch {this.importing.set(false);this.error.set('File JSON không hợp lệ.');}
  }
  safeLink(value: string | null): string | null {
    if(!value)return null;
    try {const u=new URL(value);return u.protocol==='https:' && ['facebook.com','www.facebook.com','m.facebook.com'].includes(u.hostname) && !u.username && !u.password && !u.port && !u.search && !u.hash && /^\/(?:groups\/[A-Za-z0-9.]+\/(?:posts|permalink)|[^/]+\/posts)\/(?:\d+|pfbid[A-Za-z0-9]+)\/?$/u.test(u.pathname)?value:null;} catch{return null;}
  }
}
