import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentService } from '../../features/agent/agent.service';
import { duplicatePlatformSocialLinks, portfolio } from '../../features/public/shared/public-test-data';
import { PortfolioStore } from '../../features/public/shared/portfolio.store';
import { PublicSocialLink } from '../../features/public/shared/public.models';
import { PublicLayoutComponent } from './public-layout.component';

describe('PublicLayoutComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function render(socialLinks: PublicSocialLink[] = []) {
    const store = { data: signal({ ...portfolio, socialLinks }), load: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [PublicLayoutComponent],
      providers: [
        provideRouter([]),
        { provide: PortfolioStore, useValue: store },
        { provide: AgentService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicLayoutComponent);
    fixture.detectChanges();
    return { fixture, store };
  }

  it('refreshes public data on entry and retains the complete ordered social-link list', async () => {
    const store = {
      data: signal({
        ...portfolio,
        socialLinks: [
          ...duplicatePlatformSocialLinks,
          { id: 'linkedin', platform: 'LinkedIn', label: null, url: 'https://linkedin.com/in/example', iconKey: 'linkedin' },
        ],
      }),
      load: vi.fn(),
    };
    TestBed.overrideComponent(PublicLayoutComponent, { set: { template: '', imports: [] } });
    await TestBed.configureTestingModule({
      imports: [PublicLayoutComponent],
      providers: [{ provide: PortfolioStore, useValue: store }],
    }).compileComponents();

    const component = TestBed.createComponent(PublicLayoutComponent).componentInstance;

    expect(store.load).toHaveBeenCalledWith(true);
    expect(component.socialLinks().map(link => link.id)).toEqual([
      'github-coderpl1005', 'github-phucnd3009', 'linkedin',
    ]);
  });

  it('uses the same focused public navigation on desktop and mobile and exposes no Admin footer link', async () => {
    const { fixture } = await render();
    const expected = ['Home', 'Experience', 'Projects', 'Skills', 'Journey', 'Contact'];
    const labels = (selector: string) => [...fixture.nativeElement.querySelectorAll(selector)]
      .map((link: HTMLElement) => link.textContent?.trim());

    expect(labels('.desktop-nav a')).toEqual(expected);
    expect(labels('.desktop-nav a')).not.toContain('About');
    fixture.componentInstance.toggleMenu();
    fixture.detectChanges();
    expect(labels('.mobile-nav a')).toEqual(expected);
    expect(fixture.nativeElement.querySelector('.public-footer a[href^="/admin"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.public-footer')?.textContent).not.toContain('Admin');
  });

  it('renders duplicate GitHub links with mapped icons, distinct destinations and accessible labels', async () => {
    const links: PublicSocialLink[] = [
      { id:'github-dotnet',platform:'GitHub',label:'GitHub For .NET',url:'https://github.com/CoderPL1005',iconKey:'github' },
      { id:'github-ai',platform:'GitHub',label:'GitHub For AI',url:'https://github.com/PhucND3009',iconKey:'github' },
      { id:'unknown',platform:'Mastodon',label:null,url:'https://example.com/@portfolio',iconKey:'mastodon' },
    ];
    const { fixture } = await render(links);
    const anchors=[...fixture.nativeElement.querySelectorAll('.social-actions a')] as HTMLAnchorElement[];
    const github=anchors.slice(0,2);

    expect(github).toHaveLength(2);
    expect(github.map(link=>link.href)).toEqual(['https://github.com/CoderPL1005','https://github.com/PhucND3009']);
    expect(github.map(link=>link.getAttribute('aria-label'))).toEqual(['GitHub For .NET','GitHub For AI']);
    expect(github.map(link=>link.title)).toEqual(['GitHub For .NET','GitHub For AI']);
    expect(github.every(link=>link.querySelector('svg[data-icon-key="github"]')!==null)).toBe(true);
    expect(github.every(link=>link.textContent?.trim()!=='G')).toBe(true);
    expect(anchors[2].querySelector('[data-icon-fallback="true"]')?.textContent).toBe('M');
  });
});
