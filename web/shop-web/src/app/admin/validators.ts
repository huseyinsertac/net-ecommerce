import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Matches the server's `MaxDescriptionLength`. */
export const MAX_DESCRIPTION_LENGTH = 4000;

/** A money amount carries at most two decimal places (empty passes; `required` is separate). */
export function maxTwoDecimals(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (value === null || value === undefined || value === '') return null;
  return Math.abs(Math.round(Number(value) * 100) - Number(value) * 100) < 1e-6
    ? null
    : { decimals: true };
}
