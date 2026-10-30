import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { JobHuntingService } from './job-hunting.service';
import { DiscoveryReviewList } from './discovery-review.models';

const executionId='d20d3b13-ae44-492f-b4aa-590af73dd78a'; // Known regression ID, synthetic content only.
const decision=(status:string)=>({status,reasonCodes:[],explanation:'Synthetic stored decision',evidenceSnippets:[]});
const candidate=(i:number)=>({id:`candidate-${i}`,runId:'older-run',query:'Synthetic original query',pageNumber:1,ingestedAt:'2026-10-26T00:00:00Z',duplicateCount:0,
  item:{itemIndex:i,providerPostId:`${i}`,sourceUrl:null,rawContent:'Hiring Backend Developer',publishedAt:null,
    contentValidity:decision('VALID_RECRUITMENT'),targetMatching:decision('NOT_MATCHED'),verificationStatus:'NOT_CHECKED',
    extracted:{role:'Backend Developer',technologies:[]},sourceWarnings:[],missingFields:[],classifierVersion:'content-first-v1',evaluatedAt:'2026-10-26T00:00:00Z'}});
const observation=(i:number,status:string|null='VALID_RECRUITMENT',match='TARGET_MATCH')=>({origin:'EXECUTION_OBSERVATION',executionId,itemIndex:i,
  collectedAt:'2026-10-27T00:00:00Z',workflowRunId:'10001',executionState:'INGESTING',semanticState:'NOT_REQUESTED',extractionError:null,
  source:{itemIndex:i,providerPostId:`900${i}`,sourceUrl:null,providerSourceUrl:'https://www.facebook.com/fixture/posts/pfbidSynthetic',rawContent:`Hiring Backend Developer ${i}\n${'Synthetic description '.repeat(35)}`,contentHash:null,publishedAt:null,outcome:'POST_ID_URL_MISMATCH'},
  review:status?{contentValidity:decision(status),targetMatching:decision(match),sourceWarnings:['POST_ID_URL_MISMATCH'],missingFields:['LOCATION'],extracted:{}}:null,vacancies:[],candidateId:null,humanReview:null});
