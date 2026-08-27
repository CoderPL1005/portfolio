import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ApiHttpError } from '../api/api-error.model';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
    ] });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  it('preserves the backend API error code and validation details', () => {
    let received: unknown;
    http.get('/api').subscribe({ error: (error) => (received = error) });
    controller.expectOne('/api').flush(
      { success: false, error: { code: 'VALIDATION_ERROR', message: 'Invalid.', details: { email: ['Required.'] } } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(received).toBeInstanceOf(ApiHttpError);
    expect((received as ApiHttpError).apiError.code).toBe('VALIDATION_ERROR');
    expect((received as ApiHttpError).apiError.details?.['email']).toEqual(['Required.']);
  });

  it('uses a safe fallback instead of exposing an unknown raw response', () => {
    let received: ApiHttpError | undefined;
    http.get('/api').subscribe({ error: (error) => (received = error) });
    controller.expectOne('/api').flush('internal provider detail', { status: 500, statusText: 'Error' });

    expect(received?.apiError.message).toBe('An unexpected server error occurred.');
    expect(received?.message).not.toContain('provider detail');
  });
});
