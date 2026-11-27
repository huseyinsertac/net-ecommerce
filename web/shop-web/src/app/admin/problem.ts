import { HttpErrorResponse } from '@angular/common/http';

export interface ProblemInfo {
  /** The machine code (ProblemDetails `type`), e.g. `catalog.concurrent_update`. */
  code: string;
  /** Human title to show. */
  title: string;
  /** Field messages from a validation problem, flattened. */
  fieldMessages: string[];
  status: number;
}

/** Reads an HTTP failure into what the admin forms show: field messages when present, the title otherwise. */
export function readProblem(
  err: unknown,
  fallback = 'That did not work. Please try again.',
): ProblemInfo {
  if (!(err instanceof HttpErrorResponse)) {
    return { code: '', title: fallback, fieldMessages: [], status: 0 };
  }
  const body = (err.error ?? {}) as { type?: string; title?: string; errors?: Record<string, string[]> };
  const fieldMessages = Object.values(body.errors ?? {}).flat();
  return { code: body.type ?? '', title: body.title || fallback, fieldMessages, status: err.status };
}

/** The line to show for a failure: field messages win over the generic title. */
export function problemText(info: ProblemInfo): string {
  return info.fieldMessages.length > 0 ? info.fieldMessages.join(' ') : info.title;
}
