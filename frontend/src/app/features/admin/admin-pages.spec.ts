import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiHttpError } from '../../core/api/api-error.model';
import { ContactMessageDetailPageComponent } from './contact-messages/contact-message-detail-page.component';
import { ContactMessageAdminService } from './contact-messages/contact-message-admin.service';
import { DashboardPageComponent } from './dashboard/dashboard-page.component';
import { DashboardService } from './dashboard/dashboard.service';
import { ExperienceEditPageComponent } from './experiences/experience-edit-page.component';
import { ExperienceAdminService } from './experiences/experience-admin.service';
import { ProfileAdminService } from './profile/profile-admin.service';
import { ProfilePageComponent } from './profile/profile-page.component';
import { ProjectEditPageComponent } from './projects/project-edit-page.component';
import { ProjectAdminService } from './projects/project-admin.service';
import { SiteSettingsAdminService } from './site-settings/site-settings-admin.service';
import { SiteSettingsPageComponent } from './site-settings/site-settings-page.component';
import { SocialLinkAdminService } from './social-links/social-link-admin.service';
import { SocialLinksPageComponent } from './social-links/social-links-page.component';
import { TechnologyAdminService } from './technologies/technology-admin.service';

const profile = { id:'p',fullName:'API Admin',professionalTitle:null,secondaryTitle:null,heroHeadline:null,heroSummary:null,aboutMarkdown:null,email:null,phone:null,location:null,university:null,major:null,availabilityStatus:null,profileImage:null,cvMedia:null,isPublished:true,updatedAt:'2026-01-01T00:00:00Z' };
const dashboard = {projects:2,experiences:3,skills:4,certificates:5,unreadContactMessages:1,knowledge:{indexed:0,pending:0,failed:0},conversations:0,recentUpdates:[]};

describe('DashboardPageComponent',()=>{
  beforeEach(()=>TestBed.resetTestingModule());
  async function render(result:unknown){const api={get:vi.fn(()=>result)};TestBed.overrideComponent(DashboardPageComponent,{set:{providers:[{provide:DashboardService,useValue:api}]}});await TestBed.configureTestingModule({imports:[DashboardPageComponent]}).compileComponents();const fixture=TestBed.createComponent(DashboardPageComponent);fixture.detectChanges();return fixture;}
  it('shows loading while the metric request is pending',async()=>{const fixture=await render(new Subject());expect(fixture.nativeElement.textContent).toContain('Loading dashboard');});
  it('renders only real dashboard metrics and empty updates',async()=>{const fixture=await render(of(dashboard));expect(fixture.nativeElement.textContent).toContain('Projects');expect(fixture.nativeElement.textContent).toContain('No recent updates');expect(fixture.nativeElement.textContent).not.toContain('Visitors');});
  it('shows normalized errors and retry',async()=>{const fixture=await render(throwError(()=>new ApiHttpError(500,{code:'FAILED',message:'Safe dashboard error'})));expect(fixture.nativeElement.textContent).toContain('Safe dashboard error');expect(fixture.nativeElement.textContent).toContain('Retry');});
});

describe('ProfilePageComponent',()=>{
  beforeEach(()=>TestBed.resetTestingModule());
  function create(update=vi.fn(()=>of(profile))){const api={get:vi.fn(()=>of(profile)),update};TestBed.overrideComponent(ProfilePageComponent,{set:{providers:[{provide:ProfileAdminService,useValue:api}]}});TestBed.configureTestingModule({imports:[ProfilePageComponent]});return {component:TestBed.createComponent(ProfilePageComponent).componentInstance,api};}
  it('loads the singleton profile into the form',()=>{const {component}=create();expect(component.form.controls.fullName.value).toBe('API Admin');expect(component.hasUnsavedChanges()).toBe(false);});
  it('blocks invalid save and tracks dirty state',()=>{const {component,api}=create();component.form.controls.fullName.setValue('');component.form.controls.fullName.markAsDirty();component.save();expect(api.update).not.toHaveBeenCalled();expect(component.hasUnsavedChanges()).toBe(true);});
  it('saves exact typed fields and clears dirty state',()=>{const {component,api}=create();component.form.controls.fullName.setValue('Updated');component.save();expect(api.update).toHaveBeenCalledWith(expect.objectContaining({fullName:'Updated',profileImageId:null,cvMediaId:null}));expect(component.hasUnsavedChanges()).toBe(false);});
  it('surfaces backend validation safely',()=>{const {component}=create(vi.fn(()=>throwError(()=>new ApiHttpError(400,{code:'VALIDATION_ERROR',message:'Review fields',details:{fullName:['Required.']}}))));component.form.controls.fullName.setValue('Valid');component.save();expect(component.error()).toBe('Review fields');});
});

describe('ExperienceEditPageComponent',()=>{
  beforeEach(()=>TestBed.resetTestingModule());
  function create(){const api={create:vi.fn(()=>of({id:'e1'})),update:vi.fn(),get:vi.fn()};const tech={list:vi.fn(()=>of([{id:'t1',name:'Angular',category:'Frontend',iconKey:null,websiteUrl:null,displayOrder:0,isActive:true,updatedAt:''}]))};const router={navigate:vi.fn()};TestBed.overrideComponent(ExperienceEditPageComponent,{set:{providers:[{provide:ExperienceAdminService,useValue:api}]}});TestBed.configureTestingModule({imports:[ExperienceEditPageComponent],providers:[{provide:TechnologyAdminService,useValue:tech},{provide:Router,useValue:router},{provide:ActivatedRoute,useValue:{snapshot:{paramMap:convertToParamMap({})}}}]});return{component:TestBed.createComponent(ExperienceEditPageComponent).componentInstance,api,router};}
  it('validates required experience fields',()=>{const {component,api}=create();component.save();expect(api.create).not.toHaveBeenCalled();});
  it('creates with unique technology IDs and current-position semantics',()=>{const {component,api}=create();component.form.patchValue({companyName:'Company',roleTitle:'Role',startDate:'2025-01-01',isCurrent:true,endDate:'2025-02-01'});component.toggleTechnology('t1');component.save();expect(api.create).toHaveBeenCalledWith(expect.objectContaining({endDate:null,technologyIds:['t1']}));});
});

