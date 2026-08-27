import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiClientService } from '../../../core/api/api-client.service';
import { portfolio, project, projectDetail } from './public-test-data';
import { PublicPortfolioService } from './public-portfolio.service';

describe('PublicPortfolioService', () => {
  const api = { get: vi.fn(), post: vi.fn() }; let service: PublicPortfolioService;
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [{ provide: ApiClientService, useValue: api }] }); service = TestBed.inject(PublicPortfolioService); });
  it('requests the aggregate', () => { api.get.mockReturnValue(of({ success: true, data: portfolio })); service.getPortfolio().subscribe(value => expect(value).toBe(portfolio)); expect(api.get).toHaveBeenCalledWith('public/portfolio'); });
  it('requests projects and supported featured filtering', () => { api.get.mockReturnValue(of({ success: true, data: [project] })); service.getProjects(true).subscribe(); expect(api.get.mock.calls[0][0]).toBe('public/projects'); expect(api.get.mock.calls[0][1].params.get('featured')).toBe('true'); });
  it('encodes a project slug', () => { api.get.mockReturnValue(of({ success: true, data: projectDetail })); service.getProject('a/b').subscribe(); expect(api.get).toHaveBeenCalledWith('public/projects/a%2Fb'); });
  it('submits the contact contract', () => { const request = { name: 'A', email: 'a@example.com', subject: null, message: 'Hi' }; api.post.mockReturnValue(of({ success: true, data: { id: '1', status: 'NEW' } })); service.submitContact(request).subscribe(); expect(api.post).toHaveBeenCalledWith('public/contact', request); });
});
