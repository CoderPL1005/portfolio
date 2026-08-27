import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiHttpError } from '../../core/api/api-error.model';
import { ContactPageComponent } from './contact/contact-page.component';
import { ExperiencePageComponent } from './experience/experience-page.component';
import { HomePageComponent } from './home/home-page.component';
import { JourneyPageComponent } from './journey/journey-page.component';
import { ProjectDetailPageComponent } from './project-detail/project-detail-page.component';
import { ProjectsPageComponent } from './projects/projects-page.component';
import { PortfolioStore } from './shared/portfolio.store';
import { portfolio, project, projectDetail } from './shared/public-test-data';
import { PublicProjectDetail, PublicProjectListItem } from './shared/public.models';
import { PublicPortfolioService } from './shared/public-portfolio.service';
import { SkillsPageComponent, groupSkills } from './skills/skills-page.component';

function storeWith(data = portfolio) {
  return { status: signal<'idle'|'loading'|'loaded'|'error'>('loaded'), data: signal(data), error: signal<string|null>(null), load: vi.fn(), retry: vi.fn() };
}
describe('public aggregate pages', () => {
  beforeEach(() => TestBed.resetTestingModule());
  it('maps API content onto Home and creates project links', async () => { const store = storeWith(); await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: store }] }).compileComponents(); const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('API Person'); expect(fixture.nativeElement.textContent).toContain('API project'); expect(fixture.nativeElement.querySelector('a[href="/projects/real-project"]')).not.toBeNull(); });
  it('safely omits absent optional Home sections', async () => { const store = storeWith({ ...portfolio, featuredProjects: [], experiences: [], skills: [], journey: [], socialLinks: [] }); await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: store }] }).compileComponents(); const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).not.toContain('Featured projects'); expect(fixture.nativeElement.textContent).toContain('API Person'); });
  it('renders experience, education, training and certificates without inventing an end date', async () => { await TestBed.configureTestingModule({ imports: [ExperiencePageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith() }] }).compileComponents(); const fixture = TestBed.createComponent(ExperiencePageComponent); fixture.detectChanges(); const content = fixture.nativeElement.textContent; expect(content).toContain('API Company'); expect(content).toContain('API University'); expect(content).toContain('API Training'); expect(content).toContain('API Certificate'); expect(content).toContain('Present'); });
  it('shows an intentional empty experience state', async () => { const empty = { ...portfolio, experiences: [], educations: [], trainings: [], certificates: [] }; await TestBed.configureTestingModule({ imports: [ExperiencePageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith(empty) }] }).compileComponents(); const fixture = TestBed.createComponent(ExperiencePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No experience published'); });
  it('groups skills in first-seen backend order with no percentages', async () => { expect(groupSkills(portfolio.skills).map(g => g.category)).toEqual(['Languages', 'Frontend']); await TestBed.configureTestingModule({ imports: [SkillsPageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith() }] }).compileComponents(); const fixture = TestBed.createComponent(SkillsPageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('TypeScript'); expect(fixture.nativeElement.textContent).not.toContain('%'); });
  it('retains journey order and handles an empty journey', async () => { const store = storeWith(); await TestBed.configureTestingModule({ imports: [JourneyPageComponent], providers: [{ provide: PortfolioStore, useValue: store }] }).compileComponents(); let fixture = TestBed.createComponent(JourneyPageComponent); fixture.detectChanges(); const value = fixture.nativeElement.textContent; expect(value.indexOf('First milestone')).toBeLessThan(value.indexOf('Second milestone')); store.data.set({ ...portfolio, journey: [] }); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No journey items published'); });
});

describe('ProjectsPageComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());
  async function render(result: Observable<PublicProjectListItem[]>) { const api = { getProjects: vi.fn(() => result) }; await TestBed.configureTestingModule({ imports: [ProjectsPageComponent], providers: [provideRouter([]), { provide: PublicPortfolioService, useValue: api }] }).compileComponents(); const fixture = TestBed.createComponent(ProjectsPageComponent); fixture.detectChanges(); return { fixture, api }; }
  it('shows loading and then successful project cards with a slug link', async () => { const request = new Subject<typeof project[]>(); const { fixture } = await render(request); expect(fixture.nativeElement.textContent).toContain('Loading projects'); request.next([project]); request.complete(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('API project'); expect(fixture.nativeElement.querySelector('a[href="/projects/real-project"]')).not.toBeNull(); });
  it('shows an empty state', async () => { const { fixture } = await render(of([])); expect(fixture.nativeElement.textContent).toContain('No projects yet'); });
  it('shows an error and retry control', async () => { const { fixture } = await render(throwError(() => new ApiHttpError(500, { code: 'FAILED', message: 'Safe project error' })) as never); expect(fixture.nativeElement.textContent).toContain('Safe project error'); expect(fixture.nativeElement.querySelector('.retry-button')).not.toBeNull(); });
});

describe('ProjectDetailPageComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());
  async function render(result: Observable<PublicProjectDetail>) { const api = { getProject: vi.fn(() => result) }; await TestBed.configureTestingModule({ imports: [ProjectDetailPageComponent], providers: [provideRouter([]), { provide: PublicPortfolioService, useValue: api }, { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ slug: 'real-project' })) } }] }).compileComponents(); const fixture = TestBed.createComponent(ProjectDetailPageComponent); fixture.detectChanges(); return { fixture, api }; }
  it('requests the route slug and renders API sections/media in supplied order', async () => { const { fixture, api } = await render(of(projectDetail)); expect(api.getProject).toHaveBeenCalledWith('real-project'); const value = fixture.nativeElement.textContent; expect(value).toContain('Structured text'); expect(value.indexOf('Second')).toBeLessThan(value.indexOf('First')); expect(value.indexOf('Second media')).toBeLessThan(value.indexOf('First media')); });
  it('allows safe repository links but suppresses unsafe live links', async () => { const { fixture } = await render(of(projectDetail)); expect(fixture.nativeElement.querySelector('a[href="https://example.com/repo"]')?.getAttribute('rel')).toBe('noopener noreferrer'); expect(fixture.nativeElement.textContent).not.toContain('Live project'); });
  it('distinguishes PROJECT_NOT_FOUND', async () => { const { fixture } = await render(throwError(() => new ApiHttpError(404, { code: 'PROJECT_NOT_FOUND', message: 'missing' }))); expect(fixture.nativeElement.textContent).toContain('Project not found'); });
});

