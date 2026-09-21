import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { routes } from '../../../app.routes';
import { API_BASE_URL } from '../../../core/config/api-config';
import { applicationTransitions } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

describe('Job Hunting admin feature', () => {
  let service: JobHuntingService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'https://api.example/api/v1' }] });
    service = TestBed.inject(JobHuntingService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('registers the lazy private routes under the guarded admin shell', () => {
    const admin = routes.find(x => x.path === 'admin')!;
    const feature = admin.children!.filter(x => x.path?.startsWith('job-hunting/'));
    expect(feature.map(x => x.path)).toEqual(['job-hunting/jobs','job-hunting/jobs/new/screenshots','job-hunting/jobs/new','job-hunting/jobs/:id','job-hunting/applications','job-hunting/applications/:id']);
    expect(feature.every(x => !!x.loadComponent)).toBe(true);
    expect(admin.canActivateChild).toHaveLength(1);
  });

  it('submits one multipart request with a stable id and all selected files', () => {
    const files=[new File(['one'],'one.png',{type:'image/png'}),new File(['two'],'two.jpg',{type:'image/jpeg'})];
    service.submitScreenshots('submission-1',files).subscribe();
    const request=http.expectOne('https://api.example/api/v1/admin/raw-job-postings/screenshots');
    expect(request.request.method).toBe('POST');expect(request.request.body).toBeInstanceOf(FormData);
    const body=request.request.body as FormData;expect(body.get('submissionId')).toBe('submission-1');expect(body.getAll('files')).toEqual(files);
    expect(request.request.headers.has('Content-Type')).toBe(false);
    request.flush({success:true,data:{rawJobPostingId:'raw-1',ingestionStatus:'RECEIVED',attachmentCount:2,created:true}});
  });

  it('lists received raw submissions and analyzes one without a client payload',()=>{
    service.rawJobPostings({page:1,pageSize:100,ingestionStatus:'RECEIVED'}).subscribe();
    const list=http.expectOne(r=>r.url.endsWith('/admin/raw-job-postings')&&r.params.get('ingestionStatus')==='RECEIVED');expect(list.request.method).toBe('GET');list.flush({success:true,data:{items:[],page:1,pageSize:100,total:0,totalPages:0}});
    service.analyzeRawJobPosting('raw-1').subscribe();const analyze=http.expectOne('https://api.example/api/v1/admin/raw-job-postings/raw-1/analyze');expect(analyze.request.method).toBe('POST');expect(analyze.request.body).toBeNull();analyze.flush({success:true,data:{id:'job-1'}});
  });

  it('uses server filters and exact posting mutation bodies', () => {
    service.jobs({page:2,pageSize:20,source:'MANUAL',archived:false}).subscribe();
    const list=http.expectOne(r=>r.url.endsWith('/admin/job-postings')&&r.params.get('page')==='2'&&r.params.get('source')==='MANUAL'&&r.params.get('archived')==='false');expect(list.request.method).toBe('GET');list.flush({success:true,data:{items:[],page:2,pageSize:20,total:0,totalPages:0}});
    service.verification('job','VERIFIED',3).subscribe();const state=http.expectOne('https://api.example/api/v1/admin/job-postings/job/verification');expect(state.request.method).toBe('PUT');expect(state.request.body).toEqual({status:'VERIFIED',expectedVersion:3});state.flush({success:true,data:{}});
    service.archive('job',4).subscribe();const archive=http.expectOne('https://api.example/api/v1/admin/job-postings/job/archive');expect(archive.request.method).toBe('POST');expect(archive.request.body).toEqual({expectedVersion:4});archive.flush({success:true,data:{}});
  });

  it('uses exact application transition and soft-remove endpoints', () => {
    service.applications({page:3,pageSize:20,search:'Acme',status:'APPLIED',channel:'EMAIL'}).subscribe();const list=http.expectOne(r=>r.url.endsWith('/admin/job-applications')&&r.params.get('page')==='3'&&r.params.get('pageSize')==='20'&&r.params.get('search')==='Acme'&&r.params.get('status')==='APPLIED'&&r.params.get('channel')==='EMAIL');expect(list.request.method).toBe('GET');list.flush({success:true,data:{items:[],page:3,pageSize:20,total:0,totalPages:0}});
    service.transition('app',{status:'APPLIED',expectedVersion:2,note:'sent'}).subscribe();const transition=http.expectOne('https://api.example/api/v1/admin/job-applications/app/status');expect(transition.request.method).toBe('PUT');expect(transition.request.body).toEqual({status:'APPLIED',expectedVersion:2,note:'sent'});transition.flush({success:true,data:{}});
    service.remove('app','doc').subscribe();const remove=http.expectOne('https://api.example/api/v1/admin/job-applications/app/documents/doc');expect(remove.request.method).toBe('DELETE');remove.flush(null);
  });

  it('exposes only valid transitions and none for terminal states', () => {
    expect(applicationTransitions.DRAFT).toEqual(['APPLIED','WITHDRAWN']);
    expect(applicationTransitions.INTERVIEW).toEqual(['REJECTED','OFFER','WITHDRAWN']);
    expect(applicationTransitions.REJECTED).toEqual([]);expect(applicationTransitions.WITHDRAWN).toEqual([]);
  });
});
