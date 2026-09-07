import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiHttpError } from '../../core/api/api-error.model';
import { ContactPageComponent } from './contact/contact-page.component';
import { ExperiencePageComponent } from './experience/experience-page.component';
import { HomePageComponent } from './home/home-page.component';
import { buildTimelineLayout, calculatePopoverPosition, JourneyPageComponent } from './journey/journey-page.component';
import { ProjectDetailPageComponent } from './project-detail/project-detail-page.component';
import { ProjectsPageComponent } from './projects/projects-page.component';
import { PortfolioStore } from './shared/portfolio.store';
import { duplicatePlatformSocialLinks, portfolio, project, projectDetail } from './shared/public-test-data';
import { PublicJourneyItem, PublicProjectDetail, PublicProjectListItem } from './shared/public.models';
import { PublicPortfolioService } from './shared/public-portfolio.service';
import { SkillsPageComponent, groupSkills } from './skills/skills-page.component';

function storeWith(data = portfolio) {
  return { status: signal<'idle'|'loading'|'loaded'|'error'>('loaded'), data: signal(data), error: signal<string|null>(null), load: vi.fn(), retry: vi.fn() };
}

const journeyTimeline: PublicJourneyItem[] = [
  { id:'education-id',title:'Engineer Degree',subtitle:'Hanoi University of Civil Engineering',description:'Software Engineering education with backend and database focus.',occurredAt:'2022-09-23',iconKey:null,sourceType:'EDUCATION',sourceId:'education-source',startAt:'2022-09-23',endAt:null,isOngoing:true,timelineKind:'PERIOD' },
  { id:'training-id',title:'AI Training',subtitle:'VinUni',description:'Hands-on AI and cloud training.',occurredAt:'2026-07-01',iconKey:null,sourceType:'TRAINING',sourceId:'training-source',startAt:'2026-07-01',endAt:'2026-09-01',isOngoing:false,timelineKind:'PERIOD' },
  { id:'project-id',title:'PolicyMeta AI (P-234)',subtitle:'Backend Developer',description:'Secure regulatory-document backend and RAG integration.',occurredAt:'2026-07-27',iconKey:null,sourceType:'PROJECT',sourceId:'project-source',startAt:'2026-07-27',endAt:'2026-08-20',isOngoing:false,timelineKind:'PERIOD' },
  { id:'manual-id',title:'AI & Cloud',subtitle:'Milestone',description:'Expanded into AI and cloud engineering.',occurredAt:'2026-08-01',iconKey:'cloud',sourceType:'MANUAL',sourceId:'manual-id',startAt:'2026-08-01',endAt:null,isOngoing:false,timelineKind:'POINT' },
];

