import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const NO_BEARER = /^\/api\/identity\/(login|register|refresh|logout|demo\/)/;

function path(url: string): string {
  return url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
}

function wantsBearer(url: string): boolean {
  const p = path(url);
  return p.startsWith('/api/') && !NO_BEARER.test(p);
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
