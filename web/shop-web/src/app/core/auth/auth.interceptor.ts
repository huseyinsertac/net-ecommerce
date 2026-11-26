import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const NO_BEARER = /^\/api\/identity\/(login|register|refresh|logout|demo\/)/;

/** Same-origin only: a relative `/api/` URL, or an absolute one on this page's origin. */
function wantsBearer(url: string): boolean {
  let parsed: URL;
  try {
    parsed = new URL(url, location.origin);
  } catch {
    return false;
  }
  return (
    parsed.origin === location.origin &&
    parsed.pathname.startsWith('/api/') &&
    !NO_BEARER.test(parsed.pathname)
  );
}

const withToken = <T>(req: HttpRequest<T>, token: string) =>
  req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

/**
 * Adds the bearer to API calls. On a 401 for a request that carried one, refreshes once (shared
 * with any parallel 401s) and retries once. A failed refresh rethrows the original error; the
 * retry is not itself retried, so this can never loop.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();
  if (!token || !wantsBearer(req.url)) return next(req);

  return next(withToken(req, token)).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401) return throwError(() => err);
      return from(
        // Another request may already have rotated the token while this one was in flight.
        auth.accessToken() !== token ? Promise.resolve(true) : auth.refresh(),
      ).pipe(
        switchMap((ok) => {
          const fresh = auth.accessToken();
          if (!ok || !fresh) return throwError(() => err);
          return next(withToken(req, fresh));
        }),
      );
    }),
  );
};
