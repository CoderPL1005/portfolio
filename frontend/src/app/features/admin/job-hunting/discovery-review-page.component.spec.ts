import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
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
function mount(id?:string) {
  const api={discovery:vi.fn(()=>of(listing())),discoveryItem:vi.fn(()=>of(detail())),importDiscovery:vi.fn(()=>of({inserted:1,unchanged:0})),
    saveDiscoveryReview:vi.fn(()=>of({status:'CONFIRMED',version:1,contentStatus:'VALID_RECRUITMENT',targetStatus:'NOT_MATCHED',notes:'reason',reviewedAt:'2026-10-26T03:00:00Z',reviewedByAdminId:'synthetic-admin'} as HumanReview)),
    discoveryHistory:vi.fn(()=>of({items:[],page:1,pageSize:20,total:0,totalPages:0,current:{status:'UNREVIEWED',version:0,contentStatus:null,targetStatus:null,notes:null,reviewedAt:null,reviewedByAdminId:null}} as ReviewHistory)),
    discoveryMetrics:vi.fn(()=>of({} as DiscoveryQualityReport))};
  const map=convertToParamMap(id?{id}:{});
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),
    {provide:JobHuntingService,useValue:api},{provide:ActivatedRoute,useValue:{paramMap:of(map),snapshot:{paramMap:map}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();return {fixture,api,c:fixture.componentInstance};
}
describe('Discovery Review',()=>{
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
  it('keeps valid nonmatching rows visible with warnings and separate counters',()=>{
    const {fixture,api}=mount();expect(api.discovery).toHaveBeenCalledTimes(1);
    const text=fixture.nativeElement.textContent;expect(text).toContain('VALID_RECRUITMENT');expect(text).toContain('NOT_MATCHED');expect(text).toContain('POST_ID_URL_MISMATCH');
    expect(fixture.nativeElement.querySelector('tbody a').getAttribute('href')).toContain('/discovery/synthetic-1');
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
  it('imports only on explicit action and prevents duplicate clicks while request is pending',async()=>{
    const {c,api}=mount();const pending=new Subject<{inserted:number;unchanged:number}>();api.importDiscovery.mockReturnValue(pending);
    c.selected.set({size:100,text:()=>Promise.resolve('{"schemaVersion":1}')} as File);
    await c.importFile();await c.importFile();expect(api.importDiscovery).toHaveBeenCalledTimes(1);expect(c.importing()).toBe(true);
    pending.next({inserted:1,unchanged:0});expect(c.importing()).toBe(false);expect(c.selected()).toBeNull();
  });
  it('rejects oversized and invalid JSON locally and recovers after API failure',async()=>{
    const {c,api}=mount();c.selected.set({size:8*1024*1024+1} as File);await c.importFile();expect(api.importDiscovery).not.toHaveBeenCalled();
    c.selected.set({size:2,text:()=>Promise.resolve('no')} as File);await c.importFile();expect(c.importing()).toBe(false);expect(api.importDiscovery).not.toHaveBeenCalled();
    api.importDiscovery.mockReturnValue(throwError(()=>new Error('synthetic')));c.selected.set({size:2,text:()=>Promise.resolve('{}')} as File);
    await c.importFile();expect(c.importing()).toBe(false);expect(c.error()).not.toBe('');
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
    expect(fixture.nativeElement.textContent).toContain('CONFIRMED');
  });
  it('sends an explicit local calendar-day window to the server and shows all review states in list',()=>{
    const {c,api,fixture}=mount();c.collectedDate='2026-10-26';c.filter();
    const params=(api.discovery.mock.calls as unknown as [Record<string,string>][]).at(-1)![0];
    expect(new Date(params['collectedFrom']).getDate()).toBe(26);
    expect(new Date(params['collectedBefore']).getDate()).toBe(27);
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('UNREVIEWED');
  });
});
