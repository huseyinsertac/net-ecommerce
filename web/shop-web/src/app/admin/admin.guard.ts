import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { loginRedirect, whenReady } from '../core/auth/auth.guard';

/**
 * Admin area only (matched before the admin chunk downloads): signed-out visitors go to /login (and come back),
 * signed-in non-admins go home.
 */
export const adminGuard: CanMatchFn = (_route, segments) => {
  const router = inject(Router);
  const target =
    router.currentNavigation()?.extractedUrl.toString() ?? `/${segments.map((s) => s.path).join('/')}`;
  return whenReady((auth) => {
    if (!auth.user()) return loginRedirect(router, target);
    return auth.isAdmin() ? true : router.parseUrl('/');
  });
};
