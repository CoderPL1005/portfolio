import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthStore } from '../auth/auth.store';
import { API_BASE_URL } from '../config/api-config';
import { authInterceptor } from './auth.interceptor';
import { HttpClient } from '@angular/common/http';

describe('authInterceptor', () => {
  const baseUrl = 'http://localhost:5000/api/v1';
  let http: HttpClient;
  let controller: HttpTestingController;
  let store: AuthStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: baseUrl },
    ] });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
  });

  it('adds the Bearer token only to backend requests', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'secret-token');
    http.get(`${baseUrl}/admin/dashboard`).subscribe();
    http.get('https://external.example/data').subscribe();

    const backend = controller.expectOne(`${baseUrl}/admin/dashboard`);
    const external = controller.expectOne('https://external.example/data');
    expect(backend.request.headers.get('Authorization')).toBe('Bearer secret-token');
    expect(external.request.headers.has('Authorization')).toBe(false);
    backend.flush({});
    external.flush({});
  });

  it('refreshes once and retries the original request with the rotated token', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'expired');
    let result: unknown;
    http.get(`${baseUrl}/admin/dashboard`).subscribe((value) => (result = value));
    controller.expectOne(`${baseUrl}/admin/dashboard`).flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = controller.expectOne(`${baseUrl}/auth/refresh`);
    expect(refresh.request.withCredentials).toBe(true);
    refresh.flush({ success: true, data: { accessToken: 'rotated', expiresIn: 900 } });
    const retry = controller.expectOne(`${baseUrl}/admin/dashboard`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer rotated');
    retry.flush({ value: 42 });

    expect(result).toEqual({ value: 42 });
  });

  it('shares one refresh request across concurrent 401 responses', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'expired');
    http.get(`${baseUrl}/admin/one`).subscribe();
    http.get(`${baseUrl}/admin/two`).subscribe();
    controller.expectOne(`${baseUrl}/admin/one`).flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(`${baseUrl}/admin/two`).flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = controller.expectOne(`${baseUrl}/auth/refresh`);
    refresh.flush({ success: true, data: { accessToken: 'rotated', expiresIn: 900 } });
    controller.expectOne(`${baseUrl}/admin/one`).flush({});
    controller.expectOne(`${baseUrl}/admin/two`).flush({});
    controller.verify();
  });

  it('does not recursively refresh the refresh endpoint and clears auth on refresh failure', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'expired');
    http.get(`${baseUrl}/admin/dashboard`).subscribe({ error: () => undefined });
    controller.expectOne(`${baseUrl}/admin/dashboard`).flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(`${baseUrl}/auth/refresh`).flush({}, { status: 401, statusText: 'Unauthorized' });

    controller.expectNone(`${baseUrl}/auth/refresh`);
    expect(store.status()).toBe('anonymous');
  });

  it('does not attempt refresh recursion for a failed login request', () => {
    http.post(`${baseUrl}/auth/login`, {}).subscribe({ error: () => undefined });

    controller.expectOne(`${baseUrl}/auth/login`).flush({}, { status: 401, statusText: 'Unauthorized' });

    controller.expectNone(`${baseUrl}/auth/refresh`);
  });
});
