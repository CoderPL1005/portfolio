import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { JobHuntingService } from './job-hunting.service';
import { observationEnriched, observationFact } from './discovery-observation-view';
import { DiscoveryObservationDetail } from './discovery-review.models';

// Synthetic only. Not a production result or a measured model prediction.
const raw='TAX NET tuyển dụng Intern Backend Developer\nThanh Xuân, Hà Nội\nC# / .NET';
const field=(value:string,origin='GEMINI')=>{const start=raw.indexOf(value);return {value,status:'EXTRACTED',origin,evidence:[{start,end:start+value.length,quote:value}]};};
const row=()=>({origin:'EXECUTION_OBSERVATION',executionId:'11111111-1111-4111-8111-111111111111',itemIndex:0,workflowRunId:'10001',collectedAt:'2026-10-30T00:00:00Z',executionState:'INGESTING',semanticState:'COMPLETED',extractionError:null,
  source:{itemIndex:0,providerPostId:'9001',sourceUrl:null,providerSourceUrl:null,rawContent:raw,contentHash:null,publishedAt:null,outcome:'READY_FOR_EXTRACTION'},
  review:null,sourceWarnings:[],candidateId:null,humanReview:null,vacancies:[{fields:{companyName:field('TAX NET'),positionTitle:field('Intern Backend Developer','DETERMINISTIC'),location:field('Thanh Xuân, Hà Nội')}}]}) as DiscoveryObservationDetail;
function mount(detail=false){
  const o=row(),map=convertToParamMap(detail?{executionId:o.executionId,itemIndex:'0'}:{});
  const api={discovery:vi.fn(()=>of({results:{items:[o],page:1,pageSize:20,total:1,totalPages:1},counts:{total:1,valid:0,needsReview:1,reviewed:0}})),discoveryExecutions:vi.fn(()=>of({items:[]}))};
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api},{provide:ActivatedRoute,useValue:{paramMap:of(map),queryParamMap:of(convertToParamMap({})),snapshot:{paramMap:map}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();return fixture;
}
describe('Validated Gemini fields in unified Discovery',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('renders enrichment in the same card without converting processing success into classification',()=>{
    const f=mount();const card=f.nativeElement.querySelector('.job-card');
    for(const value of ['TAX NET','Intern Backend Developer','Thanh Xuân, Hà Nội','Đã bổ sung có bằng chứng','Chưa phân loại','Chưa xác định ngày đăng'])expect(card.textContent).toContain(value);
    expect(card.querySelector('h2').textContent).not.toContain('tuyển dụng');expect(card.textContent).not.toContain('VALID_RECRUITMENT');
  });
  it('shows deterministic/Gemini attribution, exact source and missing information on detail',()=>{
    const f=mount(true),text=f.nativeElement.textContent;
    for(const value of ['Deterministic','Gemini','UNKNOWN','TAX NET','Thanh Xuân, Hà Nội'])expect(text).toContain(value);
    expect(f.nativeElement.querySelector('[data-testid=raw-content]').textContent).toBe(raw);
    expect(f.nativeElement.querySelector('.human-review')).toBeNull();
  });
  it('COMPLETED generation alone is not proof of saved enrichment',()=>{
    const o=row();o.vacancies=[];expect(observationEnriched(o)).toBe(false);
    o.vacancies=[{fields:{companyName:field('TAX NET','DETERMINISTIC')}}];expect(observationEnriched(o)).toBe(false);
  });
  it('rejects a real value backed by an unrelated source quote',()=>{
    const o=row();o.vacancies[0].fields!['companyName']={...field('TAX NET'),evidence:field('.NET').evidence};
    expect(observationFact(o,'companyName')).toBe('');
  });
});
