import { FormControl, FormGroup, Validators } from '@angular/forms';
import type { AdminSpecDefinition } from '../admin-api.service';

export type SpecValue = number | string | boolean;
export type SpecForm = FormGroup<Record<string, FormControl<SpecValue | null>>>;

/** One control per schema entry, seeded from existing values; required entries get a validator. */
export function buildSpecForm(
  schema: readonly AdminSpecDefinition[],
  values: Record<string, SpecValue> = {},
): SpecForm {
  const controls: Record<string, FormControl<SpecValue | null>> = {};
  for (const def of schema) {
    const initial = values[def.key] ?? (def.type === 'bool' ? false : null);
    // A toggle always has an answer, so it never needs the validator.
    const validators = def.required && def.type !== 'bool' ? [Validators.required] : [];
    controls[def.key] = new FormControl<SpecValue | null>(initial, validators);
  }
  return new FormGroup(controls);
}

/** The specs payload: blank optional fields are left out. */
export function readSpecs(
  schema: readonly AdminSpecDefinition[],
  form: SpecForm,
): Record<string, SpecValue> {
  const specs: Record<string, SpecValue> = {};
  for (const def of schema) {
    const value = form.controls[def.key]?.value;
    if (value === null || value === undefined || value === '') continue;
    specs[def.key] = typeof value === 'string' ? value.trim() : value;
  }
  return specs;
}
