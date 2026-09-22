import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ApplicationDetailPageComponent } from './application-detail-page.component';
import { ApplicationListPageComponent } from './application-list-page.component';
import { JobEditPageComponent } from './job-edit-page.component';
import { JobListPageComponent } from './job-list-page.component';
import { ApplicationDetail, ApplicationItem, ApplicationPackage, ApplicationPackageReadiness, JobDetail, JobFitAnalysis, JobSummary } from './job-hunting.models';
import { JobHuntingService } from './job-hunting.service';

const paged=<T>(items:T[],page=1,totalPages=1)=>({items,page,pageSize:20,total:items.length,totalPages});
const jobSummary=(overrides:Partial<JobSummary>={}):JobSummary=>({id:'job-1',companyName:'Acme',positionTitle:'Developer',location:'Remote',source:'MANUAL',sourceCount:1,verificationStatus:'PENDING',selectionStatus:'PENDING_ANALYSIS',archived:false,createdAt:'2026-01-01',updatedAt:'2026-01-02',applicationCount:0,version:1,...overrides});
const jobDetail=(overrides:Partial<JobDetail>={}):JobDetail=>({id:'job-1',companyName:'Acme',positionTitle:'Developer',location:'Remote',employmentType:null,workplaceType:null,salaryMinimum:null,salaryMaximum:null,salaryCurrency:null,salaryPeriod:null,experienceRequirements:null,description:'Build things',technologyStack:['Angular'],applicationEmail:null,applicationUrl:null,verificationStatus:'PENDING',selectionStatus:'PENDING_ANALYSIS',expiresAt:null,verifiedAt:null,archivedAt:null,notes:null,version:1,createdAt:'2026-01-01',updatedAt:'2026-01-02',sources:[{id:'source-1',source:'MANUAL',sourceExternalId:null,sourceUrl:null,sourceUrlHash:null,rawContent:'Original immutable source',contentHash:'hash',companyTitleFingerprint:null,ingestionStatus:'NORMALIZED',duplicateOfRawJobPostingId:null,metadata:{},discoveredAt:'2026-01-01',createdAt:'2026-01-01',updatedAt:'2026-01-01'}],applications:[],...overrides});
const fitAnalysis=(overrides:Partial<JobFitAnalysis>={}):JobFitAnalysis=>({jobPostingId:'job-1',jobVersion:1,preferenceVersion:1,overallScore:82,availableWeight:75,totalConfiguredWeight:100,coveragePercent:75,jobVerificationStatus:'PENDING',recommendation:'RECOMMENDED',reasons:['Location matches a configured preference.'],concerns:['Salary is not comparable.'],components:[{key:'SALARY',label:'Salary',score:null,configuredWeight:5,status:'UNKNOWN',explanation:'Salary is not comparable.',evidence:[]},{key:'TECHNICAL_CAPABILITY',label:'Technical capability',score:50,configuredWeight:30,status:'PARTIAL',explanation:'One match.',evidence:['Angular']}],matchedTechnologies:['Angular'],developingTechnologies:['Docker'],missingTechnologies:['PostgreSQL'],unknownFactors:['Salary'],...overrides});
const applicationItem=(overrides:Partial<ApplicationItem>={}):ApplicationItem=>({id:'app-1',jobPostingId:'job-1',companyName:'Acme',positionTitle:'Developer',status:'DRAFT',channel:'MANUAL',appliedAt:null,lastActivityAt:'2026-01-02',version:1,...overrides});
const applicationDetail=(overrides:Partial<ApplicationDetail>={}):ApplicationDetail=>({id:'app-1',jobPostingId:'job-1',status:'DRAFT',channel:'MANUAL',applicationEmail:null,applicationUrl:null,externalApplicationId:null,appliedAt:null,lastActivityAt:'2026-01-02',notes:null,version:2,createdAt:'2026-01-01',updatedAt:'2026-01-02',job:{id:'job-1',companyName:'Acme',positionTitle:'Developer',location:'Remote',verificationStatus:'VERIFIED',selectionStatus:'APPROVED',archived:false},events:[{id:'event-2',eventType:'STATUS_CHANGED',fromStatus:'DRAFT',toStatus:'APPLIED',actorType:'ADMIN',actorAdminUserId:null,note:'Submitted',metadata:{},occurredAt:'2026-01-02',createdAt:'2026-01-02'},{id:'event-1',eventType:'CREATED',fromStatus:null,toStatus:'DRAFT',actorType:'ADMIN',actorAdminUserId:null,note:null,metadata:{},occurredAt:'2026-01-01',createdAt:'2026-01-01'}],documents:[{id:'doc-1',documentType:'CV',versionLabel:'v1',fileName:'cv.pdf',storageKey:null,contentHash:null,metadata:{},createdAt:'2026-01-01',removedAt:null},{id:'doc-2',documentType:'COVER_LETTER',versionLabel:'v1',fileName:null,storageKey:'removed',contentHash:null,metadata:{},createdAt:'2026-01-01',removedAt:'2026-01-03'}],...overrides});
const packageReadiness=(overrides:Partial<ApplicationPackageReadiness>={}):ApplicationPackageReadiness=>({status:'READY_TO_FINALIZE',isReady:true,blockers:[],applicationVersion:2,jobPostingVersion:4,canonicalCvVersion:3,canonicalCv:{fileName:'canonical.pdf',contentType:'application/pdf',fileSizeBytes:1024,contentHash:'a'.repeat(64),version:3,updatedAt:'2026-01-02'},...overrides});
const applicationPackage=(overrides:Partial<ApplicationPackage>={}):ApplicationPackage=>({status:'DRAFT',revision:0,jobPostingVersion:null,manifestHash:null,finalizedAt:null,finalizedByAdminUserId:null,cv:null,...overrides});

