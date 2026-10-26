import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, JsonPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryReviewDetail, DiscoveryReviewList, DiscoveryQualityReport, ReviewHistory, HumanReviewWrite } from './discovery-review.models';
import { DiscoveryQualityComponent } from './discovery-quality.component';
import { safeAdminError } from '../shared/admin-api';

@Component({
  selector: 'app-discovery-review-page', standalone: true, imports: [FormsModule, RouterLink, DatePipe, JsonPipe, DiscoveryQualityComponent],
  template: `
    <main class="space-y-5 p-4 sm:p-6">
      <header><h1 class="text-2xl font-semibold">Discovery Review</h1>
        <p>Facebook chỉ là nguồn phát hiện cơ hội. Phân loại nội dung không xác nhận công ty đang tuyển dụng.</p></header>
      @if(error()){<p role="alert" class="rounded border border-red-400 p-3">{{error()}}</p>}
      @if(message()){<p role="status">{{message()}}</p>}
      @if(loading()){<p role="status">Đang tải…</p>}
      @if(detail(); as d){
        <a routerLink="/admin/job-hunting/discovery" class="underline">← Danh sách</a>
        <section class="space-y-3 rounded border p-4">
          <h2 class="text-lg font-semibold">Run {{d.runId}} · trang {{d.pageNumber}} · item {{d.item.itemIndex}}</h2>
          <p>{{d.query}} · nhập {{d.ingestedAt | date:'medium'}}</p>
          <p>Provider ID: {{d.item.providerPostId ?? 'Thiếu'}} · Company verification: {{d.item.verificationStatus}}</p>
          @if(safeLink(d.item.sourceUrl); as link){<a [href]="link" target="_blank" rel="noopener noreferrer" class="underline">URL nguồn (chưa xác minh)</a>}
          @else{<p>Không có URL Facebook an toàn để mở.</p>}
          <h3 class="font-semibold">Nội dung gốc</h3><pre class="whitespace-pre-wrap break-words" data-testid="raw-content">{{d.item.rawContent ?? 'Không lưu nội dung không an toàn/không có text.'}}</pre>
          <h3 class="font-semibold">Content validity: {{d.item.contentValidity.status}}</h3>
          <p>{{d.item.contentValidity.explanation}}</p><p>{{d.item.contentValidity.reasonCodes.join(', ')}}</p>
          @for(s of d.item.contentValidity.evidenceSnippets; track $index){<blockquote class="border-l-2 pl-3">{{s}}</blockquote>}
          <h3 class="font-semibold">Target matching: {{d.item.targetMatching.status}}</h3>
          <p>{{d.item.targetMatching.explanation}}</p><p>{{d.item.targetMatching.reasonCodes.join(', ')}}</p>
          @for(s of d.item.targetMatching.evidenceSnippets; track $index){<blockquote class="border-l-2 pl-3">{{s}}</blockquote>}
          <p>Source warnings: {{d.item.sourceWarnings.join(', ') || 'Không'}} · Cùng nội dung: {{d.duplicateCount}}</p>
          <p>Thiếu/chưa rõ: {{d.item.missingFields.join(', ') || 'Không ghi nhận'}}</p>
          <h3 class="font-semibold">Thông tin trích xuất (chưa xác minh)</h3><pre class="overflow-auto whitespace-pre-wrap">{{d.item.extracted | json}}</pre>
          <p>{{d.item.classifierVersion}} · {{d.item.evaluatedAt | date:'medium'}} · Ngày đăng: {{d.item.publishedAt ?? 'UNKNOWN'}}</p>
          <p class="break-all">Classifier context · Profile {{d.item.profileFingerprint ?? 'UNKNOWN'}} · Preferences {{d.item.preferencesFingerprint ?? 'UNKNOWN'}}</p>
          <p>Chỉ xem xét; không tạo JobPosting, không ứng tuyển, không xác minh công ty.</p>
          <section class="space-y-3 border-t pt-4" aria-label="Human review">
            <h3 class="text-lg font-semibold">Classifier prediction vs Human decision</h3>
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
        <section class="rounded border p-4">
          <label for="review-import" class="block font-semibold">Import review export JSON từ local</label>
          <input id="review-import" type="file" accept=".json,application/json" [disabled]="importing()" (change)="chooseFile($event)" />
          <button type="button" class="ml-3 rounded border px-3 py-2" [disabled]="!selected() || importing()" (click)="importFile()">{{importing() ? 'Đang nhập…' : 'Import để review'}}</button>
          <p>Chỉ export đã kiểm soát, tối đa 8 MiB. Không upload credentials hoặc raw provider response.</p>
        </section>
        <section class="flex flex-wrap gap-3" aria-label="Bộ lọc">
          <label>Human review <select [(ngModel)]="reviewState" (ngModelChange)="filter()"><option value="">Tất cả</option>@for(s of reviewStates;track s){<option>{{s}}</option>}</select></label>
          <label>Content <select [(ngModel)]="contentStatus" (ngModelChange)="filter()" class="rounded border p-2"><option value="">Tất cả</option><option>VALID_RECRUITMENT</option><option>INVALID_RECRUITMENT</option><option>NEEDS_REVIEW</option></select></label>
          <label>Target <select [(ngModel)]="targetStatus" (ngModelChange)="filter()" class="rounded border p-2"><option value="">Tất cả</option>@for(s of targetStatuses; track s){<option>{{s}}</option>}</select></label>
          <label>Run <input [(ngModel)]="runId" class="rounded border p-2" maxlength="100" /></label><button type="button" (click)="filter()" class="rounded border px-3">Lọc</button>
          <label><input type="checkbox" [(ngModel)]="identityOnly" (ngModelChange)="filter()" /> Identity warnings</label>
          <label><input type="checkbox" [(ngModel)]="duplicatesOnly" (ngModelChange)="filter()" /> Cùng nội dung</label>
        </section>
        @if(list(); as data){
          <p role="status">Tập đang lọc — Tổng {{data.counts.total}} · Valid {{data.counts.valid}} · Invalid {{data.counts.invalid}} · Needs review {{data.counts.needsReview}} · Target {{data.counts.targetMatch}} · Identity warnings {{data.counts.identityWarnings}} · Duplicate items {{data.counts.duplicateItems}}</p>
          <div class="overflow-x-auto rounded border"><table class="w-full text-left"><thead><tr><th class="p-3">Run / item</th><th>Query / nhập lúc</th><th>Content / Target</th><th>Reasons / warnings</th></tr></thead><tbody>
            @for(d of data.results.items; track d.id){<tr class="border-t"><td class="p-3"><a class="underline" [routerLink]="['/admin/job-hunting/discovery',d.id]">{{d.runId}} / {{d.pageNumber}} / {{d.item.itemIndex}}</a></td><td>{{d.query}}<br />{{d.ingestedAt | date:'short'}}</td><td>{{d.item.contentValidity.status}}<br />{{d.item.targetMatching.status}}</td><td>{{d.item.contentValidity.reasonCodes.join(', ')}}<br />{{d.item.sourceWarnings.join(', ')}} @if(d.duplicateCount > 1){<span> · {{d.duplicateCount}} cùng nội dung</span>}</td></tr>}
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
  contentStatus = ''; targetStatus = ''; runId = ''; identityOnly = false; duplicatesOnly = false; page = 1;
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
    this.api.saveDiscoveryReview(d.id,body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:()=>{
      this.savingReview.set(false);delete this.drafts[d.id];if(this.detail()?.id===d.id){this.message.set('Đã lưu đánh giá.');this.load();}
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
