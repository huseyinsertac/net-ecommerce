import { HttpInterceptorFn } from '@angular/common/http';

/** Must match the API's CsrfHeader (Shop.Infrastructure.Common/Http/CsrfHeader.cs). */
export const CSRF_HEADER = 'X-Requested-With';
export const CSRF_VALUE = 'voltlane';

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

function isSameOriginApi(url: string): boolean {
  try {
    const parsed = new URL(url, location.origin);
    return parsed.origin === location.origin && parsed.pathname.startsWith('/api/');
  } catch {
    return false;
  }
}

/**
 * The API refuses every state-changing /api/ request without `X-Requested-With: voltlane` (400
 * request.missing_csrf_header). Another site cannot add a custom header without a CORS preflight,
 * which the API never grants, so this blocks cross-site form posts. Same-origin only, so the
 * header never leaks to third parties.
 */
export const csrfInterceptor: HttpInterceptorFn = (req, next) =>
  SAFE_METHODS.has(req.method) || !isSameOriginApi(req.url)
    ? next(req)
    : next(req.clone({ setHeaders: { [CSRF_HEADER]: CSRF_VALUE } }));