function mount<T>(component:new(...args:any[])=>T,api:Record<string,any>,id?:string):ComponentFixture<T>{
  const service={packageReadiness:()=>of(packageReadiness()),applicationPackage:()=>of(applicationPackage()),...api};
  TestBed.configureTestingModule({imports:[component],providers:[provideRouter([]),{provide:ActivatedRoute,useValue:{snapshot:{paramMap:convertToParamMap(id?{id}:{})}}},{provide:JobHuntingService,useValue:service}]});
  const fixture=TestBed.createComponent(component);fixture.detectChanges();return fixture;
}

describe('Job Hunting list pages',()=>{
  afterEach(()=>TestBed.resetTestingModule());
  it('renders job loading, populated, empty, and error states',()=>{
    const pending=new Subject<ReturnType<typeof paged<JobSummary>>>();const api={jobs:vi.fn(()=>pending)};const fixture=mount(JobListPageComponent,api);
    expect(fixture.nativeElement.textContent).toContain('Loading jobs');
    pending.next(paged([jobSummary()]));pending.complete();fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Acme');expect(fixture.nativeElement.textContent).toContain('Developer');
    fixture.componentInstance.items.set([]);fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('No jobs match');
    fixture.componentInstance.error.set('Unable to load');fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Unable to load');
  });
  it('shows the safe error state when the job request fails',()=>{const fixture=mount(JobListPageComponent,{jobs:()=>throwError(()=>new Error('private detail'))});expect(fixture.componentInstance.loading()).toBe(false);expect(fixture.componentInstance.error()).toBe('The request could not be completed.');expect(fixture.nativeElement.textContent).not.toContain('private detail')});
  it('sends job filters, archived semantics, paging, reset, and enforces page boundaries',()=>{
    const api={jobs:vi.fn(()=>of(paged([jobSummary()],1,3)))};const fixture=mount(JobListPageComponent,api);const c=fixture.componentInstance;
    expect(api.jobs).toHaveBeenLastCalledWith({page:1,pageSize:20,archived:false});
    c.search='Acme';c.source='MANUAL';c.verification='VERIFIED';c.selection='APPROVED';c.archived='archived';c.apply();
    expect(api.jobs).toHaveBeenLastCalledWith({page:1,pageSize:20,search:'Acme',source:'MANUAL',verificationStatus:'VERIFIED',selectionStatus:'APPROVED',archived:true});
    c.totalPages.set(3);c.go(2);expect(api.jobs).toHaveBeenLastCalledWith(expect.objectContaining({page:2,archived:true}));const calls=api.jobs.mock.calls.length;c.go(4);expect(api.jobs).toHaveBeenCalledTimes(calls);
    c.reset();expect(api.jobs).toHaveBeenLastCalledWith({page:1,pageSize:20,archived:false});expect(c.archived).toBe('active');
    c.archived='all';c.apply();expect(api.jobs).toHaveBeenLastCalledWith({page:1,pageSize:20});
  });
  it('exposes create and detail navigation links',async()=>{
    TestBed.configureTestingModule({providers:[provideRouter([{path:'jobs',component:JobListPageComponent}]),{provide:JobHuntingService,useValue:{jobs:()=>of(paged([jobSummary()]))}}]});const harness=await RouterTestingHarness.create('/jobs');const links=[...harness.routeNativeElement!.querySelectorAll('a')] as HTMLAnchorElement[];
    expect(links.find(x=>x.textContent?.includes('New manual job'))?.getAttribute('href')).toContain('new');expect(links.find(x=>x.textContent?.includes('New from screenshots'))?.getAttribute('href')).toContain('new/screenshots');expect(links.find(x=>x.textContent?.includes('Open'))?.getAttribute('href')).toContain('job-1');
  });
  it('renders application loading, cards, status, null channel, empty, and error states',()=>{
    const pending=new Subject<ReturnType<typeof paged<ApplicationItem>>>();const fixture=mount(ApplicationListPageComponent,{applications:()=>pending});expect(fixture.nativeElement.textContent).toContain('Loading applications');
    pending.next(paged([applicationItem({channel:null})]));pending.complete();fixture.detectChanges();expect(fixture.nativeElement.querySelector('.application-card')).not.toBeNull();expect(fixture.nativeElement.textContent).toContain('Acme');expect(fixture.nativeElement.textContent).toContain('Draft');expect(fixture.nativeElement.textContent).toContain('No channel specified');
    fixture.componentInstance.items.set([]);fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('No applications match');fixture.componentInstance.error.set('Failed');fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Failed');
  });
  it('shows the safe error state when the application request fails',()=>{const fixture=mount(ApplicationListPageComponent,{applications:()=>throwError(()=>new Error('private detail'))});expect(fixture.componentInstance.loading()).toBe(false);expect(fixture.componentInstance.error()).toBe('The request could not be completed.');expect(fixture.nativeElement.textContent).not.toContain('private detail')});
  it('sends application search/status/channel and page parameters with guarded pagination',()=>{
    const api={applications:vi.fn(()=>of(paged([applicationItem()],1,4)))};const fixture=mount(ApplicationListPageComponent,api);const c=fixture.componentInstance;
    c.search='Acme';c.status='APPLIED';c.channel='EMAIL';c.apply();expect(api.applications).toHaveBeenLastCalledWith({page:1,pageSize:20,search:'Acme',status:'APPLIED',channel:'EMAIL'});
    c.totalPages.set(4);c.go(2);expect(api.applications).toHaveBeenLastCalledWith(expect.objectContaining({page:2,pageSize:20}));const calls=api.applications.mock.calls.length;c.go(0);expect(api.applications).toHaveBeenCalledTimes(calls);c.reset();expect(api.applications).toHaveBeenLastCalledWith({page:1,pageSize:20});
  });
  it('links each application to its detail page',async()=>{TestBed.configureTestingModule({providers:[provideRouter([{path:'applications',component:ApplicationListPageComponent}]),{provide:JobHuntingService,useValue:{applications:()=>of(paged([applicationItem()]))}}]});const harness=await RouterTestingHarness.create('/applications');expect((harness.routeNativeElement!.querySelector('a') as HTMLAnchorElement).getAttribute('href')).toContain('app-1')});
});

