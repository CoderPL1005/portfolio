import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, JsonPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest } from 'rxjs';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryReviewDetail, DiscoveryReviewList, DiscoveryQualityReport, ReviewHistory, HumanReviewWrite } from './discovery-review.models';
import { DiscoveryQualityComponent } from './discovery-quality.component';
import { DiscoveryJobInformationComponent, discoveryTitle, evidenceText } from './discovery-job-information.component';
import { safeAdminError } from '../shared/admin-api';
import { DiscoveryExecutionComponent } from './discovery-execution.component';

@Component({
  selector: 'app-discovery-review-page', standalone: true, imports: [FormsModule, RouterLink, DatePipe, JsonPipe, DiscoveryQualityComponent, DiscoveryJobInformationComponent, DiscoveryExecutionComponent],
  styleUrl: './discovery-review-page.component.css',
  template: `
    <main class="admin-page discovery-page">
      <header class="page-header"><div><p class="eyebrow">JOB HUNTING / DISCOVERY</p><h1>Cơ hội việc làm</h1>
        <p>Khám phá bài tuyển dụng và đánh giá cơ hội theo mục tiêu của bạn.</p></div>
        <button class="admin-button" type="button" (click)="load()" [disabled]="loading() || savingReview()">Làm mới</button></header>
      @if(!detailMode()){<app-discovery-execution />}
      @if(error()){<p role="alert" class="admin-error feedback">{{error()}}</p>}
      @if(message()){<p role="status">{{message()}}</p>}
      @if(loading()){<p role="status">Đang tải…</p>}
      @if(detail(); as d){
        <a routerLink="/admin/job-hunting/discovery" class="back-link">← Tất cả cơ hội</a>
        <section class="detail-layout">
          <header class="job-heading"><h2>{{title(d.item.extracted)}}</h2><p>{{text(d.item.extracted['company']) || 'Chưa xác định công ty'}} · {{text(d.item.extracted['location']) || 'Chưa xác định địa điểm'}}</p>
            <p>{{d.item.publishedAt ? ('Ngày đăng: ' + (d.item.publishedAt | date:'medium')) : 'Chưa xác định ngày đăng'}}</p>
            <div class="badges"><span class="status-badge">{{label(d.item.targetMatching.status)}}</span><span class="status-badge neutral">{{label(d.humanReview?.status ?? 'UNREVIEWED')}}</span></div></header>
          <section class="detail-section job-facts"><h3>Thông tin tuyển dụng</h3><app-discovery-job-information [data]="d.item.extracted" /></section>
          <section class="detail-section original-post"><h3>Nội dung bài đăng gốc</h3>
            @if(safeLink(d.item.sourceUrl); as link){<a [href]="link" target="_blank" rel="noopener noreferrer" class="source-link">Mở bài viết Facebook ↗</a>}
            @else{<p>Không có URL Facebook an toàn để mở.</p>}
            <p class="muted">Nguồn bài đăng chưa được xác minh độc lập.</p>
            <pre data-testid="raw-content">{{d.item.rawContent ?? 'Không lưu nội dung không an toàn/không có text.'}}</pre></section>
          <section class="detail-section assessment"><h3>Đánh giá AI / bộ phân loại</h3>
          <h4>Nội dung: {{label(d.item.contentValidity.status)}}</h4>
          <p>{{d.item.contentValidity.explanation}}</p>
          @for(s of d.item.contentValidity.evidenceSnippets; track $index){<blockquote>{{s}}</blockquote>}
          <h4>Mức độ phù hợp: {{label(d.item.targetMatching.status)}}</h4>
          <p>{{d.item.targetMatching.explanation}}</p>
          @for(s of d.item.targetMatching.evidenceSnippets; track $index){<blockquote>{{s}}</blockquote>}
          <p class="muted">Nhận định tự động không xác minh doanh nghiệp, ngày còn tuyển hoặc cho phép ứng tuyển.</p></section>
          <section class="detail-section human-review admin-form" aria-label="Human review">
            <h3>Đánh giá của bạn · Human Review</h3><p>Đối chiếu nhận định tự động với nội dung bạn đã kiểm tra.</p>
            <div class="comparison"><div><h4>Nhận định classifier</h4><p>{{label(d.item.contentValidity.status)}} / {{label(d.item.targetMatching.status)}}</p></div>
            <div><h4>Quyết định Admin</h4><p>{{label(d.humanReview?.status ?? 'UNREVIEWED')}} · phiên bản {{d.humanReview?.version ?? 0}}</p><p>{{label(d.humanReview?.contentStatus ?? '')}} / {{label(d.humanReview?.targetStatus ?? '')}}</p></div></div>
            @if(d.humanReview?.notes){<p>{{d.humanReview?.notes}} · {{d.humanReview?.reviewedAt | date:'medium'}} · Admin {{d.humanReview?.reviewedByAdminId}}</p>}
            <p>Đánh giá nội dung không phải xác minh công ty hoặc cho phép ứng tuyển.</p>
            <div class="admin-form-grid"><label>Kết luận nội dung <select [(ngModel)]="draft(d).contentStatus" [disabled]="savingReview()">@for(s of contentStatuses;track s){<option [value]="s">{{label(s)}}</option>}</select></label>
            <label>Kết luận phù hợp <select [(ngModel)]="draft(d).targetStatus" [disabled]="savingReview()">@for(s of targetStatuses;track s){<option [value]="s">{{label(s)}}</option>}</select></label></div>
            <label for="human-notes">Lý do (bắt buộc)</label><textarea id="human-notes" [(ngModel)]="draft(d).notes" [disabled]="savingReview()" maxlength="2000" rows="3"></textarea>
            <p class="muted">Xác nhận giữ nguyên nhận định gốc. Lưu sửa sử dụng hai kết luận đã chọn. Chưa thể kết luận không tạo nhãn xác nhận.</p>
            <div class="admin-actions">
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('CONFIRMED')" class="admin-button primary">Xác nhận nhận định gốc</button>
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('CORRECTED')" class="admin-button">Lưu sửa phân loại</button>
              <button type="button" [disabled]="!draft(d).notes.trim() || savingReview() || reviewConflict()" (click)="saveReview('UNCERTAIN')" class="admin-button">Chưa thể kết luận</button>
            </div>
            @if(reviewConflict()){<p role="alert">Review đã thay đổi. Draft được giữ lại; tải lại quyết định trước khi tự lưu lần nữa.</p><button type="button" class="admin-button" (click)="load()" [disabled]="loading()">Tải lại quyết định</button>}
            <button type="button" class="admin-button" (click)="loadHistory(1)" [disabled]="historyLoading()">Xem lịch sử đánh giá</button>
            @if(history();as h){<ol class="review-history">@for(e of h.items;track e.version){<li>v{{e.version}} · {{label(e.status)}} · {{label(e.contentStatus ?? '')}} / {{label(e.targetStatus ?? '')}}<p>{{e.notes}}</p><p>{{e.reviewedAt | date:'medium'}} · Admin {{e.reviewedByAdminId}}</p></li>}</ol>
              <p>{{h.total}} lần đánh giá</p><div class="admin-actions"><button type="button" class="admin-button" (click)="loadHistory(h.page-1)" [disabled]="h.page<=1 || historyLoading()">Trước</button><button type="button" class="admin-button" (click)="loadHistory(h.page+1)" [disabled]="h.page>=h.totalPages || historyLoading()">Sau</button></div>}
          </section>
          <details class="technical detail-section"><summary>Thông tin kỹ thuật · Technical Details</summary>
            <p>Run {{d.runId}} · trang {{d.pageNumber}} · item {{d.item.itemIndex}}</p><p>{{d.query}} · đồng bộ {{d.ingestedAt | date:'medium'}}</p>
            <p>Provider ID: {{d.item.providerPostId ?? 'Thiếu'}} · Company verification: {{d.item.verificationStatus}}</p>
            <p>Content: {{d.item.contentValidity.reasonCodes.join(', ')}} · Target: {{d.item.targetMatching.reasonCodes.join(', ')}}</p>
            <p>Source warnings: {{d.item.sourceWarnings.join(', ') || 'Không'}} · Cùng nội dung: {{d.duplicateCount}}</p>
            <p>Thiếu/chưa rõ: {{d.item.missingFields.join(', ') || 'Không ghi nhận'}}</p>
            <p>Freshness: {{d.item.publishedAt ? 'UNVERIFIED_PROVIDER_TIMESTAMP' : 'UNKNOWN'}} · Import: STORED · Phát hiện: {{d.item.evaluatedAt | date:'medium'}}</p>
            <pre>{{d.item.extracted | json}}</pre>
            <p>{{d.item.classifierVersion}} · Profile {{d.item.profileFingerprint ?? 'UNKNOWN'}} · Preferences {{d.item.preferencesFingerprint ?? 'UNKNOWN'}}</p>
          </details>
        </section>
      }@else if(!detailMode()){
        @if(list(); as data){@if(data.results.total > 0 && !loading()){
          <section class="summary" aria-label="Tổng quan trong phạm vi lọc">
            <div><span>Tổng cơ hội</span><strong>{{data.counts.total}}</strong></div>
            <div><span>Phù hợp</span><strong>{{data.counts.targetMatch}}</strong></div>
            <div><span>Cần đánh giá</span><strong>{{data.counts.needsReview}}</strong><small>Nội dung cần xem xét</small></div>
          </section><p class="scope-note">Số liệu theo bộ lọc hiện tại; mỗi bài ghi nhận chưa chắc là một việc làm riêng biệt.</p>
        }}
        @if(listControlsVisible()){
        <nav class="admin-tabs filter-tabs" aria-label="Lọc cơ hội">
          @for(tab of tabs;track tab.id){<button type="button" class="admin-button" [class.primary]="activeTab()===tab.id" [attr.aria-pressed]="activeTab()===tab.id" (click)="selectTab(tab.id)">{{tab.label}}</button>}
        </nav>
        <details class="advanced-filters"><summary>Bộ lọc bổ sung{{hasAdvancedFilters() ? ' · Đang áp dụng' : ''}}</summary>
        <section class="filter-grid admin-form" aria-label="Bộ lọc bổ sung">
          <label>Đánh giá của Admin <select [(ngModel)]="reviewState" (ngModelChange)="filter()"><option value="">Tất cả</option>@for(s of reviewStates;track s){<option [value]="s">{{label(s)}}</option>}</select></label>
          <label>Nội dung <select [(ngModel)]="contentStatus" (ngModelChange)="filter()"><option value="">Tất cả</option>@for(s of contentStatuses;track s){<option [value]="s">{{label(s)}}</option>}</select></label>
          <label>Mức độ phù hợp <select [(ngModel)]="targetStatus" (ngModelChange)="filter()"><option value="">Tất cả</option>@for(s of targetStatuses;track s){<option [value]="s">{{label(s)}}</option>}</select></label>
          <label>Phiên thu thập <input [(ngModel)]="runId" maxlength="100" /></label>
          <label>Ngày đồng bộ (giờ local) <input type="date" [(ngModel)]="collectedDate" (ngModelChange)="filter()" /></label>
          <div class="checks"><label><input type="checkbox" [(ngModel)]="identityOnly" (ngModelChange)="filter()" /> Có cảnh báo nguồn</label>
          <label><input type="checkbox" [(ngModel)]="duplicatesOnly" (ngModelChange)="filter()" /> Có nội dung trùng</label></div>
          <div class="admin-actions"><button type="button" (click)="filter()" class="admin-button">Áp dụng</button><button type="button" (click)="clearFilters()" class="admin-button">Xóa bộ lọc</button></div>
        </section></details>
        }
        @if(list(); as data){
          @if(!loading() && !error()){
            @if(data.results.total){
              <section class="candidate-list" aria-label="Danh sách cơ hội">
                @for(d of data.results.items;track d.id){<article class="job-card">
                  <div class="badges"><span class="status-badge" [class.match]="d.item.targetMatching.status==='TARGET_MATCH'">{{label(d.item.targetMatching.status)}}</span><span class="status-badge neutral">{{label(d.humanReview?.status ?? 'UNREVIEWED')}}</span></div>
                  <h2><a [routerLink]="['/admin/job-hunting/discovery',d.id]">{{title(d.item.extracted)}}</a></h2>
                  <p class="company">{{text(d.item.extracted['company']) || 'Chưa xác định công ty'}}</p>
                  <p class="muted">{{text(d.item.extracted['location']) || 'Chưa xác định địa điểm'}}</p>
                  <div class="job-tags">@if(text(d.item.extracted['technologies']);as tech){<span>{{tech}}</span>}@if(seniority(d);as level){<span>{{level}}</span>}</div>
                  <footer><span>{{d.item.publishedAt ? ('Ngày đăng: ' + (d.item.publishedAt | date:'shortDate')) : 'Chưa xác định ngày đăng'}}</span><a [routerLink]="['/admin/job-hunting/discovery',d.id]" [attr.aria-label]="'Xem chi tiết: ' + title(d.item.extracted)">Xem chi tiết →</a></footer>
                </article>}
              </section>
              @if(data.results.totalPages>1){<nav aria-label="Phân trang" class="pagination"><button class="admin-button" type="button" [disabled]="page <= 1 || loading()" (click)="changePage(-1)">Trước</button><span>Trang {{page}} / {{data.results.totalPages}}</span><button class="admin-button" type="button" [disabled]="page >= data.results.totalPages || loading()" (click)="changePage(1)">Sau</button></nav>}
            }@else{
              <section class="empty-state" role="status">
                <span class="empty-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M4 7h16v13H4zM8 7V4h8v3M4 12h16M10 12v3h4v-3" /></svg></span>
                <h2>{{hasFilters() ? 'Không có cơ hội khớp bộ lọc' : 'Chưa có cơ hội việc làm'}}</h2><p>{{hasFilters() ? 'Thử thay đổi bộ lọc để xem các bài tuyển dụng khác.' : 'Discovery chưa có kết quả. Bài tuyển dụng mới sẽ xuất hiện tại đây để bạn xem và đánh giá.'}}</p>
                @if(hasFilters()){<button type="button" class="admin-button" (click)="clearFilters()">Xóa bộ lọc</button>}
              </section>
            }
          }
        }
        @if(list()?.results?.total && !loading() && !error()){
        <details class="quality-section"><summary>Nâng cao · Chất lượng phân loại</summary><p class="muted">Thống kê từ đánh giá của Admin theo phiên thu thập đang chọn, không theo các bộ lọc phân loại. Không đại diện toàn bộ thị trường.</p>
          <p class="muted">Facebook chỉ là nguồn phát hiện. Phân loại không xác minh công ty, ngày còn tuyển hoặc cho phép tự động ứng tuyển.</p>
          <button type="button" (click)="loadMetrics()" [disabled]="metricsLoading()" class="admin-button">{{metricsLoading() ? 'Đang tải thống kê…' : 'Xem thống kê chất lượng'}}</button>
          <app-discovery-quality [report]="metrics()" />
        </details>
        }
      }
    </main>`
})
export class DiscoveryReviewPageComponent {
  private api = inject(JobHuntingService); private route = inject(ActivatedRoute); private destroyRef = inject(DestroyRef);
  list = signal<DiscoveryReviewList | null>(null); detail = signal<DiscoveryReviewDetail | null>(null);
  detailMode = signal(false);
  error = signal(''); message = signal(''); loading = signal(false);
  contentStatus = ''; targetStatus = ''; runId = ''; collectedDate = ''; identityOnly = false; duplicatesOnly = false; page = 1;
  title=discoveryTitle; text=evidenceText;
  seniority(d:DiscoveryReviewDetail){return this.title(d.item.extracted).match(/\b(?:intern(?:ship)?|fresher|junior|middle|senior|lead)\b/gi)?.join(' / ') || '';}
  readonly tabs = [{id:'all',label:'Tất cả'},{id:'matched',label:'Phù hợp'},{id:'needs-review',label:'Cần xem xét'},{id:'reviewed',label:'Đã đánh giá'}];
  label(value:string):string {
    const labels:Record<string,string>={TARGET_MATCH:'Phù hợp mục tiêu',FALLBACK_MATCH:'Cơ hội dự phòng',POTENTIAL_MATCH:'Có thể phù hợp',NOT_MATCHED:'Không phù hợp',INSUFFICIENT_DATA:'Chưa đủ dữ liệu',VALID_RECRUITMENT:'Có nội dung tuyển dụng',INVALID_RECRUITMENT:'Không phải tin tuyển dụng',NEEDS_REVIEW:'Cần xem xét nội dung',UNREVIEWED:'Chưa đánh giá',REVIEWED:'Đã đánh giá',CONFIRMED:'Đã xác nhận',CORRECTED:'Đã chỉnh sửa',UNCERTAIN:'Chưa thể kết luận'};
    return labels[value] ?? 'Chưa có nhãn';
  }
  activeTab(){
    if(!this.contentStatus && !this.targetStatus && !this.reviewState)return 'all';
    if(!this.contentStatus && this.targetStatus==='TARGET_MATCH' && !this.reviewState)return 'matched';
    if(this.contentStatus==='NEEDS_REVIEW' && !this.targetStatus && !this.reviewState)return 'needs-review';
    if(!this.contentStatus && !this.targetStatus && this.reviewState==='REVIEWED')return 'reviewed';
    return '';
  }
  selectTab(id:string){this.contentStatus=id==='needs-review'?'NEEDS_REVIEW':'';this.targetStatus=id==='matched'?'TARGET_MATCH':'';this.reviewState=id==='reviewed'?'REVIEWED':'';this.filter();}
  hasAdvancedFilters(){return !!(this.runId.trim() || this.collectedDate || this.identityOnly || this.duplicatesOnly || (this.hasFilters() && !this.activeTab()));}
  hasFilters(){return !!(this.contentStatus || this.targetStatus || this.reviewState || this.runId.trim() || this.collectedDate || this.identityOnly || this.duplicatesOnly);}
  listControlsVisible(){return !!this.list() && !this.loading() && !this.error() && (!!this.list()?.results.total || this.hasFilters());}
  clearFilters(){this.contentStatus=this.targetStatus=this.reviewState=this.runId=this.collectedDate='';this.identityOnly=this.duplicatesOnly=false;this.filter();}
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
  private metricsRequest = 0;
  loadMetrics(){if(this.metricsLoading())return;const request=++this.metricsRequest;
    this.metricsLoading.set(true);this.metrics.set(null);
    this.api.discoveryMetrics(this.runId.trim()||undefined).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:m=>{if(request===this.metricsRequest){this.metrics.set(m);this.metricsLoading.set(false);}},error:e=>{if(request===this.metricsRequest){this.error.set(safeAdminError(e));this.metricsLoading.set(false);}}});}
  private requestNumber = 0;
  constructor() { combineLatest([this.route.paramMap,this.route.queryParamMap]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(([,query]) => {
    this.runId = query.get('runId') ?? ''; this.page=1;
    this.list.set(null);
    this.metricsRequest++;this.metrics.set(null);this.metricsLoading.set(false);
    this.load();
  }); }
  filter() { this.page = 1; this.metricsRequest++;this.metrics.set(null);this.metricsLoading.set(false);this.load(); }
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
  safeLink(value: string | null): string | null {
    if(!value)return null;
    try {const u=new URL(value);return u.protocol==='https:' && ['facebook.com','www.facebook.com','m.facebook.com'].includes(u.hostname) && !u.username && !u.password && !u.port && !u.search && !u.hash && /^\/(?:groups\/[A-Za-z0-9.]+\/(?:posts|permalink)|[^/]+\/posts)\/(?:\d+|pfbid[A-Za-z0-9]+)\/?$/u.test(u.pathname)?value:null;} catch{return null;}
  }
}
