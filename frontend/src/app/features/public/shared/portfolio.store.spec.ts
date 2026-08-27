import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { PortfolioStore } from './portfolio.store';
import { portfolio } from './public-test-data';
import { PublicPortfolioService } from './public-portfolio.service';

describe('PortfolioStore', () => {
  const api = { getPortfolio: vi.fn() }; let store: PortfolioStore;
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [{ provide: PublicPortfolioService, useValue: api }] }); store = TestBed.inject(PortfolioStore); });
  it('starts idle without data', () => { expect(store.status()).toBe('idle'); expect(store.data()).toBeNull(); });
  it('loads the aggregate successfully', () => { api.getPortfolio.mockReturnValue(of(portfolio)); store.load(); expect(store.status()).toBe('loaded'); expect(store.data()).toBe(portfolio); });
  it('reuses an in-flight and loaded request', () => { const pending = new Subject(); api.getPortfolio.mockReturnValue(pending); store.load(); store.load(); expect(api.getPortfolio).toHaveBeenCalledTimes(1); pending.next(portfolio); pending.complete(); store.load(); expect(api.getPortfolio).toHaveBeenCalledTimes(1); });
  it('stores normalized errors and retries', () => { api.getPortfolio.mockReturnValueOnce(throwError(() => new ApiHttpError(500, { code: 'FAILED', message: 'Safe message' }))).mockReturnValueOnce(of(portfolio)); store.load(); expect(store.status()).toBe('error'); expect(store.error()).toBe('Safe message'); store.retry(); expect(store.status()).toBe('loaded'); expect(api.getPortfolio).toHaveBeenCalledTimes(2); });
});
