import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, filter, map, take } from 'rxjs';
import { AuthService } from './auth.service';
import { safeReturnUrl } from './return-url';

/** Waits for the initial silent refresh, then lets `decide` pick between letting the visitor in or redirecting. */
export function whenReady(
  decide: (auth: AuthService, router: Router) => boolean | UrlTree,
): Observable<boolean | UrlTree> {
  const auth = inject(AuthService);
  const router = inject(Router);
  return toObservable(auth.ready).pipe(
    filter(Boolean),
    take(1),
    map(() => decide(auth, router)),
  );
}

/** Sends a signed-out visitor to /login, remembering where they were headed. */
export function loginRedirect(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/login'], { queryParams: { returnUrl } });
}

/** Waits for the initial silent refresh, then lets signed-in users through. */
export const authGuard: CanActivateFn = (_route, state) =>
  whenReady((auth, router) => (auth.user() ? true : loginRedirect(router, state.url)));

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
