import { computed, inject, Injectable, signal } from '@angular/core';
import { AdminSummary, AuthenticationStatus, CurrentAdmin } from './auth.models';
import { TokenStorageService } from './token-storage.service';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly tokenStorage = inject(TokenStorageService);
  private readonly adminState = signal<AdminSummary | CurrentAdmin | null>(null);
  private readonly statusState = signal<AuthenticationStatus>('unknown');

  readonly admin = this.adminState.asReadonly();
  readonly status = this.statusState.asReadonly();
  readonly accessToken = this.tokenStorage.accessToken;
  readonly isAuthenticated = computed(() => this.statusState() === 'authenticated');
  readonly isInitializing = computed(() =>
    this.statusState() === 'unknown' || this.statusState() === 'initializing',
  );

  beginInitialization(): void {
    this.statusState.set('initializing');
  }

  setAccessToken(accessToken: string): void {
    this.tokenStorage.set(accessToken);
  }

  authenticate(admin: AdminSummary | CurrentAdmin, accessToken?: string): void {
    if (accessToken) {
      this.tokenStorage.set(accessToken);
    }
    this.adminState.set(admin);
    this.statusState.set('authenticated');
  }

  clear(): void {
    this.tokenStorage.clear();
    this.adminState.set(null);
    this.statusState.set('anonymous');
  }
}
