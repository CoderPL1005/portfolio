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
    expect(feature.map(x => x.path)).toEqual(['job-hunting/jobs','job-hunting/jobs/new/screenshots','job-hunting/jobs/new','job-hunting/jobs/:id','job-hunting/preferences','job-hunting/applications','job-hunting/applications/:id']);
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

  it('uses private preference and explicit fit-analysis endpoints',()=>{
    service.preferences().subscribe();const get=http.expectOne('https://api.example/api/v1/admin/job-hunting/preferences');expect(get.request.method).toBe('GET');get.flush({success:true,data:{version:0}});
    const body={expectedVersion:0,targetRoles:['Backend Developer'],preferredTechnologies:[],acceptableLocations:[],workplaceTypes:[],employmentTypes:[],minimumSalary:null,salaryCurrency:null,salaryPeriod:null};service.updatePreferences(body).subscribe();const put=http.expectOne('https://api.example/api/v1/admin/job-hunting/preferences');expect(put.request.method).toBe('PUT');expect(put.request.body).toEqual(body);put.flush({success:true,data:{version:1}});
    service.analyzeFit('job-1').subscribe();const fit=http.expectOne('https://api.example/api/v1/admin/job-postings/job-1/fit-analysis');expect(fit.request.method).toBe('GET');fit.flush({success:true,data:{jobPostingId:'job-1'}});
    service.emailApplication('job-1','request-1').subscribe();const email=http.expectOne('https://api.example/api/v1/admin/job-postings/job-1/email-application');expect(email.request.method).toBe('POST');expect(email.request.body).toEqual({clientRequestId:'request-1'});email.flush({success:true,data:{jobPostingId:'job-1',status:'SUCCEEDED'}});
  });

  it('uses authenticated canonical CV metadata, multipart upload, and blob content endpoints',()=>{
    service.canonicalCv().subscribe();const metadata=http.expectOne('https://api.example/api/v1/admin/job-hunting/canonical-cv');expect(metadata.request.method).toBe('GET');metadata.flush({success:true,data:{isConfigured:false,version:0}});
    const file=new File(['%PDF-test'],'CV.pdf',{type:'application/pdf'});service.uploadCanonicalCv(file,3).subscribe();const upload=http.expectOne('https://api.example/api/v1/admin/job-hunting/canonical-cv');expect(upload.request.method).toBe('PUT');expect(upload.request.body).toBeInstanceOf(FormData);const body=upload.request.body as FormData;const uploadedFile=body.get('file') as File;expect(uploadedFile).toBeInstanceOf(File);expect(uploadedFile.name).toBe('CV.pdf');expect(uploadedFile.type).toBe('application/pdf');expect(body.get('expectedVersion')).toBe('3');upload.flush({success:true,data:{isConfigured:true,version:4}});
    service.canonicalCvContent().subscribe(blob=>expect(blob.type).toBe('application/pdf'));const content=http.expectOne('https://api.example/api/v1/admin/job-hunting/canonical-cv/content');expect(content.request.method).toBe('GET');expect(content.request.responseType).toBe('blob');content.flush(new Blob(['%PDF-test'],{type:'application/pdf'}));
  });

  it('uses exact application transition and soft-remove endpoints', () => {
    service.applications({page:3,pageSize:20,search:'Acme',status:'APPLIED',channel:'EMAIL'}).subscribe();const list=http.expectOne(r=>r.url.endsWith('/admin/job-applications')&&r.params.get('page')==='3'&&r.params.get('pageSize')==='20'&&r.params.get('search')==='Acme'&&r.params.get('status')==='APPLIED'&&r.params.get('channel')==='EMAIL');expect(list.request.method).toBe('GET');list.flush({success:true,data:{items:[],page:3,pageSize:20,total:0,totalPages:0}});
    service.createApplication({jobPostingId:'job-1',expectedJobVersion:4}).subscribe();const create=http.expectOne('https://api.example/api/v1/admin/job-applications');expect(create.request.method).toBe('POST');expect(create.request.body).toEqual({jobPostingId:'job-1',expectedJobVersion:4});create.flush({success:true,data:{id:'app'}});
    service.transition('app',{status:'APPLIED',expectedVersion:2,note:'sent'}).subscribe();const transition=http.expectOne('https://api.example/api/v1/admin/job-applications/app/status');expect(transition.request.method).toBe('PUT');expect(transition.request.body).toEqual({status:'APPLIED',expectedVersion:2,note:'sent'});transition.flush({success:true,data:{}});
    service.packageReadiness('app').subscribe();const readiness=http.expectOne('https://api.example/api/v1/admin/job-hunting/applications/app/package-readiness');expect(readiness.request.method).toBe('GET');readiness.flush({success:true,data:{status:'NOT_READY',isReady:false,blockers:[],applicationVersion:2,jobPostingVersion:1,canonicalCvVersion:null,canonicalCv:null}});
    service.submissionReadiness('app').subscribe();const submission=http.expectOne('https://api.example/api/v1/admin/job-hunting/applications/app/submission-readiness');expect(submission.request.method).toBe('GET');submission.flush({success:true,data:{status:'NOT_READY_FOR_SUBMISSION',isReady:false,blockers:[],package:null}});
    service.submissionAttempts('app').subscribe();const attempts=http.expectOne('https://api.example/api/v1/admin/job-hunting/applications/app/submission-attempts');expect(attempts.request.method).toBe('GET');attempts.flush({success:true,data:[]});
    service.approveSubmissionAttempt('attempt',2).subscribe();const approve=http.expectOne('https://api.example/api/v1/admin/job-hunting/submission-attempts/attempt/approve');expect(approve.request.method).toBe('POST');expect(approve.request.body).toEqual({expectedVersion:2});approve.flush({success:true,data:{}});
    service.executeSubmissionAttempt('attempt',3).subscribe();const execute=http.expectOne('https://api.example/api/v1/admin/job-hunting/submission-attempts/attempt/execute');expect(execute.request.method).toBe('POST');expect(execute.request.body).toEqual({expectedVersion:3});execute.flush({success:true,data:{}});
    service.applicationPackage('app').subscribe();const packageState=http.expectOne('https://api.example/api/v1/admin/job-hunting/applications/app/package');expect(packageState.request.method).toBe('GET');packageState.flush({success:true,data:{status:'DRAFT',revision:0}});
    service.finalizeApplicationPackage('app',{expectedApplicationVersion:2,expectedJobPostingVersion:4,expectedCanonicalCvVersion:3}).subscribe();const finalize=http.expectOne('https://api.example/api/v1/admin/job-hunting/applications/app/package/finalize');expect(finalize.request.method).toBe('POST');expect(finalize.request.body).toEqual({expectedApplicationVersion:2,expectedJobPostingVersion:4,expectedCanonicalCvVersion:3});finalize.flush({success:true,data:{status:'FINALIZED',revision:1}});
    service.applicationPackageCv('app',true).subscribe();const packageCv=http.expectOne(r=>r.url==='https://api.example/api/v1/admin/job-hunting/applications/app/package/cv'&&r.params.get('download')==='true');expect(packageCv.request.method).toBe('GET');expect(packageCv.request.responseType).toBe('blob');packageCv.flush(new Blob(['%PDF-test'],{type:'application/pdf'}));
    service.remove('app','doc').subscribe();const remove=http.expectOne('https://api.example/api/v1/admin/job-applications/app/documents/doc');expect(remove.request.method).toBe('DELETE');remove.flush(null);
  });

  it('exposes only valid transitions and none for terminal states', () => {
    expect(applicationTransitions.DRAFT).toEqual(['APPLIED','WITHDRAWN']);
    expect(applicationTransitions.INTERVIEW).toEqual(['REJECTED','OFFER','WITHDRAWN']);
    expect(applicationTransitions.REJECTED).toEqual([]);expect(applicationTransitions.WITHDRAWN).toEqual([]);
  });
});
