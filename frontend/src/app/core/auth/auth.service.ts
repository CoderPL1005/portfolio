import { inject, Injectable } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { ApiClientService } from '../api/api-client.service';
import { ApiResponse } from '../api/api-response.model';
import { AdminSummary, CurrentAdmin, LoginRequest, LoginResponse, TokenResponse } from './auth.models';
import { AuthStore } from './auth.store';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiClientService);
  private readonly store = inject(AuthStore);
  private refreshInFlight$: Observable<string> | null = null;
  private restorationInFlight$: Observable<boolean> | null = null;

  login(request: LoginRequest): Observable<AdminSummary> {
    return this.api
      .post<ApiResponse<LoginResponse>>('auth/login', request, { withCredentials: true })
      .pipe(
        map((response) => this.requireData(response)),
        tap((result) => this.store.authenticate(result.admin, result.accessToken)),
        map((result) => result.admin),
      );
  }

  refreshAccessToken(): Observable<string> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.api
        .post<ApiResponse<TokenResponse>>('auth/refresh', null, { withCredentials: true })
        .pipe(
          map((response) => this.requireData(response).accessToken),
          tap((token) => this.store.setAccessToken(token)),
          finalize(() => (this.refreshInFlight$ = null)),
          shareReplay({ bufferSize: 1, refCount: false }),
        );
    }
    return this.refreshInFlight$;
  }

  me(): Observable<CurrentAdmin> {
    return this.api.get<ApiResponse<CurrentAdmin>>('auth/me').pipe(
      map((response) => this.requireData(response)),
      tap((admin) => this.store.authenticate(admin)),
    );
  }

  restoreSession(): Observable<boolean> {
    if (this.store.isAuthenticated()) {
      return of(true);
    }
    if (!this.restorationInFlight$) {
      this.store.beginInitialization();
      this.restorationInFlight$ = this.refreshAccessToken().pipe(
        switchMap(() => this.me()),
        map(() => true),
        catchError(() => {
          this.store.clear();
          return of(false);
        }),
        finalize(() => (this.restorationInFlight$ = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.restorationInFlight$;
  }

  logout(): Observable<void> {
    return this.api.post<void>('auth/logout', null, { withCredentials: true }).pipe(
      tap(() => this.store.clear()),
      catchError((error) => {
        this.store.clear();
        return throwError(() => error);
      }),
    );
  }

  clearSession(): void {
    this.store.clear();
  }

  private requireData<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data === undefined) {
      throw new Error('The API returned an invalid success response.');
    }
    return response.data;
  }
}
