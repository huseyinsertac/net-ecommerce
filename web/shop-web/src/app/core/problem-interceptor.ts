import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

/**
 * Toasts server and network failures. 4xx responses are left to the pages
 * (e.g. 404 product not found, 400 validation.failed).
 */
export const problemInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && (err.status === 0 || err.status >= 500)) {
        const title = (err.error as { title?: string } | null)?.title;
        const fallback =
          err.status === 0 ? 'Cannot reach the server.' : 'Something went wrong on our side.';
        snackBar.open(title || fallback, 'Dismiss', { duration: 6000 });
      }
      return throwError(() => err);
    }),
  );
};
