import {TestBed} from '@angular/core/testing';
import {of,Subject,throwError} from 'rxjs';
import {vi} from 'vitest';
import {DiscoveryCleanupComponent} from './discovery-cleanup.component';
import {JobHuntingService} from './job-hunting.service';
import {JobDataResetPlan,JobDataResetResult} from './discovery-cleanup.models';

const plan:JobDataResetPlan={counts:{discovery_candidates:8,job_postings:2,raw_job_postings:3,job_applications:1},totalRecords:14,fingerprint:'preview-fingerprint'};
const empty:JobDataResetPlan={counts:{discovery_candidates:0,job_postings:0,raw_job_postings:0,job_applications:0},totalRecords:0,fingerprint:'empty'};
function mount(){
  const api={jobDataResetPreview:vi.fn(()=>of(plan)),resetAllJobData:vi.fn(()=>of({deleted:plan,remaining:empty}))};
  TestBed.configureTestingModule({imports:[DiscoveryCleanupComponent],providers:[{provide:JobHuntingService,useValue:api}]});
  const f=TestBed.createComponent(DiscoveryCleanupComponent);f.detectChanges();return{f,c:f.componentInstance,api};
}
describe('Reset All Job Data',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('previews actual counts, includes applications and never resets on load',()=>{
    const {f,api}=mount();expect(api.jobDataResetPreview).toHaveBeenCalledTimes(1);expect(api.resetAllJobData).not.toHaveBeenCalled();
    expect(f.nativeElement.textContent).toContain('job_applications: 1');expect(f.nativeElement.textContent).toContain('14');
    expect(f.nativeElement.querySelector('input[type=checkbox]')).toBeNull();expect(f.nativeElement.querySelector('input[type=file]')).toBeNull();
  });
  it('requires exact typed confirmation and blocks double click',()=>{
    const {c,api}=mount();for(const value of ['', 'reset all job data','RESET ALL JOB DATA ']){c.confirmation=value;c.execute();}
    expect(api.resetAllJobData).not.toHaveBeenCalled();
    const pending=new Subject<JobDataResetResult>();api.resetAllJobData.mockReturnValue(pending);
    c.confirmation='RESET ALL JOB DATA';c.execute();c.execute();expect(api.resetAllJobData).toHaveBeenCalledExactlyOnceWith({confirmation:'RESET ALL JOB DATA',expectedFingerprint:plan.fingerprint});
    pending.next({deleted:plan,remaining:empty});expect(c.plan()?.totalRecords).toBe(0);expect(c.confirmation).toBe('');
    c.confirmation='RESET ALL JOB DATA';c.execute();expect(api.resetAllJobData).toHaveBeenCalledTimes(1);
  });
  it('clears stale preview on 409 and never automatically retries',()=>{
    const {c,api}=mount();api.resetAllJobData.mockReturnValue(throwError(()=>({status:409})));c.confirmation='RESET ALL JOB DATA';c.execute();c.execute();
    expect(api.resetAllJobData).toHaveBeenCalledTimes(1);expect(c.plan()).toBeNull();expect(c.confirmation).toBe('');expect(api.jobDataResetPreview).toHaveBeenCalledTimes(1);
  });
  it('failed preview stays UNKNOWN, not an assumed empty database',()=>{
    const {f,c,api}=mount();api.jobDataResetPreview.mockReturnValue(throwError(()=>({status:503})));c.refresh();f.detectChanges();
    expect(c.plan()).toBeNull();expect(f.nativeElement.textContent).toContain('UNKNOWN');c.confirmation='RESET ALL JOB DATA';c.execute();expect(api.resetAllJobData).not.toHaveBeenCalled();
  });
  it('refresh clears previous confirmation and loads new counts',()=>{
    const {c,api}=mount();c.confirmation='RESET ALL JOB DATA';api.jobDataResetPreview.mockReturnValue(of(empty));c.refresh();expect(c.confirmation).toBe('');expect(c.plan()?.totalRecords).toBe(0);
  });
});
