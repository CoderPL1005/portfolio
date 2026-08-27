import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { AuthStore } from '../auth/auth.store';
import { adminAuthGuard } from './admin-auth.guard';
import { adminLoginGuard } from './admin-login.guard';

describe('admin guards', () => {
  const router = { createUrlTree: vi.fn((commands: string[], extras?: unknown) => ({ commands, extras })) };
  const auth = { restoreSession: vi.fn() };
  let store: AuthStore;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [
      { provide: Router, useValue: router },
      { provide: AuthService, useValue: auth },
    ] });
    store = TestBed.inject(AuthStore);
  });

  it('redirects an initialized anonymous admin visitor to login', () => {
    store.clear();

    const result = TestBed.runInInjectionContext(() =>
      adminAuthGuard({} as ActivatedRouteSnapshot, { url: '/admin/projects' } as RouterStateSnapshot),
    );

    expect(result).toEqual({ commands: ['/admin/login'], extras: { queryParams: { returnUrl: '/admin/projects' } } });
    expect(auth.restoreSession).not.toHaveBeenCalled();
  });

  it('restores an initializing session before allowing an admin route', async () => {
    store.beginInitialization();
    auth.restoreSession.mockReturnValue(of(true));

    const result = TestBed.runInInjectionContext(() =>
      adminAuthGuard({} as ActivatedRouteSnapshot, { url: '/admin' } as RouterStateSnapshot),
    );

    expect(isObservable(result)).toBe(true);
    expect(await firstValueFrom(result as ReturnType<typeof auth.restoreSession>)).toBe(true);
  });

  it('redirects an authenticated admin away from the login route', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'token');

    const result = TestBed.runInInjectionContext(() =>
      adminLoginGuard({} as ActivatedRouteSnapshot, { url: '/admin/login' } as RouterStateSnapshot),
    );

    expect(result).toEqual({ commands: ['/admin'], extras: undefined });
  });
});