describe('Job editor workflows',()=>{
  afterEach(()=>{vi.restoreAllMocks();TestBed.resetTestingModule()});
  it('renders extracted values, readable state labels, and technology chips',()=>{
    const existing=jobDetail({companyName:'GAP GLOBAL',positionTitle:'Backend Java',location:'Ha Noi',employmentType:'FULL_TIME',workplaceType:'HYBRID',salaryMinimum:25000000,salaryMaximum:35000000,salaryCurrency:'VND',salaryPeriod:'MONTHLY',experienceRequirements:'Three years',description:'Build reliable APIs',technologyStack:['Java','Spring Boot','RESTful API']});
    const fixture=mount(JobEditPageComponent,{job:()=>of(existing)},'job-1');const text=fixture.nativeElement.textContent as string;
    expect(text).toContain('Backend Java');expect(text).toContain('GAP GLOBAL');expect(text).toContain('Pending analysis');expect(text).not.toContain('PENDING_ANALYSIS');
    expect([...fixture.nativeElement.querySelectorAll('.technology-preview li')].map((x:Element)=>x.textContent?.trim())).toEqual(['Java','Spring Boot','RESTful API']);
    expect(fixture.componentInstance.form.controls.salaryMinimum.value).toBe(25000000);expect(fixture.componentInstance.form.controls.description.value).toBe('Build reliable APIs');
  });
  it('runs fit analysis only after explicit action and renders score, coverage, statuses, and evidence',()=>{
    const fit=fitAnalysis();
    const api={job:()=>of(jobDetail()),analyzeFit:vi.fn(()=>of(fit))};const fixture=mount(JobEditPageComponent,api,'job-1');
    expect(api.analyzeFit).not.toHaveBeenCalled();expect(fixture.nativeElement.textContent).toContain('runs only when you choose');
    fixture.componentInstance.analyzeFit();fixture.detectChanges();expect(api.analyzeFit).toHaveBeenCalledTimes(1);const text=fixture.nativeElement.textContent as string;
    expect(text).toContain('82 / 100');expect(text).toContain('75%');expect(text).toContain('Recommended');expect(text).toContain('Why this recommendation');expect(text).toContain('Location matches a configured preference.');expect(text).toContain('Needs review');expect(text).toContain('Salary is not comparable.');expect(text).toContain('Unknown');expect(text).toContain('Partial');expect(text).toContain('Angular');expect(text).toContain('Docker');expect(text).toContain('PostgreSQL');expect(text).not.toContain('chance of getting hired');expect(fixture.componentInstance.job()?.selectionStatus).toBe('PENDING_ANALYSIS');
  });
  it.each([
    ['technology stack', {technologyStack:'Angular, PostgreSQL'}],
    ['location', {location:'Hanoi'}],
    ['salary', {salaryMaximum:35000000}],
  ])('invalidates fit analysis after a user edits %s',(_label,change)=>{
    const api={job:()=>of(jobDetail()),analyzeFit:vi.fn(()=>of(fitAnalysis()))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.analyzeFit();fixture.detectChanges();expect(c.fitAnalysis()).not.toBeNull();expect(fixture.nativeElement.textContent).toContain('Recommended');expect(fixture.nativeElement.querySelectorAll('.decision-actions button')).toHaveLength(2);
    c.form.patchValue(change);fixture.detectChanges();expect(c.fitAnalysis()).toBeNull();expect(fixture.nativeElement.textContent).not.toContain('Why this recommendation');expect(fixture.nativeElement.querySelector('.decision-actions')).toBeNull();
  });
  it('keeps analysis invalid after editing and saving a newer job version',()=>{
    const api={job:()=>of(jobDetail()),analyzeFit:vi.fn(()=>of(fitAnalysis())),updateJob:vi.fn(()=>of(jobDetail({version:2,technologyStack:['Angular','PostgreSQL']})))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.analyzeFit();c.form.patchValue({technologyStack:'Angular, PostgreSQL'});expect(c.fitAnalysis()).toBeNull();c.save();fixture.detectChanges();
    expect(api.updateJob).toHaveBeenCalledWith('job-1',expect.objectContaining({expectedVersion:1,technologyStack:['Angular','PostgreSQL']}));expect(c.job()?.version).toBe(2);expect(c.fitAnalysis()).toBeNull();expect(fixture.nativeElement.textContent).not.toContain('Recommended');expect(fixture.nativeElement.querySelector('.decision-actions')).toBeNull();
  });
  it('does not treat same-version server form synchronization as a user edit',()=>{
    const api={job:vi.fn(()=>of(jobDetail())),analyzeFit:vi.fn(()=>of(fitAnalysis()))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.analyzeFit();expect(c.fitAnalysis()).not.toBeNull();c.load();fixture.detectChanges();
    expect(api.job).toHaveBeenCalledTimes(2);expect(c.fitAnalysis()?.recommendation).toBe('RECOMMENDED');expect(fixture.nativeElement.textContent).toContain('Recommended');
  });
  it('renders a fresh analysis after edit invalidation and explicit re-analysis',()=>{
    const first=fitAnalysis({overallScore:82,recommendation:'RECOMMENDED'});const second=fitAnalysis({overallScore:55,recommendation:'NEEDS_REVIEW',reasons:[],concerns:['Review the changed job.']});const api={job:()=>of(jobDetail()),analyzeFit:vi.fn().mockReturnValueOnce(of(first)).mockReturnValueOnce(of(second))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.analyzeFit();c.form.patchValue({location:'Hanoi'});expect(c.fitAnalysis()).toBeNull();c.analyzeFit();fixture.detectChanges();
    expect(api.analyzeFit).toHaveBeenCalledTimes(2);expect(c.fitAnalysis()?.overallScore).toBe(55);expect(fixture.nativeElement.textContent).toContain('Needs review');expect(fixture.nativeElement.textContent).toContain('Review the changed job.');
  });
  it('requires raw content in create mode and sends the exact manual-ingestion payload',()=>{
    const created=jobDetail();const api={createJob:vi.fn(()=>of(created))};const fixture=mount(JobEditPageComponent,api);const c=fixture.componentInstance;const router=TestBed.inject(Router);vi.spyOn(router,'navigate').mockResolvedValue(true);
    c.form.patchValue({companyName:'Acme',positionTitle:'Developer',location:'Remote',description:'Description',technologyStack:'Angular, .NET'});c.save();expect(api.createJob).not.toHaveBeenCalled();expect(c.sourceForm.controls.rawContent.hasError('required')).toBe(true);
    c.sourceForm.patchValue({source:'MANUAL',sourceUrl:'https://example.test/job',sourceExternalId:'42',rawContent:'raw vacancy'});c.sourceForm.markAsDirty();expect(c.hasUnsavedChanges()).toBe(true);c.save();
    expect(api.createJob).toHaveBeenCalledWith(expect.objectContaining({source:'MANUAL',sourceUrl:'https://example.test/job',sourceExternalId:'42',rawContent:'raw vacancy',companyName:'Acme',technologyStack:['Angular','.NET']}));expect(router.navigate).toHaveBeenCalledWith(['/admin/job-hunting/jobs','job-1']);
  });
  it('keeps create input dirty and displays a safe API validation failure',()=>{const api={createJob:vi.fn(()=>throwError(()=>({status:400})))};const fixture=mount(JobEditPageComponent,api);const c=fixture.componentInstance;c.form.patchValue({companyName:'Acme',positionTitle:'Developer',location:'Remote',description:'Description'});c.sourceForm.patchValue({rawContent:'raw'});c.form.markAsDirty();c.save();expect(c.error()).toBe('The request could not be completed.');expect(c.hasUnsavedChanges()).toBe(true);expect(c.saving()).toBe(false)});
  it('serializes technologies unchanged and blocks duplicate saves while pending',()=>{
    const pending=new Subject<JobDetail>();const api={job:()=>of(jobDetail()),updateJob:vi.fn(()=>pending)};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.form.patchValue({technologyStack:'Java, Spring Boot\nRESTful API'});c.save();fixture.detectChanges();c.save();
    expect(api.updateJob).toHaveBeenCalledTimes(1);expect(api.updateJob).toHaveBeenCalledWith('job-1',expect.objectContaining({expectedVersion:1,technologyStack:['Java','Spring Boot','RESTful API']}));
    expect(c.saving()).toBe(true);expect((fixture.nativeElement.querySelector('.save-button') as HTMLButtonElement).disabled).toBe(true);
    pending.next(jobDetail({version:2,technologyStack:['Java','Spring Boot','RESTful API']}));pending.complete();expect(c.saving()).toBe(false);
  });
  it('loads normalized edit fields without source validation and omits all raw-source fields on update',()=>{
    const existing=jobDetail();const api={job:vi.fn(()=>of(existing)),updateJob:vi.fn((_id:string,_body:Record<string,unknown>)=>of(jobDetail({version:2})))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    expect(c.form.controls.companyName.value).toBe('Acme');expect(c.form.valid).toBe(true);expect(fixture.nativeElement.querySelector('textarea[formControlName="rawContent"]')).toBeNull();expect(fixture.nativeElement.textContent).toContain('Original immutable source');
    c.form.patchValue({companyName:'Acme Updated'});c.save();expect(api.updateJob).toHaveBeenCalledTimes(1);const body=api.updateJob.mock.calls[0][1];expect(body['expectedVersion']).toBe(1);expect(body['companyName']).toBe('Acme Updated');expect(body).not.toHaveProperty('source');expect(body).not.toHaveProperty('sourceUrl');expect(body).not.toHaveProperty('sourceExternalId');expect(body).not.toHaveProperty('rawContent');expect(c.job()?.version).toBe(2);
  });
  it('uses current versions for state mutations and confirms archive',()=>{
    vi.spyOn(window,'confirm').mockReturnValue(true);const next=jobDetail({version:2,verificationStatus:'VERIFIED'});const api={job:()=>of(jobDetail()),verification:vi.fn(()=>of(next)),selection:vi.fn(()=>of(jobDetail({version:3,selectionStatus:'APPROVED'}))),archive:vi.fn(()=>of(jobDetail({version:4,archivedAt:'2026-02-01'})))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    c.verify('VERIFIED');expect(api.verification).toHaveBeenCalledWith('job-1','VERIFIED',1);expect(c.job()?.version).toBe(2);c.select('APPROVED');expect(api.selection).toHaveBeenCalledWith('job-1','APPROVED',2);c.archive();expect(api.archive).toHaveBeenCalledWith('job-1',3);
  });
  it.each([
    ['PENDING_ANALYSIS',null],['RECOMMENDED',null],['SKIPPED',null],['APPROVED','2026-02-01'],
  ])('does not allow application creation for selection %s with archive %s',(selectionStatus,archivedAt)=>{
    const api={job:()=>of(jobDetail({selectionStatus:selectionStatus as JobDetail['selectionStatus'],archivedAt})),createApplication:vi.fn(()=>of(applicationDetail()))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;
    expect(fixture.nativeElement.textContent).not.toContain('Create application');c.createApplication();expect(api.createApplication).not.toHaveBeenCalled();
  });
  it('creates one draft explicitly with the current job version and blocks duplicate clicks',()=>{
    const pending=new Subject<ApplicationDetail>();const api={job:()=>of(jobDetail({selectionStatus:'APPROVED',version:4})),createApplication:vi.fn(()=>pending)};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;const router=TestBed.inject(Router);vi.spyOn(router,'navigate').mockResolvedValue(true);
    expect(fixture.nativeElement.textContent).toContain('Create application');c.createApplication();c.createApplication();fixture.detectChanges();expect(api.createApplication).toHaveBeenCalledTimes(1);expect(api.createApplication).toHaveBeenCalledWith({jobPostingId:'job-1',expectedJobVersion:4});expect(c.applicationCreating()).toBe(true);const createButton=[...fixture.nativeElement.querySelectorAll('button')].find((button:HTMLButtonElement)=>button.textContent?.includes('Creating')) as HTMLButtonElement;expect(createButton.disabled).toBe(true);
    pending.next(applicationDetail({status:'DRAFT',version:1}));pending.complete();expect(c.applicationCreating()).toBe(false);expect(router.navigate).toHaveBeenCalledWith(['/admin/job-hunting/applications','app-1']);
  });
  it('shows Open application instead of duplicate creation when one exists',()=>{
    const current=jobDetail({selectionStatus:'APPROVED',applications:[{id:'app-existing',status:'DRAFT',channel:null,appliedAt:null,lastActivityAt:'2026-01-02',version:1}]});const fixture=mount(JobEditPageComponent,{job:()=>of(current)},'job-1');const link=fixture.nativeElement.querySelector('a.admin-button.primary') as HTMLAnchorElement;
    expect(link.textContent).toContain('Open application');expect(link.getAttribute('href')).toContain('app-existing');expect(fixture.nativeElement.textContent).not.toContain('Create application');
  });
  it('recovers the Create application action after a backend failure',()=>{
    const api={job:()=>of(jobDetail({selectionStatus:'APPROVED'})),createApplication:vi.fn(()=>throwError(()=>({status:409})))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;c.createApplication();fixture.detectChanges();expect(c.applicationCreating()).toBe(false);expect(c.error()).toBeTruthy();expect(fixture.nativeElement.textContent).toContain('Create application');
  });
  it('submits an explicit human decision once, waits for the server, and rebinds status and version',()=>{
    const pending=new Subject<JobDetail>();const api={job:()=>of(jobDetail()),analyzeFit:()=>of(fitAnalysis()),selection:vi.fn(()=>pending)};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;c.fitAnalysis.set(fitAnalysis({reasons:[],concerns:[]}));fixture.detectChanges();
    c.select('APPROVED');c.select('APPROVED');fixture.detectChanges();expect(api.selection).toHaveBeenCalledTimes(1);expect(api.selection).toHaveBeenCalledWith('job-1','APPROVED',1);expect(c.job()?.selectionStatus).toBe('PENDING_ANALYSIS');expect(c.selectionAction()).toBe('APPROVED');expect(fixture.nativeElement.textContent).toContain('Approving...');expect([...fixture.nativeElement.querySelectorAll('.decision-actions button')].every((button:HTMLButtonElement)=>button.disabled)).toBe(true);
    pending.next(jobDetail({selectionStatus:'APPROVED',version:2}));pending.complete();fixture.detectChanges();expect(c.job()?.selectionStatus).toBe('APPROVED');expect(c.job()?.version).toBe(2);expect(c.selectionAction()).toBeNull();expect(fixture.nativeElement.textContent).toContain('Human decision: Approved');
  });
  it('recovers selection controls after backend failure without faking success',()=>{const api={job:vi.fn(()=>of(jobDetail())),selection:vi.fn(()=>throwError(()=>({status:400})))};const fixture=mount(JobEditPageComponent,api,'job-1');const c=fixture.componentInstance;c.fitAnalysis.set(fitAnalysis({recommendation:'NEEDS_REVIEW',reasons:[],concerns:[]}));c.select('SKIPPED');expect(api.selection).toHaveBeenCalledWith('job-1','SKIPPED',1);expect(c.selectionAction()).toBeNull();expect(c.job()?.selectionStatus).toBe('PENDING_ANALYSIS');expect(c.job()?.version).toBe(1);expect(c.error()).toBeTruthy()});
  it('shows a stale-version error and reloads the latest job after a 409',()=>{
    const api={job:vi.fn().mockReturnValueOnce(of(jobDetail())).mockReturnValueOnce(of(jobDetail({version:7}))),updateJob:vi.fn(()=>throwError(()=>({status:409})))};const fixture=mount(JobEditPageComponent,api,'job-1');fixture.componentInstance.form.patchValue({companyName:'Changed'});fixture.componentInstance.save();expect(api.job).toHaveBeenCalledTimes(2);expect(fixture.componentInstance.job()?.version).toBe(7);expect(fixture.componentInstance.error()).toBeTruthy();
  });
});

describe('Application detail workflows',()=>{
  afterEach(()=>{vi.restoreAllMocks();TestBed.resetTestingModule()});
  it.each([
    ['DRAFT',['Mark as applied','Withdraw']],['APPLIED',['Move to interview','Reject','Withdraw']],['INTERVIEW',['Reject','Mark offer received','Withdraw']],['OFFER',['Withdraw']],
  ] as [ApplicationDetail['status'],string[]][])('renders only valid %s actions', (status,labels)=>{
    const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail({status}))},'app-1');
    expect([...fixture.nativeElement.querySelectorAll('.transition-actions button')].map((button:HTMLButtonElement)=>button.textContent?.trim())).toEqual(labels);
  });
  it('shows a readiness loading state without exposing a finalize action',()=>{
    const pending=new Subject<ApplicationPackageReadiness>();const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail()),packageReadiness:()=>pending},'app-1');expect(fixture.nativeElement.textContent).toContain('Checking package readiness');expect(fixture.nativeElement.textContent).not.toContain('Finalize');pending.next(packageReadiness());pending.complete();fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Ready to finalize');
  });
  it('renders a ready package with safe canonical CV metadata and current versions',()=>{
    const api={application:()=>of(applicationDetail()),packageReadiness:vi.fn(()=>of(packageReadiness()))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const text=fixture.nativeElement.textContent as string;
    expect(api.packageReadiness).toHaveBeenCalledWith('app-1');expect(text).toContain('Ready to finalize');expect(text).toContain('Application v2');expect(text).toContain('Job v4');expect(text).toContain('CV v3');expect(text).toContain('canonical.pdf');expect(text).not.toContain('canonical-cv/private.pdf');
  });
  it('finalizes once with readiness versions and renders immutable package actions',()=>{
    const finalized=applicationPackage({status:'FINALIZED',revision:1,jobPostingVersion:4,manifestHash:'b'.repeat(64),finalizedAt:'2026-09-22T08:00:00Z',finalizedByAdminUserId:'admin-1',cv:{documentId:'doc-1',fileName:'canonical.pdf',contentType:'application/pdf',fileSizeBytes:1024,contentHash:'a'.repeat(64),packageRevision:1,sourceCanonicalCvVersion:3,createdAt:'2026-09-22T08:00:00Z'}});const pending=new Subject<ApplicationPackage>();const api={application:()=>of(applicationDetail()),packageReadiness:()=>of(packageReadiness()),applicationPackage:()=>of(applicationPackage()),finalizeApplicationPackage:vi.fn(()=>pending)};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;
    c.finalizePackage();c.finalizePackage();fixture.detectChanges();expect(api.finalizeApplicationPackage).toHaveBeenCalledTimes(1);expect(api.finalizeApplicationPackage).toHaveBeenCalledWith('app-1',{expectedApplicationVersion:2,expectedJobPostingVersion:4,expectedCanonicalCvVersion:3});expect(c.finalizing()).toBe(true);pending.next(finalized);pending.complete();fixture.detectChanges();expect(c.finalizing()).toBe(false);expect(fixture.nativeElement.textContent).toContain('Finalized');expect(fixture.nativeElement.textContent).toContain('Package revision 1');expect(fixture.nativeElement.textContent).toContain('Preview finalized CV');expect(fixture.nativeElement.textContent).toContain('Download finalized CV');expect(fixture.nativeElement.textContent).not.toContain('Reopen');expect(fixture.nativeElement.textContent).not.toContain('Generate CV');
  });
  it('does not show finalize for not-ready or already-finalized packages',()=>{
    const notReady=packageReadiness({status:'NOT_READY',isReady:false,blockers:[{code:'CANONICAL_CV_MISSING',message:'A canonical CV has not been configured.'}],canonicalCv:null,canonicalCvVersion:null});const draft=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail()),packageReadiness:()=>of(notReady)},'app-1');expect(draft.nativeElement.textContent).not.toContain('Finalize package');TestBed.resetTestingModule();const finalized=applicationPackage({status:'FINALIZED',revision:1,manifestHash:'b'.repeat(64),finalizedAt:'2026-09-22',cv:{documentId:'doc',fileName:'cv.pdf',contentType:'application/pdf',fileSizeBytes:10,contentHash:'a'.repeat(64),packageRevision:1,sourceCanonicalCvVersion:1,createdAt:'2026-09-22'}});const done=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail()),applicationPackage:()=>of(finalized)},'app-1');expect(done.nativeElement.textContent).not.toContain('Finalize package');expect(done.nativeElement.textContent).not.toContain('Reopen');
  });
  it('previews and downloads finalized CV through authenticated blob APIs',()=>{
    vi.useFakeTimers();const finalized=applicationPackage({status:'FINALIZED',revision:1,manifestHash:'b'.repeat(64),finalizedAt:'2026-09-22',cv:{documentId:'doc',fileName:'cv.pdf',contentType:'application/pdf',fileSizeBytes:10,contentHash:'a'.repeat(64),packageRevision:1,sourceCanonicalCvVersion:1,createdAt:'2026-09-22'}});const create=vi.spyOn(URL,'createObjectURL').mockReturnValue('blob:package');const revoke=vi.spyOn(URL,'revokeObjectURL').mockImplementation(()=>{});const open=vi.spyOn(window,'open').mockReturnValue(null);const click=vi.spyOn(HTMLAnchorElement.prototype,'click').mockImplementation(()=>{});const api={application:()=>of(applicationDetail()),applicationPackage:()=>of(finalized),applicationPackageCv:vi.fn(()=>of(new Blob(['%PDF-test'],{type:'application/pdf'})))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');fixture.componentInstance.openPackageCv(false);fixture.componentInstance.openPackageCv(true);expect(api.applicationPackageCv).toHaveBeenNthCalledWith(1,'app-1',false);expect(api.applicationPackageCv).toHaveBeenNthCalledWith(2,'app-1',true);expect(open).toHaveBeenCalledWith('blob:package','_blank','noopener,noreferrer');expect(click).toHaveBeenCalled();vi.advanceTimersByTime(60_000);expect(revoke).toHaveBeenCalledTimes(2);fixture.destroy();vi.useRealTimers();
  });
  it('renders every not-ready blocker and a safe independent error state',()=>{
    const state=packageReadiness({status:'NOT_READY',isReady:false,blockers:[{code:'APPLICATION_NOT_DRAFT',message:'The application must be in DRAFT status.'},{code:'JOB_ARCHIVED',message:'The associated job posting is archived.'}],canonicalCv:null,canonicalCvVersion:null});
    const packageRequest=vi.fn().mockReturnValueOnce(of(state)).mockReturnValueOnce(throwError(()=>new Error('private')));const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail()),packageReadiness:packageRequest},'app-1');expect(fixture.nativeElement.textContent).toContain('Not ready');expect(fixture.nativeElement.textContent).toContain('Application Not Draft');expect(fixture.nativeElement.textContent).toContain('Job Archived');
    fixture.componentInstance.loadReadiness();fixture.detectChanges();expect(fixture.componentInstance.readinessError()).toBe('The request could not be completed.');expect(fixture.componentInstance.item()).not.toBeNull();
  });
  it.each(['REJECTED','WITHDRAWN'] as ApplicationDetail['status'][])('renders %s as a terminal outcome with no actions',(status)=>{const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail({status}))},'app-1');expect(fixture.nativeElement.textContent).toContain('terminal outcome');expect(fixture.nativeElement.querySelector('.terminal-outcome')).not.toBeNull();expect(fixture.nativeElement.querySelector('.transition-actions')).toBeNull()});
  it('renders readable metadata, immutable activity, documents, and original-job navigation',()=>{
    const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail())},'app-1');const c=fixture.componentInstance;expect(c.form.controls.channel.value).toBe('MANUAL');expect(c.hasUnsavedChanges()).toBe(false);
    const text=fixture.nativeElement.textContent as string;expect(text).toContain('Not specified');expect(text.indexOf('Status changed')).toBeLessThan(text.indexOf('Application created'));expect(text).toContain('Draft → Applied');expect(text).toContain('Submitted');expect(text).toContain('cv.pdf');expect(text).toContain('Removed');expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeNull();expect((fixture.nativeElement.querySelector('.header-actions a') as HTMLAnchorElement).getAttribute('href')).toContain('/admin/job-hunting/jobs/job-1');
  });
  it('shows the document empty state',()=>{const fixture=mount(ApplicationDetailPageComponent,{application:()=>of(applicationDetail({documents:[]}))},'app-1');expect(fixture.nativeElement.textContent).toContain('No documents attached yet');expect(fixture.nativeElement.textContent).toContain('Add document reference')});
  it('enters populated edit mode and cancel neither saves nor leaves dirty state',()=>{
    const api={application:()=>of(applicationDetail({applicationEmail:'jobs@example.com',notes:'Keep me'})),updateApplication:vi.fn()};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.beginEdit();fixture.detectChanges();expect(c.form.controls.applicationEmail.value).toBe('jobs@example.com');expect(fixture.nativeElement.querySelector('form.details-form')).not.toBeNull();c.form.patchValue({notes:'Changed'});c.cancelEdit();fixture.detectChanges();expect(api.updateApplication).not.toHaveBeenCalled();expect(c.form.controls.notes.value).toBe('Keep me');expect(c.hasUnsavedChanges()).toBe(false);expect(fixture.nativeElement.querySelector('form.details-form')).toBeNull();
  });
  it('saves the existing contract once while pending and rebinds the version',()=>{
    const pending=new Subject<ApplicationDetail>();const api={application:()=>of(applicationDetail()),updateApplication:vi.fn(()=>pending)};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.beginEdit();c.form.patchValue({channel:'EMAIL',notes:'Submitted'});c.save();c.save();fixture.detectChanges();expect(api.updateApplication).toHaveBeenCalledTimes(1);expect(api.updateApplication).toHaveBeenCalledWith('app-1',expect.objectContaining({expectedVersion:2,channel:'EMAIL',notes:'Submitted'}));expect(c.saving()).toBe(true);expect((fixture.nativeElement.querySelector('.details-form button.primary') as HTMLButtonElement).disabled).toBe(true);pending.next(applicationDetail({version:3,channel:'EMAIL'}));pending.complete();expect(c.item()?.version).toBe(3);expect(c.saving()).toBe(false);expect(c.editingDetails()).toBe(false);
  });
  it('transitions once while pending with expectedVersion and the optional note',()=>{
    vi.spyOn(window,'confirm').mockReturnValue(true);const pending=new Subject<ApplicationDetail>();const api={application:()=>of(applicationDetail()),packageReadiness:vi.fn(()=>of(packageReadiness())),transition:vi.fn(()=>pending)};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.transitionNote.setValue('Sent');c.transition('APPLIED');c.transition('APPLIED');fixture.detectChanges();expect(api.transition).toHaveBeenCalledTimes(1);expect(api.transition).toHaveBeenCalledWith('app-1',{status:'APPLIED',expectedVersion:2,note:'Sent'});expect([...fixture.nativeElement.querySelectorAll('.transition-actions button')].every((button:HTMLButtonElement)=>button.disabled)).toBe(true);pending.next(applicationDetail({version:3,status:'APPLIED'}));pending.complete();expect(c.item()?.version).toBe(3);expect(c.transitionPending()).toBeNull();expect(api.packageReadiness).toHaveBeenCalledTimes(2);
  });
  it('preserves an unsaved metadata draft when a status transition completes',()=>{
    vi.spyOn(window,'confirm').mockReturnValue(true);const api={application:()=>of(applicationDetail()),transition:vi.fn(()=>of(applicationDetail({version:3,status:'APPLIED',notes:null})))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.beginEdit();c.form.patchValue({notes:'Unsaved local draft'});c.form.markAsDirty();c.transition('APPLIED');expect(c.item()?.version).toBe(3);expect(c.form.controls.notes.value).toBe('Unsaved local draft');expect(c.form.dirty).toBe(true);
  });
  it('attaches metadata only and soft-removes after confirmation',()=>{
    vi.spyOn(window,'confirm').mockReturnValue(true);const api={application:vi.fn(()=>of(applicationDetail())),attach:vi.fn(()=>of({})),remove:vi.fn(()=>of(undefined))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.beginDocument();c.documentForm.patchValue({documentType:'CV',versionLabel:'v2',fileName:'cv-v2.pdf',storageKey:'docs/cv',contentHash:'a'.repeat(64)});c.documentForm.markAsDirty();expect(c.hasUnsavedChanges()).toBe(true);c.attach();expect(api.attach).toHaveBeenCalledWith('app-1',{documentType:'CV',versionLabel:'v2',fileName:'cv-v2.pdf',storageKey:'docs/cv',contentHash:'a'.repeat(64),metadata:null});c.remove('doc-1');expect(api.remove).toHaveBeenCalledWith('app-1','doc-1');expect(api.application).toHaveBeenCalledTimes(3);
  });
  it('prevents duplicate document mutations while a request is pending',()=>{
    const pending=new Subject<unknown>();const api={application:()=>of(applicationDetail()),attach:vi.fn(()=>pending)};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');const c=fixture.componentInstance;c.beginDocument();c.documentForm.patchValue({documentType:'CV',versionLabel:'v2'});c.attach();c.attach();fixture.detectChanges();expect(api.attach).toHaveBeenCalledTimes(1);expect(c.documentPending()).toBe(true);expect((fixture.nativeElement.querySelector('.document-form button.primary') as HTMLButtonElement).disabled).toBe(true);
  });
  it('does not remove without confirmation and handles repeated-removal conflict by refreshing',()=>{
    vi.spyOn(window,'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);const api={application:vi.fn(()=>of(applicationDetail())),remove:vi.fn(()=>throwError(()=>({status:409})))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');fixture.componentInstance.remove('doc-1');expect(api.remove).not.toHaveBeenCalled();fixture.componentInstance.remove('doc-1');expect(api.remove).toHaveBeenCalledTimes(1);expect(api.application).toHaveBeenCalledTimes(2);expect(fixture.componentInstance.error()).toBeTruthy();
  });
  it('shows stale metadata conflicts and reloads the latest application version',()=>{
    const api={application:vi.fn().mockReturnValueOnce(of(applicationDetail())).mockReturnValueOnce(of(applicationDetail({version:9}))),updateApplication:vi.fn(()=>throwError(()=>({status:409})))};const fixture=mount(ApplicationDetailPageComponent,api,'app-1');fixture.componentInstance.beginEdit();fixture.componentInstance.save();expect(api.application).toHaveBeenCalledTimes(2);expect(fixture.componentInstance.item()?.version).toBe(9);expect(fixture.componentInstance.error()).toBeTruthy();
  });
});
