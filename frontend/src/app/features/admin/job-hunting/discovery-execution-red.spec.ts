import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryReviewPageComponent } from './discovery-review-page.component';
import { JobHuntingService } from './job-hunting.service';

// Test-only proposed service wire ports, no missing production model imports.
const id='11111111-1111-4111-8111-111111111111';
function mount(active=false){
  const pending=new Subject<any>();
  const api={
    discovery:vi.fn(()=>of({results:{items:[],page:1,pageSize:20,total:0,totalPages:0},counts:{total:0,valid:0,invalid:0,needsReview:0,targetMatch:0,identityWarnings:0,duplicateItems:0}})),
    discoveryMetrics:vi.fn(()=>of({})),importDiscovery:vi.fn(),saveDiscoveryReview:vi.fn(),
    startDiscoveryExecution:vi.fn(()=>pending),
    discoveryExecution:vi.fn(()=>of({id,state:'CLAIMED',actualRequests:1,creditsDeducted:null,counts:{fetched:0,normalized:0}})),
    discoveryExecutions:vi.fn(()=>of({items:active?[{id,state:'QUEUED',actualRequests:0,creditsDeducted:null}]:[],page:1,pageSize:20,total:active?1:0}))
  };
  const map=convertToParamMap({});
  TestBed.configureTestingModule({imports:[DiscoveryReviewPageComponent],providers:[provideRouter([]),
    {provide:JobHuntingService,useValue:api},{provide:ActivatedRoute,useValue:{paramMap:of(map),queryParamMap:of(map),snapshot:{paramMap:map,queryParamMap:map}}}]});
  const fixture=TestBed.createComponent(DiscoveryReviewPageComponent);fixture.detectChanges();
  const button=()=>Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'))
    .find(b=>/Run Job Discovery|Chạy.*(?:Discovery|khám phá)/iu.test(b.textContent??''));
  return {fixture,api,pending,button};
}

describe('Semi-automated Discovery T15 test-only RED',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('one click calls backend once with only an idempotency UUID, no GitHub inputs',()=>{
    const h=mount();expect(h.button(),'Run Job Discovery action is not implemented').toBeDefined();
    h.button()!.click();h.fixture.detectChanges();
    expect(h.api.startDiscoveryExecution).toHaveBeenCalledTimes(1);
    const body=(h.api.startDiscoveryExecution.mock.calls as unknown as Array<[Record<string,unknown>]>)[0][0];
    expect(Object.keys(body)).toEqual(['clientRequestId']);expect(body['clientRequestId']).toMatch(/^[a-f0-9-]{36}$/iu);
  });
  it('double click while start is pending cannot start another execution',()=>{
    const h=mount();expect(h.button(),'Run Job Discovery action is not implemented').toBeDefined();
    h.button()!.click();h.fixture.detectChanges();expect(h.button()!.disabled).toBe(true);
    h.button()!.click();h.fixture.detectChanges();expect(h.api.startDiscoveryExecution).toHaveBeenCalledTimes(1);
  });
  it('restores an active server execution without dispatching another run',()=>{
    const h=mount(true);expect(h.button(),'Run Job Discovery action is not implemented').toBeDefined();
    expect(h.api.discoveryExecutions).toHaveBeenCalled();expect(h.button()!.disabled).toBe(true);
    expect(h.api.startDiscoveryExecution).not.toHaveBeenCalled();
  });
  it('unknown start outcome is visible and does not automatically retry',()=>{
    const h=mount();expect(h.button(),'Run Job Discovery action is not implemented').toBeDefined();
    h.button()!.click();h.fixture.detectChanges();h.pending.error({status:504});h.fixture.detectChanges();
    expect(h.api.startDiscoveryExecution).toHaveBeenCalledTimes(1);
    expect(h.fixture.nativeElement.textContent).toMatch(/unknown|không xác định|chưa xác nhận|không rõ/iu);
  });
  it('partial terminal result includes counts, uncertain fields and normalized Job links',()=>{
    const h=mount();expect(h.button(),'Run Job Discovery action is not implemented').toBeDefined();
    h.api.discoveryExecution.mockReturnValue(of({id,state:'PARTIAL',actualRequests:1,creditsDeducted:1,
      counts:{fetched:3,validRecruitment:2,normalized:1,duplicates:1,extractionFailures:1},
      results:[{jobPostingId:'22222222-2222-4222-8222-222222222222',missingFields:['LOCATION']}]} as any));
    h.button()!.click();h.pending.next({id,state:'PARTIAL'});h.pending.complete();h.fixture.detectChanges();
    const el:HTMLElement=h.fixture.nativeElement;
    expect(el.textContent).toMatch(/partial|một phần/iu);
    expect(el.querySelector('a[href*="22222222-2222-4222-8222-222222222222"]')).not.toBeNull();
    expect(h.api.importDiscovery).not.toHaveBeenCalled();expect(h.api.saveDiscoveryReview).not.toHaveBeenCalled();
  });
  it('T16 control: existing empty Discovery does not auto-start, import or review',()=>{
    const h=mount();expect(h.api.discovery).toHaveBeenCalledTimes(1);
    expect(h.api.startDiscoveryExecution).not.toHaveBeenCalled();
    expect(h.api.importDiscovery).not.toHaveBeenCalled();expect(h.api.saveDiscoveryReview).not.toHaveBeenCalled();
  });
});
