import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { DiscoveryExecutionComponent } from './discovery-execution.component';
import { JobHuntingService } from './job-hunting.service';

describe('Persisted post visibility',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('keeps classification failures visible without invented decisions',()=>{
    const run={id:'synthetic',state:'INGESTING',results:[{itemIndex:0,observation:{rawContent:'Synthetic retained source'},
      processing:{origin:'WORKER_INCREMENTAL',review:null,vacancies:[],extractionError:'CLASSIFICATION_FAILED'}}]};
    const api={discoveryExecutions:vi.fn(()=>of({items:[run]})),discoveryExecution:vi.fn(()=>of(run)),startDiscoveryExecution:vi.fn()};
    TestBed.configureTestingModule({imports:[DiscoveryExecutionComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api}]});
    const fixture=TestBed.createComponent(DiscoveryExecutionComponent);fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Synthetic retained source');
    expect(fixture.nativeElement.textContent).toContain('CLASSIFICATION_FAILED');
    expect(fixture.nativeElement.textContent).toContain('Classification unavailable');
    expect(fixture.nativeElement.textContent).not.toContain('VALID_RECRUITMENT');
    expect(api.startDiscoveryExecution).not.toHaveBeenCalled();
  });
  for(const state of ['INGESTING','PARTIAL'])it(`shows source/classification/Gemini failure while ${state}`,()=>{
    const run={id:'synthetic',state,results:[{itemIndex:0,semanticState:'UNKNOWN',observation:{rawContent:'Synthetic original recruitment',sourceUrl:null,providerSourceUrl:'https://www.facebook.com/fixture/posts/pfbidSynthetic',outcome:'POST_ID_URL_MISMATCH'},
      processing:{origin:'PERSISTED_REPROCESSING',review:{contentValidity:{status:'VALID_RECRUITMENT',explanation:'Employer hiring'},targetMatching:{status:'INSUFFICIENT_DATA',explanation:'Location unknown'},missingFields:['LOCATION'],sourceWarnings:['POST_ID_URL_MISMATCH']},vacancies:[],extractionError:'SEMANTIC_UNAVAILABLE'}}]};
    const api={discoveryExecutions:vi.fn(()=>of({items:[run]})),discoveryExecution:vi.fn(()=>of(run)),startDiscoveryExecution:vi.fn()};
    TestBed.configureTestingModule({imports:[DiscoveryExecutionComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api}]});
    const fixture=TestBed.createComponent(DiscoveryExecutionComponent);fixture.detectChanges();
    const text=fixture.nativeElement.textContent;
    for(const value of ['Synthetic original recruitment','VALID_RECRUITMENT','INSUFFICIENT_DATA','UNKNOWN','LOCATION','POST_ID_URL_MISMATCH','SEMANTIC_UNAVAILABLE'])expect(text).toContain(value);
    expect(api.startDiscoveryExecution).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('a[href*="pfbidSynthetic"]')).toBeNull();
  });
});
