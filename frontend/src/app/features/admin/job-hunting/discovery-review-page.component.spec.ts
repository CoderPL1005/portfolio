import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, BehaviorSubject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { DiscoveryReviewDetail, DiscoveryReviewList, HumanReview, DiscoveryQualityReport, ReviewHistory, DimensionQuality } from './discovery-review.models';
import { JobHuntingService } from './job-hunting.service';

const detail = (): DiscoveryReviewDetail => ({id:'synthetic-1',runId:'synthetic-run',query:'Backend Hanoi',pageNumber:1,
  ingestedAt:'2026-10-26T03:00:00Z',duplicateCount:2,item:{itemIndex:0,providerPostId:'123',sourceUrl:'https://www.facebook.com/fixture/posts/pfbidExample',
    rawContent:'<script>alert(1)</script><img src=x onerror=alert(2)> Hiring Senior Java',publishedAt:null,
    contentValidity:{status:'VALID_RECRUITMENT',reasonCodes:['EMPLOYER_HIRING'],explanation:'Có nội dung tuyển dụng.',evidenceSnippets:['Hiring Senior Java']},
    targetMatching:{status:'NOT_MATCHED',reasonCodes:['SENIORITY_MISMATCH'],explanation:'Không phù hợp mục tiêu.',evidenceSnippets:[]},
    verificationStatus:'NOT_CHECKED',extracted:{company:null,technologies:['Java']},missingFields:['COMPANY','PUBLISHED_AT'],
    sourceWarnings:['POST_ID_URL_MISMATCH'],classifierVersion:'content-first-v1',evaluatedAt:'2026-10-26T03:00:00Z'}});
const listing = ():DiscoveryReviewList=>({results:{items:[detail()],page:1,pageSize:20,total:1,totalPages:1},
  counts:{total:1,valid:1,invalid:0,needsReview:0,targetMatch:0,identityWarnings:1,duplicateItems:1}});