describe('ContactPageComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());
  function create(api: { submitContact: ReturnType<typeof vi.fn> }) { TestBed.configureTestingModule({ imports: [ReactiveFormsModule], providers: [{ provide: PublicPortfolioService, useValue: api }] }); return TestBed.createComponent(ContactPageComponent).componentInstance; }
  it('blocks invalid forms', () => { const api = { submitContact: vi.fn() }; const component = create(api); component.submit(); expect(api.submitContact).not.toHaveBeenCalled(); expect(component.form.controls.name.touched).toBe(true); });
  it('submits a valid form and exposes success', () => { const api = { submitContact: vi.fn(() => of({ id: '1', status: 'NEW' })) }; const component = create(api); component.form.setValue({ name: 'Name', email: 'a@example.com', subject: '', message: 'Hello' }); component.submit(); expect(api.submitContact).toHaveBeenCalledWith({ name: 'Name', email: 'a@example.com', subject: null, message: 'Hello' }); expect(component.success()).toBe(true); });
  it('maps backend validation details safely', () => { const api = { submitContact: vi.fn(() => throwError(() => new ApiHttpError(400, { code: 'VALIDATION_ERROR', message: 'invalid', details: { email: ['Email is invalid.'] } }))) }; const component = create(api); component.form.setValue({ name: 'Name', email: 'a@example.com', subject: '', message: 'Hello' }); component.submit(); expect(component.fieldError('email')).toBe('Email is invalid.'); });
  it('handles 429 and prevents duplicate submissions', () => { const pending = new Subject<{ id: string; status: string }>(); const api = { submitContact: vi.fn(() => pending) }; const component = create(api); component.form.setValue({ name: 'Name', email: 'a@example.com', subject: '', message: 'Hello' }); component.submit(); component.submit(); expect(api.submitContact).toHaveBeenCalledTimes(1); pending.error(new ApiHttpError(429, { code: 'RATE_LIMITED', message: 'internal' })); expect(component.generalError()).toContain('Please wait'); });
});
