import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { JobHuntingService } from './job-hunting.service';

// Per-post visibility now belongs to the unified detail, not the compact run panel.
function mount(run:any){
  const executionId='11111111-1111-4111-8111-111111111111',result=run.results[0],map=convertToParamMap({executionId,itemIndex:'0'});
  const o={origin:'EXECUTION_OBSERVATION',executionId,itemIndex:0,executionState:run.state,semanticState:result.semanticState??'NOT_REQUESTED',
    collectedAt:'2026-10-27T00:00:00Z',source:{...result.observation,publishedAt:null},review:result.processing.review,
    vacancies:result.processing.vacancies,extractionError:result.processing.extractionError,candidateId:null,humanReview:null};
  const api={discovery:vi.fn(()=>of({results:{items:[o],page:1,pageSize:1,total:1,totalPages:1}})),startDiscoveryExecution:vi.fn()};
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api},
    {provide:ActivatedRoute,useValue:{paramMap:of(map),queryParamMap:of(convertToParamMap({})),snapshot:{paramMap:map}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();return {fixture,api};
}

describe('Persisted post visibility',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('keeps classification failures visible without invented decisions',()=>{
    const run={id:'synthetic',state:'INGESTING',results:[{itemIndex:0,observation:{rawContent:'Synthetic retained source'},
      processing:{origin:'WORKER_INCREMENTAL',review:null,vacancies:[],extractionError:'CLASSIFICATION_FAILED'}}]};
    const {fixture,api}=mount(run);
    expect(fixture.nativeElement.textContent).toContain('Synthetic retained source');
    expect(fixture.nativeElement.textContent).toContain('CLASSIFICATION_FAILED');
    expect(fixture.nativeElement.textContent).toContain('Chưa phân loại');
    expect(fixture.nativeElement.textContent).not.toContain('VALID_RECRUITMENT');
    expect(api.startDiscoveryExecution).not.toHaveBeenCalled();
  });
  for(const state of ['INGESTING','PARTIAL'])it(`shows source/classification/Gemini failure while ${state}`,()=>{
    const run={id:'synthetic',state,results:[{itemIndex:0,semanticState:'UNKNOWN',observation:{rawContent:'Synthetic original recruitment',sourceUrl:null,providerSourceUrl:'https://www.facebook.com/fixture/posts/pfbidSynthetic',outcome:'POST_ID_URL_MISMATCH'},
      processing:{origin:'PERSISTED_REPROCESSING',review:{contentValidity:{status:'VALID_RECRUITMENT',explanation:'Employer hiring'},targetMatching:{status:'INSUFFICIENT_DATA',explanation:'Location unknown'},missingFields:['LOCATION'],sourceWarnings:['POST_ID_URL_MISMATCH']},vacancies:[],extractionError:'SEMANTIC_UNAVAILABLE'}}]};
    const {fixture,api}=mount(run);
    const text=fixture.nativeElement.textContent;
    for(const value of ['Synthetic original recruitment','Có nội dung tuyển dụng','Chưa đủ dữ liệu','Chưa xác định kết quả','LOCATION','POST_ID_URL_MISMATCH','SEMANTIC_UNAVAILABLE'])expect(text).toContain(value);
    expect(api.startDiscoveryExecution).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('a[href*="pfbidSynthetic"]')).toBeNull();
  });
});
