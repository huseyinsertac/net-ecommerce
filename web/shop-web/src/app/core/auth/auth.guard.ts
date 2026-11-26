import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { AuthService } from './auth.service';
import { safeReturnUrl } from './return-url';

/** Waits for the initial silent refresh, then lets signed-in users through. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return toObservable(auth.ready).pipe(
    filter(Boolean),
    take(1),
    map(() =>
      auth.user()
        ? true
        : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }),
    ),
  );
};

/** Keeps signed-in visitors off /login and /register: sends them to the sanitized returnUrl or `/`. */
export const guestGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return toObservable(auth.ready).pipe(
    filter(Boolean),
    take(1),
    map(() =>
      auth.user() ? router.parseUrl(safeReturnUrl(route.queryParamMap.get('returnUrl'))) : true,
    ),
  );
};
