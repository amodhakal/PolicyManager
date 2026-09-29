import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { AuthService } from './auth.service';
import { toApiError } from './problem-details';

/**
 * Attaches the bearer token to every API call and normalizes failures into `ApiError`.
 *
 * One interceptor rather than one per service, so a new feature cannot forget the token and every
 * caller gets the same error shape. The body is parsed by `toApiError`, which tolerates a non-JSON
 * error body: a proxy's HTML 502 and the API's problem details then both arrive as an `ApiError`
 * rather than one of them arriving as a parse error.
 */
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.token();

  const authorized =
    token !== null ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        // 0 is the status for a request that never reached the server — a CORS rejection or a
        // refused connection — so there is no problem details to parse and the status would read as
        // a meaningless zero.
        const status = error.status === 0 ? 0 : error.status;
        const fallback =
          status === 0
            ? 'Could not reach the API. Check that it is running and that the dev proxy points at it.'
            : `The API answered ${status}.`;
        return throwError(() => toApiError(status, error.error, fallback));
      }
      return throwError(() => error);
    }),
  );
};
