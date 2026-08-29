import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { duplicatePlatformSocialLinks, portfolio } from '../../features/public/shared/public-test-data';
import { PortfolioStore } from '../../features/public/shared/portfolio.store';
import { PublicLayoutComponent } from './public-layout.component';

describe('PublicLayoutComponent social links', () => {
  beforeEach(() => TestBed.resetTestingModule());

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
});
