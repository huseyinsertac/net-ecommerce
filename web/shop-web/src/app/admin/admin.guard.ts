import { CanActivateFn } from '@angular/router';
import { loginRedirect, whenReady } from '../core/auth/auth.guard';

/** Admin area only: signed-out visitors go to /login (and come back), signed-in non-admins go home. */
export const adminGuard: CanActivateFn = (_route, state) =>
  whenReady((auth, router) => {
    if (!auth.user()) return loginRedirect(router, state.url);
    return auth.isAdmin() ? true : router.parseUrl('/');
  });