async function renderJourney(journey = journeyTimeline) {
  const store=storeWith({...portfolio,journey});
  await TestBed.configureTestingModule({imports:[JourneyPageComponent],providers:[{provide:PortfolioStore,useValue:store}]}).compileComponents();
  const fixture=TestBed.createComponent(JourneyPageComponent);fixture.detectChanges();
  return fixture;
}
describe('public aggregate pages', () => {
  beforeEach(() => TestBed.resetTestingModule());
  it('maps API content onto Home and creates project links', async () => { const store = storeWith(); await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: store }] }).compileComponents(); const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('API Person'); expect(fixture.nativeElement.textContent).toContain('API project'); expect(fixture.nativeElement.querySelector('a[href="/projects/real-project"]')).not.toBeNull(); });
  it('uses technologies for stack views while keeping capabilities based on skills', async () => {
    const data = {
      ...portfolio,
      technologies: [
        { id: 'tech-1', name: 'ASP.NET Core', category: 'Backend', iconKey: null },
        { id: 'tech-2', name: 'Angular', category: 'Frontend', iconKey: null },
        { id: 'tech-3', name: 'SQL Server', category: 'Database', iconKey: null },
      ],
      skills: [
        { id: 'skill-1', name: 'Backend Development', category: 'Backend', experienceLevel: 'USED' as const, description: null, technologyId: null },
        { id: 'skill-2', name: 'REST API Design & Development', category: 'Backend', experienceLevel: 'USED' as const, description: null, technologyId: null },
        { id: 'skill-3', name: 'Frontend Development', category: 'Frontend', experienceLevel: 'USED' as const, description: null, technologyId: null },
      ],
    };
    await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges();
    const primaryStack = fixture.nativeElement.querySelector('.fact-strip')?.textContent;
    const coreStack = fixture.nativeElement.querySelector('.core-stack')?.textContent;
    const identity = fixture.nativeElement.querySelector('.terminal-body')?.textContent;
    const capabilities = fixture.nativeElement.querySelector('.skill-preview')?.textContent;
    expect(primaryStack).toContain('ASP.NET Core + Angular + SQL Server');
    expect(coreStack).toContain('ASP.NET Core'); expect(coreStack).toContain('Angular'); expect(coreStack).toContain('SQL Server');
    expect(identity).toContain('ASP.NET Core'); expect(identity).not.toContain('Backend Development');
    expect(primaryStack).not.toContain('Backend Development'); expect(primaryStack).not.toContain('REST API Design & Development');
    expect(capabilities).toContain('Backend Development'); expect(capabilities).toContain('REST API Design & Development'); expect(capabilities).toContain('Frontend Development');
  });
  it('prefers published Education for Home quick facts and preserves other fact mappings', async () => {
    const data = {
      ...portfolio,
      profile: { ...portfolio.profile, professionalTitle: 'Platform Engineer', secondaryTitle: 'Distributed systems', availabilityStatus: 'Available', location: 'Hanoi', major: 'Legacy Major', university: 'Legacy University' },
      educations: [{ id: 'education-1', institution: 'Hanoi University of Civil Engineering', degree: "Bachelor's degree", fieldOfStudy: 'Software Engineering', startDate: null, endDate: null, description: null, location: 'Hanoi, Vietnam' }],
    };
    await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges();
    const facts = fixture.nativeElement.querySelector('.fact-strip')?.textContent;
    const quickFacts = fixture.nativeElement.querySelector('.about aside')?.textContent;
    expect(quickFacts).toContain('Hanoi University of Civil Engineering');
    expect(quickFacts).not.toContain("Bachelor's degree in Software Engineering");
    expect(quickFacts).not.toContain('Legacy Major');
    expect(facts).toContain('Available'); expect(facts).toContain('API project'); expect(facts).toContain('Distributed systems');
    expect(quickFacts).toContain('Platform Engineer'); expect(quickFacts).toContain('Hanoi');
  });
  it('uses degree and field of study when the published Education institution is missing', async () => {
    const data = { ...portfolio, educations: [{ ...portfolio.educations[0], institution: '', degree: "Bachelor's degree", fieldOfStudy: 'Software Engineering' }] };
    await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.about aside')?.textContent).toContain("Bachelor's degree in Software Engineering");
  });
  it('falls back to legacy profile education only when no published Education is present', async () => {
    const data = { ...portfolio, educations: [], profile: { ...portfolio.profile, major: 'Legacy Major', university: 'Legacy University' } };
    await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges();
    const quickFacts = fixture.nativeElement.querySelector('.about aside')?.textContent;
    expect(quickFacts).toContain('Legacy University');
    expect(quickFacts).not.toContain('Legacy Major');
  });
  it('renders every duplicate-platform link in Find me online with stable IDs', async () => {
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const store = storeWith({ ...portfolio, socialLinks: duplicatePlatformSocialLinks });
    await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: store }] }).compileComponents();
    const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges();
    const links = [...fixture.nativeElement.querySelectorAll('.socials a')] as HTMLAnchorElement[];
    expect(links.map(link => link.textContent?.trim())).toEqual(['CoderPL1005', 'PhucND3009']);
    expect(links.map(link => link.href)).toEqual(['https://github.com/CoderPL1005', 'https://github.com/PhucND3009']);
    expect(warning.mock.calls.flat().join(' ')).not.toContain('NG0955');
    warning.mockRestore();
  });
  it('safely omits absent optional Home sections', async () => { const store = storeWith({ ...portfolio, featuredProjects: [], experiences: [], skills: [], journey: [], socialLinks: [] }); await TestBed.configureTestingModule({ imports: [HomePageComponent], providers: [provideRouter([]), { provide: PortfolioStore, useValue: store }] }).compileComponents(); const fixture = TestBed.createComponent(HomePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).not.toContain('Featured projects'); expect(fixture.nativeElement.textContent).toContain('API Person'); });
  it('renders experience, education, training and certificates without inventing an end date', async () => { await TestBed.configureTestingModule({ imports: [ExperiencePageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith() }] }).compileComponents(); const fixture = TestBed.createComponent(ExperiencePageComponent); fixture.detectChanges(); const content = fixture.nativeElement.textContent; expect(content).toContain('API Company'); expect(content).toContain('API University'); expect(content).toContain('API Training'); expect(content).toContain('API Certificate'); expect(content).toContain('Present'); });
  it('shows an intentional empty experience state', async () => { const empty = { ...portfolio, experiences: [], educations: [], trainings: [], certificates: [] }; await TestBed.configureTestingModule({ imports: [ExperiencePageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith(empty) }] }).compileComponents(); const fixture = TestBed.createComponent(ExperiencePageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No experience published'); });
  it('groups skills in first-seen backend order with no percentages', async () => { expect(groupSkills(portfolio.skills).map(g => g.category)).toEqual(['Languages', 'Frontend']); await TestBed.configureTestingModule({ imports: [SkillsPageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith() }] }).compileComponents(); const fixture = TestBed.createComponent(SkillsPageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('TypeScript'); expect(fixture.nativeElement.textContent).not.toContain('%'); });
  it('retains journey order in the event index and handles an empty journey', async () => { const store = storeWith(); await TestBed.configureTestingModule({ imports: [JourneyPageComponent], providers: [{ provide: PortfolioStore, useValue: store }] }).compileComponents(); let fixture = TestBed.createComponent(JourneyPageComponent); fixture.detectChanges(); const rows=[...fixture.nativeElement.querySelectorAll('[data-track-index="MILESTONES"] .index-title')].map((title:HTMLElement)=>title.textContent?.trim()); expect(rows).toEqual(['First milestone','Second milestone']); store.data.set({ ...portfolio, journey: [] }); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No journey items published'); });
  it('keeps the compact header, sticky shared axis, track labels, overlap lanes, and mobile fallback', async () => {
    const fixture=await renderJourney();
    expect(fixture.nativeElement.querySelector('.journey-heading h1')?.textContent).toContain('Journey');
    expect(fixture.nativeElement.querySelector('.journey-heading')?.textContent).toContain('Education, work, training, projects, and milestones');
    expect([...fixture.nativeElement.querySelectorAll('.track-label')].map((label:HTMLElement)=>label.textContent?.trim())).toEqual(['Education','Experience','Training','Projects','Milestones']);
    const viewport=fixture.nativeElement.querySelector('.timeline-scroll') as HTMLElement;
    const canvas=fixture.nativeElement.querySelector('.timeline-canvas') as HTMLElement;
    expect(viewport.dataset['horizontalScroll']).toBe('true');
    expect(getComputedStyle(viewport).overflowX).toBe('auto');
    expect(viewport.getAttribute('style') ?? '').not.toContain('overflow-y');
    expect(viewport.style.height).toBe('');
    expect(viewport.style.maxHeight).toBe('');
    expect(parseFloat(canvas.style.minWidth)).toBeGreaterThan(860);
    expect(canvas.style.height).toBe('');
    expect(canvas.style.maxHeight).toBe('');
    const axis=fixture.nativeElement.querySelector('.time-axis') as HTMLElement;
    expect(axis).not.toBeNull();
    expect(axis.dataset['stickyContext']).toBe('true');
    expect(getComputedStyle(axis).position).toBe('sticky');
    expect(axis.closest('.timeline-canvas')).toBe(canvas);
    const educationRail=fixture.nativeElement.querySelector('[data-track="EDUCATION"]') as HTMLElement;
    const educationCard=educationRail.querySelector('[data-kind="PERIOD"]') as HTMLElement;
    expect(educationRail.dataset['firstTrack']).toBe('true');
    expect(parseFloat(educationRail.style.height)).toBeGreaterThan(0);
    expect(educationRail.previousElementSibling?.getAttribute('data-first-track')).toBe('true');
    const clearances=[...fixture.nativeElement.querySelectorAll('.first-track-clearance')] as HTMLElement[];
    const headerHeight=parseFloat(canvas.dataset['headerHeight']!);
    const bodyOffset=parseFloat(canvas.dataset['bodyOffset']!);
    expect(clearances).toHaveLength(2);expect(clearances.every(clearance=>parseFloat(clearance.style.height)===bodyOffset)).toBe(true);
    expect(bodyOffset).toBeGreaterThanOrEqual(headerHeight);
    expect(clearances[1].nextElementSibling?.getAttribute('data-first-track')).toBe('true');
    expect(educationCard.textContent).toContain('Engineer Degree');expect(educationCard.textContent).toContain('Sep 2022 → Present');
    expect(parseFloat(educationCard.style.top)).toBeGreaterThanOrEqual(0);
    expect(parseFloat(educationCard.dataset['startPercent']!)).toBe(0);expect(parseFloat(educationCard.style.left)).toBe(0);
    const educationIndexRow=fixture.nativeElement.querySelector('[data-track-index="EDUCATION"] .event-index-row') as HTMLElement;
    expect(educationIndexRow.textContent).toContain('Engineer Degree');expect(educationIndexRow.dataset['entryId']).toBe(educationCard.dataset['entryId']);
    expect(fixture.nativeElement.querySelector('[data-track="MILESTONES"] [data-kind="POINT"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-track="TRAINING"] .timeline-item')?.getAttribute('data-lane')).toBe('0');
    expect(fixture.nativeElement.querySelector('[data-track="PROJECTS"] .timeline-item')?.getAttribute('data-lane')).toBe('0');
    const rails=[...fixture.nativeElement.querySelectorAll('.track-rail')] as HTMLElement[];
    const expectedCanvasHeight=headerHeight+bodyOffset
      +rails.reduce((height,rail)=>height+parseFloat(rail.style.height),0)
      +parseFloat(canvas.dataset['bottomPadding']!);
    expect(rails).toHaveLength(5);
    expect(parseFloat(canvas.style.minHeight)).toBe(expectedCanvasHeight);
    expect(parseFloat(canvas.dataset['canvasHeight']!)).toBe(expectedCanvasHeight);
    expect(fixture.nativeElement.querySelectorAll('.timeline-mobile li')).toHaveLength(4);
    expect(fixture.nativeElement.querySelector('.timeline-mobile')?.textContent).toContain('Engineer Degree');
  });
  it('uses start and end dates as the visible geometry of each single period card', async () => {
    const fixture=await renderJourney();
    const education=fixture.nativeElement.querySelector('[data-track="EDUCATION"] .timeline-item') as HTMLElement;
    const project=fixture.nativeElement.querySelector('[data-track="PROJECTS"] .timeline-item') as HTMLElement;
    const plotWidth=parseFloat((fixture.nativeElement.querySelector('[data-track="PROJECTS"]') as HTMLElement).dataset['plotWidth']!);
    expect(parseFloat(project.style.left)).toBeCloseTo(parseFloat(project.dataset['startPercent']!)*plotWidth/100,8);
    expect(parseFloat(project.style.width)).toBeCloseTo((parseFloat(project.dataset['endPercent']!)-parseFloat(project.dataset['startPercent']!))*plotWidth/100,8);
    expect(parseFloat(project.dataset['cardRight']!)).toBeCloseTo(parseFloat(project.style.left)+parseFloat(project.style.width),8);
    expect(parseFloat(project.dataset['cardRight']!)).toBeCloseTo(parseFloat(project.dataset['endPercent']!)*plotWidth/100,8);
    expect(parseFloat(education.style.left)).toBeCloseTo(parseFloat(education.dataset['startPercent']!)*plotWidth/100,8);
    expect(parseFloat(education.style.width)).toBeCloseTo((parseFloat(education.dataset['endPercent']!)-parseFloat(education.dataset['startPercent']!))*plotWidth/100,8);
    expect(parseFloat(education.dataset['endPercent']!)).toBe(100);
    expect(parseFloat(education.dataset['cardRight']!)).toBe(plotWidth);
    expect(parseFloat(project.style.width)).not.toBe(168);
    expect(parseFloat(education.style.width)).not.toBe(168);
    expect(getComputedStyle(project).minWidth).toBe('');
    expect(project.querySelector('.bar-content')).toBeNull();
    expect(project.querySelector('.external-label')).toBeNull();
    expect(project.querySelector('.period-card-content')?.textContent).toContain('PolicyMeta AI (P-234)');
    expect(project.querySelector('.period-card-content')?.textContent).toContain('Jul 2026');
    expect(project.querySelector('.timeline-title-card')).toBeNull();
    expect(project.querySelector('.period-connector')).toBeNull();
    expect(project.querySelector('.duration-geometry')).toBeNull();
    expect(project.querySelector('.internal-duration')).toBeNull();
    expect(project.querySelector('.temporal-start-anchor')).toBeNull();
    expect(project.textContent).not.toContain('PRJ1');
    expect(project.dataset['identifier']).toBe('PRJ1');
    expect(project.getAttribute('title')).toContain('PolicyMeta AI (P-234)');
    expect((fixture.nativeElement.querySelector('[data-track-index="PROJECTS"]') as HTMLElement).textContent).toContain('PolicyMeta AI (P-234)');
    expect(education.querySelector('.period-card-content')?.textContent).toContain('Engineer Degree');
    expect(education.querySelector('.period-card-content')?.textContent).not.toContain('EDU1');
    expect(education.querySelector('.period-card-content')?.textContent).toContain('Sep 2022');
    expect(education.querySelector('.period-card-content')?.textContent).toContain('Present');
    expect(education.querySelector('.internal-duration')).toBeNull();
    const point=fixture.nativeElement.querySelector('[data-track="MILESTONES"] .timeline-item') as HTMLElement;
    expect(point.querySelector('.timeline-title-card')?.textContent).toContain('AI & Cloud');
    expect(point.querySelector('.timeline-title-card')?.textContent).toContain('Aug 2026');
    expect(point.textContent).not.toContain('MIL1');
    expect(point.dataset['identifier']).toBe('MIL1');
    expect((fixture.nativeElement.querySelector('[data-track-index="MILESTONES"]') as HTMLElement).textContent).toContain('AI & Cloud');
  });
  it('maps each acceptance period from its start date through its end or presentation Present date', () => {
    const periods: PublicJourneyItem[] = [
      journeyTimeline[0],
      { id:'hotel-id',title:'Hotel Management System',subtitle:'Project',description:null,occurredAt:'2025-04-01',iconKey:null,sourceType:'PROJECT',sourceId:'hotel-source',startAt:'2025-04-01',endAt:'2025-06-30',isOngoing:false,timelineKind:'PERIOD' },
      { id:'school-id',title:'SchoolSaaS',subtitle:'Project',description:null,occurredAt:'2026-05-01',iconKey:null,sourceType:'PROJECT',sourceId:'school-source',startAt:'2026-05-01',endAt:null,isOngoing:true,timelineKind:'PERIOD' },
      { id:'policy-id',title:'PolicyMeta AI',subtitle:'Project',description:null,occurredAt:'2026-07-27',iconKey:null,sourceType:'PROJECT',sourceId:'policy-source',startAt:'2026-07-27',endAt:'2026-09-01',isOngoing:false,timelineKind:'PERIOD' },
      { id:'portfolio-id',title:'Developer Portfolio & Personal AI Agent',subtitle:'Project',description:null,occurredAt:'2026-08-21',iconKey:null,sourceType:'PROJECT',sourceId:'portfolio-source',startAt:'2026-08-21',endAt:null,isOngoing:true,timelineKind:'PERIOD' },
    ];
    const present=new Date('2026-10-01T00:00:00Z');
    const layout=buildTimelineLayout(periods,present);
    const entries=layout.tracks.flatMap(track=>track.items);
    const entry=(id:string)=>entries.find(item=>item.id===id)!;
    const minimum=Date.parse('2022-09-23T00:00:00Z');
    const maximum=Date.parse('2026-10-01T00:00:00Z');
    const percent=(date:string)=>(Date.parse(`${date}T00:00:00Z`)-minimum)/(maximum-minimum)*100;
    const assertGeometry=(id:string,start:string,end:string)=>{
      const item=entry(id);
      expect(item.startPercent).toBeCloseTo(percent(start),8);
      expect(item.endPercent).toBeCloseTo(percent(end),8);
      expect(item.cardLeft).toBeCloseTo(item.startPercent*layout.plotWidth/100,8);
      expect(item.cardRight).toBeCloseTo(item.endPercent!*layout.plotWidth/100,8);
      expect(item.cardWidth).toBeCloseTo(item.cardRight-item.cardLeft,8);
    };
    assertGeometry('education-id','2022-09-23','2026-10-01');
    assertGeometry('hotel-id','2025-04-01','2025-06-30');
    assertGeometry('school-id','2026-05-01','2026-10-01');
    assertGeometry('policy-id','2026-07-27','2026-09-01');
    assertGeometry('portfolio-id','2026-08-21','2026-10-01');
    expect(entry('policy-id').cardWidth).toBeLessThan(entry('school-id').cardWidth);
    expect(entry('policy-id').cardRight).toBeLessThan(entry('school-id').cardRight);
    expect(entry('school-id').cardRight).toBeCloseTo(entry('portfolio-id').cardRight,8);
    expect(entry('school-id').lane).not.toBe(entry('policy-id').lane);
    expect(entry('policy-id').lane).not.toBe(entry('portfolio-id').lane);
  });
  it('keeps non-ongoing periods with unknown ends minimal instead of stretching them to Present', () => {
    const unknown: PublicJourneyItem = { id:'unknown-id',title:'Software Developer Intern',subtitle:'Company',description:null,occurredAt:'2025-08-01',iconKey:null,sourceType:'EXPERIENCE',sourceId:'unknown-source',startAt:'2025-08-01',endAt:null,isOngoing:false,timelineKind:'PERIOD' };
    const layout=buildTimelineLayout([journeyTimeline[0],unknown,journeyTimeline[3]],new Date('2026-10-01T00:00:00Z'));
    const item=layout.tracks.flatMap(track=>track.items).find(entry=>entry.id==='unknown-id')!;
    expect(item.endPercent).toBeNull();
    expect(item.cardWidth).toBe(13);
    expect(item.cardRight).toBe(item.cardLeft+13);
    expect(item.cardRight).toBeLessThan(layout.plotWidth);
  });
  it('uses real project titles with collision-safe anchored cards and a readable project index', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-01T00:00:00Z'));
    try {
    const sourceProjects: PublicJourneyItem[] = [
      { id:'hotel-id',title:'Hotel Management System',subtitle:'Project',description:'Hotel project.',occurredAt:'2025-04-01',iconKey:null,sourceType:'PROJECT',sourceId:'hotel-source',startAt:'2025-04-01',endAt:'2025-06-30',isOngoing:false,timelineKind:'PERIOD' },
      { id:'school-id',title:'SchoolSaaS',subtitle:'Project',description:'School SaaS.',occurredAt:'2026-05-01',iconKey:null,sourceType:'PROJECT',sourceId:'school-source',startAt:'2026-05-01',endAt:null,isOngoing:true,timelineKind:'PERIOD' },
      journeyTimeline.find(item=>item.id==='project-id')!,
      { id:'portfolio-id',title:'Developer Portfolio & Personal AI Agent',subtitle:'Project',description:'Portfolio project.',occurredAt:'2026-08-21',iconKey:null,sourceType:'PROJECT',sourceId:'portfolio-source',startAt:'2026-08-21',endAt:null,isOngoing:true,timelineKind:'PERIOD' },
    ];
    const fixture=await renderJourney([...journeyTimeline.filter(item=>item.sourceType!=='PROJECT'),...sourceProjects]);
    const projectMarks=[...fixture.nativeElement.querySelectorAll('[data-track="PROJECTS"] .timeline-item')] as HTMLElement[];
    expect(projectMarks.map(project=>project.dataset['identifier'])).toEqual(['PRJ1','PRJ2','PRJ3','PRJ4']);
    expect(projectMarks.every(project=>project.querySelector('.external-label')===null)).toBe(true);
    expect(projectMarks.map(project=>project.textContent?.trim())).toEqual([
      'Hotel Management SystemApr 2025 → Jun 2025','SchoolSaaSMay 2026 → Present',
      'PolicyMeta AI (P-234)Jul 2026 → Aug 2026','Developer Portfolio & Personal AI AgentAug 2026 → Present',
    ]);
    expect(projectMarks.map(project=>project.textContent).join(' ')).not.toMatch(/PRJ[1-4]/);
    expect(projectMarks.every(project=>project.querySelectorAll('.period-card-content').length===1)).toBe(true);
    expect(projectMarks.every(project=>project.querySelector('.internal-duration')===null)).toBe(true);
    expect(projectMarks.every(project=>project.querySelector('.temporal-start-anchor')===null)).toBe(true);
    expect(projectMarks.every(project=>project.querySelector('.duration-geometry')===null)).toBe(true);
    const [hotel,school,policyMeta,portfolioProject]=projectMarks;
    expect(parseFloat(hotel.dataset['startPercent']!)).toBeCloseTo(64.0028,3);
    const starts=[school,policyMeta,portfolioProject].map(mark=>parseFloat(mark.dataset['startPercent']!));
    expect(starts[0]).toBeCloseTo(91.4524,3);expect(starts[1]).toBeCloseTo(97.4983,3);expect(starts[2]).toBeCloseTo(99.2356,3);
    expect(starts[0]).toBeLessThan(starts[1]);expect(starts[1]).toBeLessThan(starts[2]);
    const plotWidth=parseFloat((fixture.nativeElement.querySelector('[data-track="PROJECTS"]') as HTMLElement).dataset['plotWidth']!);
    const cardLefts=[school,policyMeta,portfolioProject].map(mark=>parseFloat(mark.style.left));
    expect(cardLefts[0]).toBeCloseTo(starts[0]*plotWidth/100,8);expect(cardLefts[1]).toBeCloseTo(starts[1]*plotWidth/100,8);expect(cardLefts[2]).toBeCloseTo(starts[2]*plotWidth/100,8);
    expect(cardLefts[0]).toBeLessThan(cardLefts[1]);expect(cardLefts[1]).toBeLessThan(cardLefts[2]);
    expect(school.dataset['lane']).not.toBe(policyMeta.dataset['lane']);
    expect(parseFloat(policyMeta.style.left)).toBeCloseTo(parseFloat(policyMeta.dataset['startPercent']!)*plotWidth/100,8);
    expect(parseFloat(portfolioProject.style.left)).toBeCloseTo(parseFloat(portfolioProject.dataset['startPercent']!)*plotWidth/100,8);
    for(const mark of projectMarks) expect(parseFloat(mark.style.width)).toBeCloseTo(parseFloat(mark.dataset['cardRight']!)-parseFloat(mark.style.left),8);
    const canvas=fixture.nativeElement.querySelector('.timeline-canvas') as HTMLElement;
    expect(parseFloat(canvas.style.minWidth)).toBeGreaterThan(plotWidth+168);
    expect(new Set(projectMarks.map(mark=>mark.dataset['lane'])).size).toBeGreaterThan(1);
    const laneGroups=new Map<string|undefined,HTMLElement[]>();
    for(const mark of projectMarks) laneGroups.set(mark.dataset['lane'],[...(laneGroups.get(mark.dataset['lane'])??[]),mark]);
    for(const lane of laneGroups.values()) {
      const footprints=lane.map(mark=>({start:parseFloat(mark.dataset['visualStart']!),end:parseFloat(mark.dataset['visualEnd']!)})).sort((a,b)=>a.start-b.start);
      for(let index=1;index<footprints.length;index++) expect(footprints[index-1].end).toBeLessThanOrEqual(footprints[index].start);
    }
    expect(parseFloat((fixture.nativeElement.querySelector('[data-track="PROJECTS"]') as HTMLElement).style.height)).toBeGreaterThanOrEqual(152);
    const canvasHeight=parseFloat(canvas.style.minHeight);
    const rails=[...fixture.nativeElement.querySelectorAll('.track-rail')] as HTMLElement[];
    const singleLaneCanvasHeight=parseFloat(canvas.dataset['headerHeight']!)+parseFloat(canvas.dataset['bodyOffset']!)+(rails.length*76)+parseFloat(canvas.dataset['bottomPadding']!);
    expect(canvasHeight).toBeGreaterThan(singleLaneCanvasHeight);
    const indexRows=[...fixture.nativeElement.querySelectorAll('[data-track-index="PROJECTS"] .event-index-row')] as HTMLButtonElement[];
    expect(indexRows.map(row=>row.dataset['identifier'])).toEqual(['PRJ1','PRJ2','PRJ3','PRJ4']);
    const indexText=indexRows.map(row=>row.textContent).join(' ');
    expect(indexText).toContain('Hotel Management System');expect(indexText).toContain('SchoolSaaS');
    expect(indexText).toContain('PolicyMeta AI');expect(indexText).toContain('Developer Portfolio & Personal AI Agent');
    expect(indexText).not.toMatch(/PRJ[1-4]/);
    } finally {
      vi.useRealTimers();
    }
  });
  it('keeps manual and certificate events as indexed point markers', async () => {
    const certificate: PublicJourneyItem = {
      id:'certificate-id',title:'Cloud Certificate',subtitle:'Certification',description:'Cloud milestone.',
      occurredAt:'2026-08-03',iconKey:null,sourceType:'CERTIFICATE',sourceId:'certificate-source',startAt:'2026-08-03',endAt:null,isOngoing:false,timelineKind:'POINT',
    };
    const fixture=await renderJourney([...journeyTimeline,certificate]);
    const points=[...fixture.nativeElement.querySelectorAll('[data-track="MILESTONES"] [data-kind="POINT"]')] as HTMLElement[];
    expect(points).toHaveLength(2);
    expect(points.every(point=>point.classList.contains('point'))).toBe(true);
    expect(points[0].dataset['lane']).not.toBe(points[1].dataset['lane']);
    expect(points.map(point=>point.dataset['identifier'])).toEqual(['MIL1','MIL2']);
    expect(points[0].textContent).toContain('AI & Cloud');expect(points[1].textContent).toContain('Cloud Certificate');
    expect(points.map(point=>point.textContent).join(' ')).not.toMatch(/MIL[1-2]/);
    const index=fixture.nativeElement.querySelector('[data-track-index="MILESTONES"]') as HTMLElement;
    expect(index.textContent).toContain('AI & Cloud');expect(index.textContent).toContain('Cloud Certificate');
  });
  it('synchronizes hover and selection between an index row and its timeline mark', async () => {
    const fixture=await renderJourney();
    const mark=fixture.nativeElement.querySelector('[data-entry-id="project-id"].timeline-item') as HTMLButtonElement;
    const row=fixture.nativeElement.querySelector('[data-entry-id="project-id"].event-index-row') as HTMLButtonElement;
    expect(mark.dataset['identifier']).toBe(row.dataset['identifier']);
    row.dispatchEvent(new MouseEvent('mouseenter'));fixture.detectChanges();
    expect(row.classList.contains('active')).toBe(true);expect(mark.classList.contains('active')).toBe(true);
    expect(fixture.nativeElement.querySelector('.preview')?.textContent).toContain('PolicyMeta AI');
    row.dispatchEvent(new MouseEvent('mouseleave'));fixture.detectChanges();
    expect(mark.classList.contains('active')).toBe(false);
    row.dispatchEvent(new FocusEvent('focus'));fixture.detectChanges();
    expect(row.classList.contains('active')).toBe(true);expect(mark.classList.contains('active')).toBe(true);
    row.dispatchEvent(new FocusEvent('blur'));fixture.detectChanges();
    row.getBoundingClientRect=()=>({top:400,right:700,bottom:440,left:200,width:500,height:40,x:200,y:400,toJSON:()=>({})}) as DOMRect;
    row.click();fixture.detectChanges();
    expect(row.classList.contains('selected')).toBe(true);expect(mark.classList.contains('selected')).toBe(true);
    expect(fixture.nativeElement.querySelector('app-journey-detail-inspector')?.dataset['anchorId']).toBe('project-id');
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('Secure regulatory-document backend');
  });
  it('uses actual Education, Experience, Training, and manual titles on timeline surfaces', async () => {
    const experience: PublicJourneyItem = { id:'experience-id',title:'Software Developer Intern',subtitle:'Company',description:'Internship.',occurredAt:'2025-08-01',iconKey:null,sourceType:'EXPERIENCE',sourceId:'experience-source',startAt:'2025-08-01',endAt:null,isOngoing:false,timelineKind:'PERIOD' };
    const training: PublicJourneyItem = { ...journeyTimeline[1], title:'AI Thực Chiến K3' };
    const fixture=await renderJourney([journeyTimeline[0],experience,training,journeyTimeline[3]]);
    const education=fixture.nativeElement.querySelector('[data-track="EDUCATION"] .timeline-item') as HTMLElement;
    const experienceMark=fixture.nativeElement.querySelector('[data-track="EXPERIENCE"] .timeline-item') as HTMLElement;
    const trainingMark=fixture.nativeElement.querySelector('[data-track="TRAINING"] .timeline-item') as HTMLElement;
    const milestone=fixture.nativeElement.querySelector('[data-track="MILESTONES"] .timeline-item') as HTMLElement;
    expect(education.textContent).toContain('Engineer Degree');expect(education.textContent).not.toContain('EDU1');
    expect(experienceMark.textContent).toContain('Software Developer Intern');expect(experienceMark.textContent).toContain('End date not set');expect(experienceMark.textContent).not.toContain('EXP1');
    expect(trainingMark.textContent).toContain('AI Thực Chiến K3');expect(trainingMark.textContent).not.toContain('TRN1');
    expect(milestone.textContent).toContain('AI & Cloud');expect(milestone.textContent).not.toContain('MIL1');
  });
  it('chooses right, left, below, and above anchored-popover fallbacks',()=>{
    expect(calculatePopoverPosition({top:200,right:200,bottom:250,left:100,width:100,height:50},1200,800).placement).toBe('right');
    expect(calculatePopoverPosition({top:200,right:1150,bottom:250,left:1050,width:100,height:50},1200,800).placement).toBe('left');
    expect(calculatePopoverPosition({top:100,right:450,bottom:150,left:350,width:100,height:50},800,800).placement).toBe('below');
    expect(calculatePopoverPosition({top:650,right:450,bottom:700,left:350,width:100,height:50},800,800).placement).toBe('above');
  });
  it('previews on hover and pins complete PERIOD details on click until explicitly closed', async () => {
    const fixture=await renderJourney();
    const project=fixture.nativeElement.querySelector('[data-track="PROJECTS"] .timeline-item') as HTMLButtonElement;
    project.dispatchEvent(new MouseEvent('mouseenter'));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-journey-detail-inspector .preview')?.textContent).toContain('PolicyMeta AI (P-234)');
    project.dispatchEvent(new MouseEvent('mouseleave'));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-journey-detail-inspector')).toBeNull();
    const education=fixture.nativeElement.querySelector('[data-track="EDUCATION"] .timeline-item') as HTMLButtonElement;
    education.getBoundingClientRect=()=>({top:180,right:320,bottom:232,left:180,width:140,height:52,x:180,y:180,toJSON:()=>({})}) as DOMRect;
    education.click();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.timeline-scroll app-journey-detail-inspector')).toBeNull();
    const inspector=fixture.nativeElement.querySelector('app-journey-detail-inspector .pinned') as HTMLElement;
    const inspectorHost=fixture.nativeElement.querySelector('app-journey-detail-inspector') as HTMLElement;
    expect(inspectorHost.dataset['anchorId']).toBe('education-id');
    expect(inspectorHost.dataset['placement']).toBe('right');
    expect(inspectorHost.style.right).toBe('');
    expect(education.classList.contains('selected')).toBe(true);
    expect(inspector.textContent).toContain('Engineer Degree');
    expect(inspector.textContent).toContain('Hanoi University of Civil Engineering');
    expect(inspector.textContent).toContain('Software Engineering education with backend and database focus.');
    expect(inspector.textContent).toContain('Present');
    (inspector.querySelector('.close-button') as HTMLButtonElement).click();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-journey-detail-inspector')).toBeNull();
  });
  it('repositions the anchored inspector when the horizontal timeline scrolls',async()=>{
    const fixture=await renderJourney();
    const project=fixture.nativeElement.querySelector('[data-track="PROJECTS"] .timeline-item') as HTMLButtonElement;
    let left=120;
    project.getBoundingClientRect=()=>({top:180,right:left+80,bottom:232,left,width:80,height:52,x:left,y:180,toJSON:()=>({})}) as DOMRect;
    project.click();fixture.detectChanges();
    const inspector=fixture.nativeElement.querySelector('app-journey-detail-inspector') as HTMLElement;
    const initialLeft=inspector.style.left;
    left=340;
    (fixture.nativeElement.querySelector('.timeline-scroll') as HTMLElement).dispatchEvent(new Event('scroll'));fixture.detectChanges();
    expect(inspector.style.left).not.toBe(initialLeft);
    expect(inspector.dataset['anchorId']).toBe('project-id');
    expect(project.classList.contains('selected')).toBe(true);
  });
  it('keeps mobile tap details in the mobile overlay presentation',async()=>{
    const originalWidth=window.innerWidth;
    Object.defineProperty(window,'innerWidth',{configurable:true,value:600});
    try {
      const fixture=await renderJourney();
      (fixture.nativeElement.querySelector('.timeline-mobile .mobile-card') as HTMLButtonElement).click();fixture.detectChanges();
      const inspector=fixture.nativeElement.querySelector('app-journey-detail-inspector') as HTMLElement;
      expect(inspector.dataset['placement']).toBe('mobile');
      expect(inspector.textContent).toContain('Engineer Degree');
    } finally {
      Object.defineProperty(window,'innerWidth',{configurable:true,value:originalWidth});
    }
  });
  it('opens PERIOD and POINT details with keyboard controls and closes with Escape', async () => {
    const fixture=await renderJourney();
    const project=fixture.nativeElement.querySelector('[data-track="PROJECTS"] .timeline-item') as HTMLButtonElement;
    project.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('Jul 27, 2026');
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('Aug 20, 2026');
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('24 days');
    document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pinned')).toBeNull();
    const point=fixture.nativeElement.querySelector('[data-track="MILESTONES"] .timeline-item') as HTMLButtonElement;
    point.dispatchEvent(new KeyboardEvent('keydown',{key:' ',code:'Space',bubbles:true}));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('AI & Cloud');
    expect(fixture.nativeElement.querySelector('.pinned')?.textContent).toContain('Timeline typePoint');
    expect(point.getAttribute('aria-label')).toContain('View details for AI & Cloud');
  });
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
  it('renders structured section items when contentMarkdown is null', async () => { const detail = { ...projectDetail, sections: [{ id:'section-id',sectionType:'ENGINEERING_FOCUS',title:'Key Backend Architecture',subtitle:null,contentMarkdown:null,content:{items:[{title:'Clean Architecture',description:'Separated application, domain, persistence/infrastructure and presentation concerns.'}]},displayOrder:3 }] }; const { fixture } = await render(of(detail)); const value=fixture.nativeElement.textContent; expect(value).toContain('Key Backend Architecture'); expect(value).toContain('Clean Architecture'); expect(value).toContain('Separated application'); });
  it('allows safe repository links but suppresses unsafe live links', async () => { const { fixture } = await render(of(projectDetail)); expect(fixture.nativeElement.querySelector('a[href="https://example.com/repo"]')?.getAttribute('rel')).toBe('noopener noreferrer'); expect(fixture.nativeElement.textContent).not.toContain('Live project'); });
  it('distinguishes PROJECT_NOT_FOUND', async () => { const { fixture } = await render(throwError(() => new ApiHttpError(404, { code: 'PROJECT_NOT_FOUND', message: 'missing' }))); expect(fixture.nativeElement.textContent).toContain('Project not found'); });
});

describe('ContactPageComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());
  it('copies the API-provided email and preserves published social links without mailto navigation', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const data = { ...portfolio, profile: { ...portfolio.profile, email: 'owner@example.com' }, socialLinks: duplicatePlatformSocialLinks };
    await TestBed.configureTestingModule({ imports: [ContactPageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(ContactPageComponent); fixture.detectChanges();
    const email = fixture.nativeElement.querySelector('.email-copy') as HTMLButtonElement;
    expect(email.tagName).toBe('BUTTON');
    expect(email.getAttribute('aria-label')).toBe('Copy email address owner@example.com');
    email.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(writeText).toHaveBeenCalledOnce();
    expect(writeText).toHaveBeenCalledWith('owner@example.com');
    expect(fixture.nativeElement.textContent).toContain('Email copied');
    expect(fixture.nativeElement.querySelector('a[href^="mailto:"]')).toBeNull();
    const links = [...fixture.nativeElement.querySelectorAll('.contact-links a[href^="https://github.com/"]')] as HTMLAnchorElement[];
    expect(links.map(link => link.querySelector('span')?.textContent?.trim())).toEqual(['GitHub', 'GitHub']);
    expect(links.map(link => link.querySelector('strong')?.textContent?.trim())).toEqual(['CoderPL1005', 'PhucND3009']);
    expect(links.map(link => link.href)).toEqual(['https://github.com/CoderPL1005', 'https://github.com/PhucND3009']);
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeNull();
  });

  it('shows accessible feedback when clipboard copying fails without throwing', async () => {
    const writeText = vi.fn(() => Promise.reject(new Error('Clipboard denied')));
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const data = { ...portfolio, profile: { ...portfolio.profile, email: 'owner@example.com' } };
    await TestBed.configureTestingModule({ imports: [ContactPageComponent], providers: [{ provide: PortfolioStore, useValue: storeWith(data) }] }).compileComponents();
    const fixture = TestBed.createComponent(ContactPageComponent); fixture.detectChanges();

    (fixture.nativeElement.querySelector('.email-copy') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith('owner@example.com');
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Unable to copy email');
  });
});
