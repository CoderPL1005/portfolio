import {
  HttpContextToken,
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AuthStore } from '../auth/auth.store';
import { API_BASE_URL } from '../config/api-config';

const AUTH_RETRY = new HttpContextToken(() => false);

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBaseUrl = inject(API_BASE_URL).replace(/\/$/, '');
  if (!isBackendRequest(request, apiBaseUrl)) {
    return next(request);
  }

  const auth = inject(AuthService);
  const store = inject(AuthStore);
  const authorizedRequest = withAccessToken(request, store.accessToken());

  return next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (!shouldRefresh(error, request, apiBaseUrl)) {
        return throwError(() => error);
      }

      return auth.refreshAccessToken().pipe(
        switchMap((accessToken) =>
          next(
            withAccessToken(
              request.clone({ context: request.context.set(AUTH_RETRY, true) }),
              accessToken,
            ),
          ),
        ),
        catchError((refreshError: unknown) => {
          auth.clearSession();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};

function isBackendRequest(request: HttpRequest<unknown>, apiBaseUrl: string): boolean {
  return request.url === apiBaseUrl || request.url.startsWith(`${apiBaseUrl}/`);
}

function withAccessToken(request: HttpRequest<unknown>, accessToken: string | null): HttpRequest<unknown> {
  return accessToken
    ? request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
    : request;
}

function shouldRefresh(error: unknown, request: HttpRequest<unknown>, apiBaseUrl: string): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 401 || request.context.get(AUTH_RETRY)) {
    return false;
  }
  const path = request.url.slice(apiBaseUrl.length).toLowerCase();
  return path !== '/auth/login' && path !== '/auth/refresh';
}