function mount(items:unknown[],params:Record<string,string>={}){
  const data={results:{items,page:1,pageSize:20,total:items.length,totalPages:1},counts:{total:items.length,valid:8,invalid:1,needsReview:3,targetMatch:4,identityWarnings:7,duplicateItems:0,reviewed:0,unknown:1}};
  const api={discovery:vi.fn(()=>of(data as unknown as DiscoveryReviewList)),discoveryItem:vi.fn(),
    discoveryExecutions:vi.fn(()=>of({items:[],page:1,pageSize:20,total:0})),discoveryExecution:vi.fn(),startDiscoveryExecution:vi.fn(),saveDiscoveryReview:vi.fn(),discoveryMetrics:vi.fn()};
  const map=convertToParamMap(params);
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api},
    {provide:ActivatedRoute,useValue:{paramMap:of(map),queryParamMap:of(convertToParamMap({})),snapshot:{paramMap:map}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();return {fixture,api,c:fixture.componentInstance};
}
describe('Unified persisted Discovery cards',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('keeps five candidates and all seven saved observations in the existing card list',()=>{
    const {fixture,api}=mount([...Array.from({length:5},(_,i)=>candidate(i)),...Array.from({length:7},(_,i)=>observation(i))]);
    const cards=fixture.nativeElement.querySelectorAll('.candidate-list .job-card');expect(cards.length).toBe(12);
    expect(fixture.nativeElement.querySelector('.summary').textContent).toContain('Tổng bài đăng');
    const card=cards[5] as HTMLElement;expect(card.querySelector('a')?.getAttribute('href')).toContain(`/executions/${executionId}/observations/0`);
    expect(card.textContent).toContain('Gemini');expect(card.textContent).not.toContain('Chưa đánh giá');
    expect(card.textContent!.length).toBeLessThan(650);expect(card.textContent).not.toContain('Synthetic description '.repeat(30));
    expect(api.saveDiscoveryReview).not.toHaveBeenCalled();expect(api.startDiscoveryExecution).not.toHaveBeenCalled();
  });
  it('distinguishes nonmatching recruitment, invalid content and pending classification',()=>{
    const {fixture}=mount([observation(0,'VALID_RECRUITMENT','NOT_MATCHED'),observation(1,'INVALID_RECRUITMENT','NOT_MATCHED'),observation(2,null)]);
    const cards=fixture.nativeElement.querySelectorAll('.job-card');
    expect(cards[0].textContent).toContain('Có nội dung tuyển dụng');expect(cards[0].textContent).toContain('Không phù hợp');
    expect(cards[1].textContent).toContain('Không phải tin tuyển dụng');expect(cards[2].textContent).toContain('Chưa phân loại');
    expect(cards[2].textContent).not.toContain('Không phải tin tuyển dụng');
  });
  it('opens observation detail by explicit source coordinates without querying a candidate ID',()=>{
    const item=observation(0);const {fixture,api}=mount([item],{executionId,itemIndex:'0'});
    expect(api.discoveryItem).not.toHaveBeenCalled();expect(api.discovery).toHaveBeenCalledWith({page:1,pageSize:1,executionId,itemIndex:0});
    expect(fixture.nativeElement.querySelector('[data-testid=raw-content]')?.textContent).toBe(item.source.rawContent);
    expect(fixture.nativeElement.textContent).toContain('Gemini');expect(fixture.nativeElement.querySelector('.human-review')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[target=_blank]')).toBeNull();
  });
  it('keeps execution status compact without duplicating per-post content above cards',()=>{
    const {fixture}=mount([observation(0)]);
    expect(fixture.nativeElement.querySelector('.execution > details')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.execution article')).toBeNull();
  });
  it('refreshes the same filtered population when another execution saves posts',()=>{
    const {fixture,api,c}=mount([observation(0)]);
    c.targetStatus='TARGET_MATCH';
    const fresh={results:{items:[observation(0),{...observation(1),executionId:'22222222-2222-4222-8222-222222222222'}],page:1,pageSize:20,total:2,totalPages:1},
      counts:{total:2,valid:2,invalid:0,needsReview:0,targetMatch:2,identityWarnings:2,duplicateItems:0,reviewed:0,unknown:0}};
    api.discovery.mockReturnValue(of(fresh as unknown as DiscoveryReviewList));c.refreshObservations();fixture.detectChanges();
    expect(api.discovery).toHaveBeenLastCalledWith({page:1,pageSize:20,targetStatus:'TARGET_MATCH'});
    expect(fixture.nativeElement.querySelectorAll('.job-card').length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.summary strong')[0].textContent).toBe('2');
  });
  it('links an overlapping observation to its real candidate without inventing review controls',()=>{
    const existing=candidate(0);const o={...observation(0),candidateId:existing.id};
    const {fixture,api}=mount([{...existing,origin:'CLASSIFIER',observation:o}],{executionId,itemIndex:'0'});
    expect(fixture.nativeElement.querySelector('a[href*="candidate-0"]')?.textContent).toContain('Human Review');
    expect(fixture.nativeElement.querySelector('.human-review')).toBeNull();expect(api.saveDiscoveryReview).not.toHaveBeenCalled();
  });
  it('does not infer fields from arbitrary extraction values and renders the original as inert text',()=>{
    const o=observation(0);o.source.rawContent='<img src=x onerror=alert(1)> Hiring Backend Developer';
    const fake={fields:{companyName:{value:'Invented employer',status:'EXTRACTED',evidence:[{start:0,end:3,quote:'not the source'}]}}};
    const {fixture}=mount([{...o,vacancies:[fake]}]);
    expect(fixture.nativeElement.querySelector('.company').textContent).toContain('Chưa xác định công ty');
    expect(fixture.nativeElement.querySelector('img')).toBeNull();expect(fixture.nativeElement.textContent).not.toContain('Invented employer');
  });
  it('does not present stale summary counts as the result of a failed new filter',()=>{
    const {fixture,api,c}=mount([observation(0)]);
    api.discovery.mockReturnValue(throwError(()=>({status:503})));c.targetStatus='TARGET_MATCH';c.filter();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.summary')).toBeNull();expect(fixture.nativeElement.querySelector('.job-card')).toBeNull();
  });
});
