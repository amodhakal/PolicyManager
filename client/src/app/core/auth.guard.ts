import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

/**
 * Sends an unauthenticated visitor to the token page rather than letting a list load and fail.
 *
 * The API answers 401 on every endpoint, so without this the first thing a new user sees is five red
 * banners. A guard is still not a security control — the token is in `localStorage` and the client
 * is not trusted — it only saves the user a round trip to find out they need a token.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAuthenticated()
    ? true
    : router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