describe('ProjectEditPageComponent',()=>{
  beforeEach(()=>TestBed.resetTestingModule());
  function create(){const projectApi={create:vi.fn(()=>of({id:'new-id'})),update:vi.fn(),replaceTechnologies:vi.fn(()=>of([])),sections:vi.fn(()=>of([])),media:vi.fn(()=>of([]))};const tech={list:vi.fn(()=>of([]))};const router={navigate:vi.fn()};TestBed.overrideComponent(ProjectEditPageComponent,{set:{providers:[{provide:ProjectAdminService,useValue:projectApi}]}});TestBed.configureTestingModule({imports:[ProjectEditPageComponent],providers:[{provide:TechnologyAdminService,useValue:tech},{provide:Router,useValue:router},{provide:ActivatedRoute,useValue:{snapshot:{paramMap:convertToParamMap({})}}}]});const fixture=TestBed.createComponent(ProjectEditPageComponent);fixture.detectChanges();return{fixture,component:fixture.componentInstance,projectApi,router};}
  it('renders the frozen five-tab editor structure',()=>{const {fixture}=create();const value=fixture.nativeElement.textContent;for(const tab of ['Basic Info','Technologies','Sections','Media','SEO Settings'])expect(value).toContain(tab);});
  it('blocks an invalid project create',()=>{const {component,projectApi}=create();component.saveBasic();expect(projectApi.create).not.toHaveBeenCalled();});
  it('creates a valid project with approved status and technology IDs',()=>{const {component,projectApi,router}=create();component.form.patchValue({title:'Project',slug:'project',status:'ACTIVE'});component.selectedTech.set(['t1']);component.saveBasic();expect(projectApi.create).toHaveBeenCalledWith(expect.objectContaining({status:'ACTIVE',technologyIds:['t1']}));expect(router.navigate).toHaveBeenCalledWith(['/admin/projects','new-id']);});
  it('shows the explicit metadata-only Media placeholder for a new project',()=>{const {fixture,component}=create();component.tab.set('Media');fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Save Basic Info before managing attached media');expect(fixture.nativeElement.textContent).not.toContain('Upload');});
});

describe('typed settings, social URL safety, and contact status',()=>{
  beforeEach(()=>TestBed.resetTestingModule());
  it('loads and saves only typed site settings while tracking dirtiness',()=>{const settings={siteName:'Site',footerText:null,showAvailability:true,enableContactForm:true,showDownloadCv:false,showJourney:true,showAiAgent:false,defaultSeoTitle:null,defaultSeoDescription:null};const api={get:vi.fn(()=>of(settings)),update:vi.fn((_value:unknown)=>of(settings))};TestBed.overrideComponent(SiteSettingsPageComponent,{set:{providers:[{provide:SiteSettingsAdminService,useValue:api}]}});TestBed.configureTestingModule({imports:[SiteSettingsPageComponent]});const component=TestBed.createComponent(SiteSettingsPageComponent).componentInstance;component.form.controls.siteName.setValue('Updated');component.save();expect(api.update).toHaveBeenCalledWith(expect.objectContaining({siteName:'Updated'}));expect(Object.keys(api.update.mock.calls[0][0] as object)).toHaveLength(9);expect(component.hasUnsavedChanges()).toBe(false);});
  it('rejects unsafe social URLs before API submission',()=>{const api={list:vi.fn(()=>of([])),create:vi.fn(),update:vi.fn()};TestBed.overrideComponent(SocialLinksPageComponent,{set:{providers:[{provide:SocialLinkAdminService,useValue:api}]}});TestBed.configureTestingModule({imports:[SocialLinksPageComponent]});const component=TestBed.createComponent(SocialLinksPageComponent).componentInstance;component.form.patchValue({platform:'Bad',url:'javascript:alert(1)'});component.save();expect(api.create).not.toHaveBeenCalled();});
  it('loads message detail and PATCHes status without a delete operation',()=>{const message={id:'m1',name:'Sender',email:'sender@example.com',subject:null,message:'Private body',status:'NEW',receivedAt:'',readAt:null,repliedAt:null};const api={get:vi.fn(()=>of(message)),updateStatus:vi.fn(()=>of({...message,status:'READ'}))};TestBed.overrideComponent(ContactMessageDetailPageComponent,{set:{providers:[{provide:ContactMessageAdminService,useValue:api}]}});TestBed.configureTestingModule({imports:[ContactMessageDetailPageComponent],providers:[provideRouter([]),{provide:ActivatedRoute,useValue:{snapshot:{paramMap:convertToParamMap({id:'m1'})}}}]});const component=TestBed.createComponent(ContactMessageDetailPageComponent).componentInstance;component.setStatus('READ');expect(api.updateStatus).toHaveBeenCalledWith('m1','READ');expect('delete' in api).toBe(false);});
});
