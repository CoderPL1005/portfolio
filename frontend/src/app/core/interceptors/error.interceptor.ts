import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ApiError, ApiHttpError } from '../api/api-error.model';

export const errorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      const apiError = readApiError(error) ?? fallbackError(error.status);
      return throwError(() => new ApiHttpError(error.status, apiError));
    }),
  );

function readApiError(response: HttpErrorResponse): ApiError | null {
  const candidate = response.error?.error;
  return candidate && typeof candidate.code === 'string' && typeof candidate.message === 'string'
    ? candidate
    : null;
}

function fallbackError(status: number): ApiError {
  const messages: Record<number, string> = {
    400: 'The request could not be processed.',
    401: 'Authentication is required.',
    403: 'The operation is forbidden.',
    404: 'The requested resource was not found.',
    409: 'The request conflicts with the current state.',
    429: 'Too many requests. Please try again later.',
    500: 'An unexpected server error occurred.',
  };
  return { code: `HTTP_${status || 0}`, message: messages[status] ?? 'The request failed.' };
}
