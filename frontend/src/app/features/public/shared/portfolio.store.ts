import { inject, Injectable, signal } from '@angular/core';
import { take } from 'rxjs';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { PortfolioAggregate } from './public.models';
import { PublicPortfolioService } from './public-portfolio.service';

export type PortfolioLoadStatus = 'idle' | 'loading' | 'loaded' | 'error';

@Injectable({ providedIn: 'root' })
export class PortfolioStore {
  private readonly api = inject(PublicPortfolioService);
  private readonly dataState = signal<PortfolioAggregate | null>(null);
  private readonly statusState = signal<PortfolioLoadStatus>('idle');
  private readonly errorState = signal<string | null>(null);

  readonly data = this.dataState.asReadonly();
  readonly status = this.statusState.asReadonly();
  readonly error = this.errorState.asReadonly();

  load(force = false): void {
    if (!force && (this.statusState() === 'loading' || this.statusState() === 'loaded')) return;
    this.statusState.set('loading');
    this.errorState.set(null);
    this.api.getPortfolio().pipe(take(1)).subscribe({
      next: (portfolio) => {
        this.dataState.set(portfolio);
        this.statusState.set('loaded');
      },
      error: (error: unknown) => {
        this.errorState.set(error instanceof ApiHttpError ? error.apiError.message : 'Unable to load the portfolio.');
        this.statusState.set('error');
      },
    });
  }

  retry(): void { this.load(true); }
}