function mount(id?:string,runId?:string) {
  const api={discovery:vi.fn(()=>of(listing())),discoveryItem:vi.fn(()=>of(detail())),importDiscovery:vi.fn(()=>of({inserted:1,unchanged:0})),
    saveDiscoveryReview:vi.fn(()=>of({status:'CONFIRMED',version:1,contentStatus:'VALID_RECRUITMENT',targetStatus:'NOT_MATCHED',notes:'reason',reviewedAt:'2026-10-26T03:00:00Z',reviewedByAdminId:'synthetic-admin'} as HumanReview)),
    discoveryHistory:vi.fn(()=>of({items:[],page:1,pageSize:20,total:0,totalPages:0,current:{status:'UNREVIEWED',version:0,contentStatus:null,targetStatus:null,notes:null,reviewedAt:null,reviewedByAdminId:null}} as ReviewHistory)),
    discoveryMetrics:vi.fn(()=>of({} as DiscoveryQualityReport))};
  const map=convertToParamMap(id?{id}:{});
  const query=new BehaviorSubject(convertToParamMap(runId?{runId}:{}));
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),
    {provide:JobHuntingService,useValue:api},{provide:ActivatedRoute,useValue:{paramMap:of(map),queryParamMap:query,snapshot:{paramMap:map,queryParamMap:query.value}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();return {fixture,api,query,c:fixture.componentInstance};
}
describe('Discovery Review',()=>{
  it('keeps all detail sections and submits confirmation from the rendered review controls',async()=>{
    const {fixture,api}=mount('synthetic-1');const el:HTMLElement=fixture.nativeElement;
    for(const heading of ['Thông tin tuyển dụng','Nội dung bài đăng gốc','Đánh giá AI / bộ phân loại','Human Review','Thông tin kỹ thuật'])expect(el.textContent).toContain(heading);
    expect(el.querySelector<HTMLDetailsElement>('.technical')?.open).toBe(false);
    const confirm=el.querySelector<HTMLButtonElement>('.human-review .admin-actions button')!;expect(confirm.disabled).toBe(true);
    const notes=el.querySelector<HTMLTextAreaElement>('#human-notes')!;notes.value='Đã đọc và đối chiếu bài gốc';notes.dispatchEvent(new Event('input'));fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(confirm.disabled).toBe(false);confirm.click();fixture.detectChanges();
    expect(api.saveDiscoveryReview).toHaveBeenCalledExactlyOnceWith('synthetic-1',{expectedVersion:0,action:'CONFIRMED',notes:'Đã đọc và đối chiếu bài gốc'});
    expect(el.querySelector('.comparison')?.textContent).toContain('Đã xác nhận');
  });
  it('updates same-route session list and metrics, ignoring stale responses and clearing removed runId',()=>{
    const {api,c,query}=mount(undefined,'fresh-session-a');const oldMetrics=new Subject<DiscoveryQualityReport>();
    const newMetrics=new Subject<DiscoveryQualityReport>();const staleList=new Subject<DiscoveryReviewList>();const newList=new Subject<DiscoveryReviewList>();
    api.discoveryMetrics.mockReturnValueOnce(oldMetrics).mockReturnValueOnce(newMetrics);
    c.loadMetrics();api.discovery.mockReturnValueOnce(staleList);c.load();c.page=3;api.discovery.mockReturnValueOnce(newList);
    query.next(convertToParamMap({runId:'fresh-session-b'}));
    expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20,runId:'fresh-session-b'});
    expect(api.discoveryMetrics).toHaveBeenLastCalledWith('fresh-session-b');
    oldMetrics.next({notice:'OLD'} as DiscoveryQualityReport);oldMetrics.complete();
    expect(c.metrics()).toBeNull();expect(c.metricsLoading()).toBe(true);
    staleList.next({...listing(),counts:{...listing().counts,total:999}});expect(c.list()).toBeNull();
    newList.next(listing());expect(c.list()?.counts.total).toBe(1);
    newMetrics.next({notice:'NEW'} as DiscoveryQualityReport);expect(c.metrics()?.notice).toBe('NEW');
    api.discoveryMetrics.mockReturnValue(of({} as DiscoveryQualityReport));query.next(convertToParamMap({}));
    expect(c.runId).toBe('');expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20});
    expect(api.discoveryMetrics).toHaveBeenLastCalledWith(undefined);
  });
  it('opens the fresh session using server-side run filter without clearing historical observations',()=>{
    const {api,c}=mount(undefined,'fresh-offline-demo-01');
    expect(c.runId).toBe('fresh-offline-demo-01');
    expect(api.discovery).toHaveBeenCalledExactlyOnceWith({page:1,pageSize:20,runId:'fresh-offline-demo-01'});
    expect(api.importDiscovery).not.toHaveBeenCalled();
  });
  it('distinguishes stored observations and unknown publication freshness from verified recent jobs',()=>{
    const {fixture,c}=mount('synthetic-1');
    expect(fixture.nativeElement.textContent).toContain('Freshness: UNKNOWN');
    expect(fixture.nativeElement.textContent).toContain('Import: STORED');
    c.detail.set({...c.detail()!,item:{...c.detail()!.item,publishedAt:'2026-10-26T03:00:00Z'}});fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('UNVERIFIED_PROVIDER_TIMESTAMP');
    expect(fixture.nativeElement.textContent).not.toContain('verified_recent');
  });
  afterEach(()=>TestBed.resetTestingModule());
  it('does not auto-review and sends original confirm with authoritative version and mandatory reason',()=>{
    const {c,api}=mount('synthetic-1');expect(api.saveDiscoveryReview).not.toHaveBeenCalled();
    c.saveReview('CONFIRMED');expect(api.saveDiscoveryReview).not.toHaveBeenCalled();
    const d=detail();d.humanReview={status:'UNCERTAIN',version:7,contentStatus:null,targetStatus:null,notes:'old',reviewedAt:null,reviewedByAdminId:'admin'};c.detail.set(d);
    c.draft(d).notes='Xác nhận sau khi đọc';c.saveReview('CONFIRMED');
    expect(api.saveDiscoveryReview).toHaveBeenCalledExactlyOnceWith('synthetic-1',{expectedVersion:7,action:'CONFIRMED',notes:'Xác nhận sau khi đọc'});
    expect(c.savingReview()).toBe(false);
  });
  it('sends independent correction labels, prevents double clicks, and preserves draft after conflict and explicit reload',()=>{
    const {c,api,fixture}=mount('synthetic-1');const pending=new Subject<HumanReview>();api.saveDiscoveryReview.mockReturnValue(pending);
    const d=c.detail()!;c.draft(d).notes='<script>private draft</script>';c.draft(d).contentStatus='NEEDS_REVIEW';
    c.saveReview('CORRECTED');c.saveReview('CORRECTED');expect(api.saveDiscoveryReview).toHaveBeenCalledTimes(1);expect(c.savingReview()).toBe(true);
    expect(api.saveDiscoveryReview).toHaveBeenCalledWith(d.id,{expectedVersion:0,action:'CORRECTED',contentStatus:'NEEDS_REVIEW',targetStatus:'NOT_MATCHED',notes:'<script>private draft</script>'});
    pending.error({status:409});fixture.detectChanges();expect(c.reviewConflict()).toBe(true);expect(c.savingReview()).toBe(false);
    c.saveReview('CORRECTED');expect(api.saveDiscoveryReview).toHaveBeenCalledTimes(1);
    expect(c.draft(c.detail()!).notes).toContain('private draft');expect(fixture.nativeElement.querySelector('script')).toBeNull();
    c.load();expect(c.reviewConflict()).toBe(false);expect(c.draft(c.detail()!).notes).toContain('private draft');
    expect(api.saveDiscoveryReview).toHaveBeenCalledTimes(1);
  });
  it('marks uncertain without editable labels and recovers from non-conflict error without erasing reason',()=>{
    const {c,api}=mount('synthetic-1');c.draft(c.detail()!).notes='Chưa rõ';api.saveDiscoveryReview.mockReturnValue(throwError(()=>({status:503})));
    c.saveReview('UNCERTAIN');expect(api.saveDiscoveryReview).toHaveBeenCalledWith('synthetic-1',{expectedVersion:0,action:'UNCERTAIN',notes:'Chưa rõ'});
    expect(c.draft(c.detail()!).notes).toBe('Chưa rõ');expect(c.savingReview()).toBe(false);expect(c.reviewConflict()).toBe(false);
  });
  it('passes review filter and only loads history on explicit action, rendering notes as inert text',()=>{
    const {c,api,fixture}=mount('synthetic-1');expect(api.discoveryHistory).not.toHaveBeenCalled();
    api.discoveryHistory.mockReturnValue(of({items:[{status:'UNCERTAIN',version:2,notes:'<img src=x onerror=alert(1)>',contentStatus:null,targetStatus:null,reviewedAt:null,reviewedByAdminId:'admin'}],page:1,pageSize:20,total:1,totalPages:1,current:{status:'UNCERTAIN',version:2,contentStatus:null,targetStatus:null,notes:'reason',reviewedAt:null,reviewedByAdminId:'admin'}} as ReviewHistory));
    c.loadHistory(1);fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('<img src=x onerror=alert(1)>');expect(fixture.nativeElement.querySelector('img')).toBeNull();
    c.reviewState='CORRECTED';expect(c.history()?.total).toBe(1);
  });
  it('forwards review-state to list API alongside existing filters',()=>{
    const {c,api}=mount();c.reviewState='UNREVIEWED';c.filter();expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20,reviewState:'UNREVIEWED'});
  });
  it('renders server metrics with cohorts/support and undefined rates, never computes quality in UI',()=>{
    const {c,api,fixture}=mount();const dimension:DimensionQuality={eligibleSamples:0,abstentions:2,unreviewed:3,uncertain:1,humanConflicts:1,predictionConflicts:0,matrix:[],classes:[{label:'TARGET_MATCH',tp:0,fp:0,fn:0,support:0,precisionDenominator:0,recallDenominator:0,precision:null,recall:null}]};
    const counts={total:8,reviewed:5,unreviewed:3,confirmed:2,corrected:2,uncertain:1};const level={contentValidity:dimension,targetMatching:dimension};
    api.discoveryMetrics.mockReturnValue(of({counts,observationLevel:level,uniqueContentLevel:level,cohorts:[{classifierVersion:'content-first-v1',profileFingerprint:null,preferencesFingerprint:null,counts,observationLevel:level,uniqueContentLevel:level}],overallMatricesAvailable:true,evaluatedAt:'2026-10-26T03:00:00Z',notice:'Only labelled sample'}));
    c.runId='run-a';c.reviewState='CORRECTED';c.loadMetrics();fixture.detectChanges();
    expect(api.discoveryMetrics).toHaveBeenCalledExactlyOnceWith('run-a');const text=fixture.nativeElement.textContent;
    expect(text).toContain('N/A');expect(text).toContain('Unique-content-level');expect(text).toContain('Human conflicts 1');expect(text).toContain('Support');
    api.discoveryMetrics.mockReturnValue(throwError(()=>new Error('synthetic')));c.loadMetrics();expect(c.metrics()).toBeNull();expect(c.metricsLoading()).toBe(false);
  });
  it('keeps nonmatching observations visible as readable cards without technical metadata',()=>{
    const {fixture,api}=mount();expect(api.discovery).toHaveBeenCalledTimes(1);
    const card:HTMLElement=fixture.nativeElement.querySelector('.job-card');
    expect(card.textContent).toContain('Không phù hợp');expect(card.textContent).toContain('Chưa đánh giá');
    expect(card.textContent).toContain('Chưa xác định ngày đăng');expect(card.textContent).toContain('Java');
    for(const technical of ['VALID_RECRUITMENT','NOT_MATCHED','POST_ID_URL_MISMATCH','synthetic-run','EMPLOYER_HIRING','Item','Page'])expect(card.textContent).not.toContain(technical);
    expect(card.querySelector('a')?.getAttribute('href')).toContain('/discovery/synthetic-1');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(api.importDiscovery).not.toHaveBeenCalled();
  });
  it('forwards filters and resets pagination, without implementing classification rules in UI',()=>{
    const {c,api}=mount();c.page=3;c.contentStatus='NEEDS_REVIEW';c.targetStatus='INSUFFICIENT_DATA';c.identityOnly=true;c.duplicatesOnly=true;c.runId='run-2';c.filter();
    expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20,contentStatus:'NEEDS_REVIEW',targetStatus:'INSUFFICIENT_DATA',runId:'run-2',identityWarning:true,duplicatesOnly:true});
  });
  it('renders raw content as inert text and source only as an unverified safe link',()=>{
    const {fixture,c}=mount('synthetic-1');expect(fixture.nativeElement.querySelector('[data-testid=raw-content]').textContent).toContain('<script>');
    expect(fixture.nativeElement.querySelector('script')).toBeNull();expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('NOT_CHECKED');expect(fixture.nativeElement.querySelector('a[target=_blank]').rel).toBe('noopener noreferrer');
    expect(c.safeLink('javascript:alert(1)')).toBeNull();expect(c.safeLink('https://facebook.com.evil.invalid/x/posts/123')).toBeNull();
    expect(c.safeLink('https://user:pass@facebook.com/x/posts/123')).toBeNull();expect(c.safeLink('https://facebook.com/search/posts?q=x')).toBeNull();
  });
  it('removes import and destructive reset controls without invoking their APIs',()=>{
    const {fixture,api,c}=mount();const el:HTMLElement=fixture.nativeElement;
    expect(el.querySelector('input[type=file]')).toBeNull();expect(el.querySelector('app-discovery-cleanup')).toBeNull();
    expect(el.textContent).not.toContain('Import thủ công');expect(el.textContent).not.toContain('Reset All Job Data');
    expect('importFile' in c).toBe(false);expect('cleanupOpen' in c).toBe(false);expect(api.importDiscovery).not.toHaveBeenCalled();
  });
  it('shows a deliberate empty state without zero summaries, pagination or unsupported discovery action',()=>{
    const {fixture,c}=mount();c.list.set({...listing(),results:{items:[],page:1,pageSize:20,total:0,totalPages:0},counts:{...listing().counts,total:0}});fixture.detectChanges();
    const el:HTMLElement=fixture.nativeElement;expect(el.querySelector('.empty-state')?.textContent).toContain('Discovery chưa có kết quả');
    expect(el.querySelector('.summary')).toBeNull();expect(el.querySelector('[aria-label="Phân trang"]')).toBeNull();
    expect(el.querySelector('.job-card')).toBeNull();expect(el.textContent).not.toContain('Chạy Discovery');expect(el.textContent).not.toContain('1 / 0');
  });
  it('routes compact tabs to existing server filters and resets pagination',()=>{
    const {fixture,c,api}=mount();const tabs=fixture.nativeElement.querySelectorAll('.filter-tabs button') as NodeListOf<HTMLButtonElement>;
    const expected=[{}, {targetStatus:'TARGET_MATCH'}, {contentStatus:'NEEDS_REVIEW'}, {reviewState:'REVIEWED'}];
    tabs.forEach((button,i)=>{c.page=4;button.click();fixture.detectChanges();expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20,...expected[i]});expect(button.getAttribute('aria-pressed')).toBe('true');});
    c.selectTab('all');expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20});
    expect(fixture.nativeElement.querySelector('.advanced-filters').open).toBe(false);
  });
  it('renders filtered empty state and clears all filters through existing list API',()=>{
    const {fixture,c,api}=mount(undefined,'session');c.reviewState='CORRECTED';c.identityOnly=true;c.collectedDate='2026-10-26';
    api.discovery.mockReturnValue(of({...listing(),results:{items:[],page:1,pageSize:20,total:0,totalPages:0}}));c.filter();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty-state').textContent).toContain('Không có cơ hội khớp bộ lọc');
    fixture.nativeElement.querySelector('.empty-state button').click();expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20});
    expect(c.hasFilters()).toBe(false);
  });
  it('uses server totals rather than the current page for summary, with functional pagination',()=>{
    const {fixture,c,api}=mount();const data=listing();data.results.total=41;data.results.totalPages=3;data.counts.total=41;data.counts.targetMatch=12;data.counts.needsReview=7;c.list.set(data);fixture.detectChanges();
    const values=[...fixture.nativeElement.querySelectorAll('.summary strong')].map((el:HTMLElement)=>el.textContent);
    expect(values).toEqual(['41','12','7']);
    const nav=fixture.nativeElement.querySelector('[aria-label="Phân trang"]');expect(nav.querySelector('button').disabled).toBe(true);
    api.discovery.mockReturnValue(of({...data,results:{...data.results,page:2}}));nav.querySelectorAll('button')[1].click();fixture.detectChanges();
    expect(api.discovery).toHaveBeenLastCalledWith({page:2,pageSize:20});expect(fixture.nativeElement.querySelector('[aria-label="Phân trang"]').textContent).toContain('Trang 2 / 3');
  });
  it('shows only source-backed title/company/location/technologies and never invents seniority',()=>{
    const {fixture,c}=mount();const data=listing();data.results.items[0].item.extracted={role:'Backend Developer',company:'Example',location:'Hà Nội',technologies:['C#','ASP.NET Core']};c.list.set(data);fixture.detectChanges();
    const card:HTMLElement=fixture.nativeElement.querySelector('.job-card');
    for(const fact of ['Backend Developer','Example','Hà Nội','C#','ASP.NET Core'])expect(card.textContent).toContain(fact);
    expect(card.textContent).not.toContain('Fresher');expect(card.textContent).not.toContain('Đã xác minh');expect(c.seniority(data.results.items[0])).toBe('');
  });
  it('does not show the previous observation or list import UI when a detail reload fails',()=>{
    const {c,api,fixture}=mount('synthetic-1');const pending=new Subject<DiscoveryReviewDetail>();api.discoveryItem.mockReturnValue(pending);
    c.load();expect(c.detail()).toBeNull();pending.error(new Error('synthetic missing record'));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid=raw-content]')).toBeNull();
    expect(fixture.nativeElement.querySelector('#review-import')).toBeNull();expect(c.error()).not.toBe('');
  });
  it('renders readable job fields while raw extraction is collapsed, without inventing missing facts',()=>{
    const {c,fixture}=mount('synthetic-1');const d=detail();
    d.item.extracted={company:'Example (post-mentioned)',role:'Fresher Backend Developer',location:'Hanoi',technologies:['C#'],
      contact:{email:'jobs@example.invalid'},vacancies:[{title:'Fresher Backend Developer',requirements:null}]};
    c.detail.set(d);fixture.detectChanges();const el:HTMLElement=fixture.nativeElement;
    expect(el.querySelector('.job-heading')?.textContent).toContain('Fresher Backend Developer');
    expect(el.querySelector('app-discovery-job-information')?.textContent).toContain('jobs@example.invalid');
    expect(el.querySelector('app-discovery-job-information')?.textContent).toContain('Chưa có bằng chứng');
    expect(el.querySelector<HTMLDetailsElement>('.technical')?.open).toBe(false);
    expect(el.textContent).toContain('UNKNOWN');expect(el.textContent).not.toContain('[object Object]');
  });
  it('updates human decision immediately from save receipt without an additional stale detail GET',()=>{
    const {c,api,fixture}=mount('synthetic-1');c.draft(c.detail()!).notes='Checked original';
    c.saveReview('CONFIRMED');fixture.detectChanges();
    expect(c.detail()?.humanReview?.version).toBe(1);expect(api.discoveryItem).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Đã xác nhận');
  });
  it('sends an explicit local calendar-day window to the server and shows all review states in list',()=>{
    const {c,api,fixture}=mount();c.collectedDate='2026-10-26';c.filter();
    const params=(api.discovery.mock.calls as unknown as [Record<string,string>][]).at(-1)![0];
    expect(new Date(params['collectedFrom']).getDate()).toBe(26);
    expect(new Date(params['collectedBefore']).getDate()).toBe(27);
    expect(fixture.nativeElement.querySelector('.job-card')?.textContent).toContain('Chưa đánh giá');
  });
});
