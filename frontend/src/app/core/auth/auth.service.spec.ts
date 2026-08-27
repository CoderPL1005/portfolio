import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiClientService } from '../api/api-client.service';
import { AuthService } from './auth.service';
import { AuthStore } from './auth.store';

describe('AuthService', () => {
  const api = { get: vi.fn(), post: vi.fn() };
  let service: AuthService;
  let store: AuthStore;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [{ provide: ApiClientService, useValue: api }] });
    service = TestBed.inject(AuthService);
    store = TestBed.inject(AuthStore);
  });

  it('sends the exact login request and accepts the refresh cookie', () => {
    api.post.mockReturnValue(of({ success: true, data: { accessToken: 'token', expiresIn: 900, admin: { id: '1', email: 'admin@example.com', fullName: 'Admin' } } }));

    service.login({ email: 'admin@example.com', password: 'password' }).subscribe();

    expect(api.post).toHaveBeenCalledWith('auth/login', { email: 'admin@example.com', password: 'password' }, { withCredentials: true });
    expect(store.accessToken()).toBe('token');
  });

  it('refreshes with credentials without reading a refresh token', () => {
    api.post.mockReturnValue(of({ success: true, data: { accessToken: 'rotated', expiresIn: 900 } }));

    service.refreshAccessToken().subscribe();

    expect(api.post).toHaveBeenCalledWith('auth/refresh', null, { withCredentials: true });
    expect(store.accessToken()).toBe('rotated');
  });

  it('loads the current-admin contract', () => {
    api.get.mockReturnValue(of({ success: true, data: { id: '1', email: 'admin@example.com', fullName: null, lastLoginAt: null } }));

    service.me().subscribe();

    expect(api.get).toHaveBeenCalledWith('auth/me');
    expect(store.admin()?.email).toBe('admin@example.com');
  });

  it('logs out with credentials and clears local state', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'token');
    api.post.mockReturnValue(of(undefined));

    service.logout().subscribe();

    expect(api.post).toHaveBeenCalledWith('auth/logout', null, { withCredentials: true });
    expect(store.status()).toBe('anonymous');
  });
});
